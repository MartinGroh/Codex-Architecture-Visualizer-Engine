"""Normalize Codex hook input into CAVE's repository-local evidence streams.

Activity evidence always remains privacy-minimized. Public user prompts and final
assistant messages are copied to a separate bounded journal only after the exact
workspace explicitly enables conversation sharing. System/developer messages,
tool inputs, tool responses, commands, transcripts, and reasoning are never copied.
"""

from __future__ import annotations

import json
import hashlib
import os
from pathlib import Path
import re
import sys
import time
from contextlib import contextmanager
from datetime import datetime, timedelta, timezone
from typing import Any, Iterable
from uuid import uuid4


SOURCE_EXTENSIONS = (
    "csproj|slnx|tsx|jsx|json|razor|props|targets|yaml|toml|cpp|hpp|"
    "cs|sln|ts|js|py|go|rs|java|h|"
    "md|css|html|xml|ps1|cmd|yml|sh"
)
ABSOLUTE_PATH = re.compile(
    rf"(?P<path>[A-Za-z]:[\\/][^\r\n\"']+?\.(?:{SOURCE_EXTENSIONS}))"
    r"(?=$|[\r\n\"'`,;)\]}])",
    re.IGNORECASE,
)
RELATIVE_PATH = re.compile(
    rf"(?<![\\/])(?P<path>(?:[A-Za-z0-9_.@+-]+[\\/])+"
    rf"[A-Za-z0-9_.@+-]+\.(?:{SOURCE_EXTENSIONS}))"
    r"(?=$|[\r\n\"'`,;)\]}])",
    re.IGNORECASE,
)
PATCH_PATH = re.compile(
    r"^\*\*\* (?:Add|Update|Delete) File:\s*(?P<path>.+?)\s*$",
    re.MULTILINE,
)
PATH_KEYS = {"file", "filepath", "file_path", "path", "paths"}
ROOT_MARKERS = (".git", ".codegraph", "CAVE.slnx")
MAX_RETAINED_EVENTS = 2048
MAX_RETAINED_CONVERSATION_MESSAGES = 512
MAX_CONVERSATION_MESSAGE_CHARACTERS = 100_000
MAX_BROWSER_MESSAGES_PER_CONTINUATION = 8
CAVE_BROWSER_CONTINUATION_PREFIX = "CAVE_BROWSER_MESSAGES_V1\n"
CAVE_OWNER_HOOK_INSTANCE_ID = "codex-owner-hook"
IN_APP_BROWSER_CONTEXT = re.compile(
    r'^<in-app-browser-context\s+source=["\']ambient-ui-state["\']>\s*\n'
    r'.*?\n</in-app-browser-context>\s*',
    re.DOTALL,
)
MY_REQUEST_HEADING = re.compile(r'^## My request:\s*', re.IGNORECASE)
PRUNE_EVENT_KINDS = {"SessionStart", "SessionEnd", "UserPromptSubmit"}
TERMINAL_EVENT_KINDS = {"SessionEnd", "SubagentStop", "Stop", "Interrupt"}
THINKING_EVENT_KINDS = {"SessionStart", "SubagentStart", "UserPromptSubmit"}
THINKING_TOOL_NAMES = {"update_plan", "request_user_input"}
MUTATION_TOOL_NAMES = {
    "apply_patch", "write", "edit", "write_file", "create_file", "delete_file",
}
VALIDATION_ACTIVITY = re.compile(
    r"(?:\bdotnet\s+(?:build|test|format)\b|"
    r"\b(?:npm|pnpm|yarn)\b[^\r\n]*(?:\btest\b|\blint\b|\bbuild\b)|"
    r"\b(?:pytest|vitest|jest|playwright)\b|"
    r"\b(?:verify|test|build|lint)(?:[-_.][\w.-]+)?\.(?:ps1|cmd|sh)\b|"
    r"\binstall-local-plugin\.ps1\b|"
    r"\bcodegraph\s+status\b|"
    r"\bhealth\b)",
    re.IGNORECASE,
)
READ_ACTIVITY = re.compile(
    r"(?:\bget-content\b|\bselect-string\b|\brg\b|"
    r"\bgit\s+(?:status|diff|log|show|branch)\b|"
    r"\bcodegraph\s+(?:explore|node|query)\b|"
    r"(?:^|__)(?:read|search|find|open|list|view|query|explore|get)(?:_|$)|"
    r"\bweb__run\b)",
    re.IGNORECASE,
)


def main() -> int:
    try:
        payload = json.load(sys.stdin)
        cwd = Path(str(payload.get("cwd") or os.getcwd())).resolve()
        candidates = extract_path_candidates(payload.get("tool_input"))
        roots = discover_roots(cwd, candidates)
        if not roots:
            return 0
        if any(is_cave_ephemeral_session(root, payload) for root in roots):
            # CAVE node memos run in App Server ephemeral forks. Their prompt, activity,
            # and answer belong only to the invoking modal, never to shared workspace state.
            return 0

        occurred_at = datetime.now(timezone.utc)
        event_id = uuid4().hex
        phase = activity_phase_for(payload)
        hook_response: dict[str, Any] | None = None
        for root in roots:
            try:
                register_workspace(root, occurred_at, event_id)
            except Exception as error:  # Registration must not suppress repository-local activity evidence.
                print(f"CAVE workspace registration failed for '{root}': {error}", file=sys.stderr)
            relative_paths = paths_for_root(root, cwd, candidates)
            event = {
                "schemaVersion": 1,
                "eventId": event_id,
                "kind": str(payload.get("hook_event_name") or "Unknown"),
                "occurredAtUtc": occurred_at.isoformat().replace("+00:00", "Z"),
                "sessionId": optional_text(payload.get("session_id")),
                "turnId": optional_text(payload.get("turn_id")),
                "agentId": optional_text(payload.get("agent_id")),
                "agentType": optional_text(payload.get("agent_type")),
                "isSubagent": bool(payload.get("agent_id")),
                "workspaceRoot": str(root),
                "toolName": optional_text(payload.get("tool_name")),
                "isMutation": is_mutation(payload),
                "phase": phase,
                "paths": relative_paths,
                "summary": summary_for(payload, relative_paths, phase),
            }
            append_event(root, occurred_at, event_id, event)
            append_conversation_message_if_enabled(root, occurred_at, event_id, payload)
            synchronize_answered_owner_hook_deliveries(root, occurred_at, payload)
            owner_response = claim_browser_messages_for_stop(root, occurred_at, payload)
            if owner_response is not None:
                hook_response = owner_response
            if event["kind"] in PRUNE_EVENT_KINDS:
                prune_events(root)

        response = hook_response or hook_response_for(payload)
        if response is not None:
            json.dump(response, sys.stdout, separators=(",", ":"))
        return 0
    except Exception as error:  # Hooks must fail open while surfacing diagnostics.
        print(f"CAVE activity hook failed: {error}", file=sys.stderr)
        return 0


def is_cave_ephemeral_session(root: Path, payload: dict[str, Any]) -> bool:
    session_id = optional_text(payload.get("session_id"))
    if session_id is None:
        return False
    marker_name = hashlib.sha256(session_id.encode("utf-8")).hexdigest() + ".json"
    return (root / ".cave" / "conversation" / "ephemeral" / marker_name).is_file()


def extract_path_candidates(value: Any) -> list[str]:
    candidates: set[str] = set()

    def visit(item: Any, key: str | None = None) -> None:
        if isinstance(item, dict):
            for child_key, child_value in item.items():
                visit(child_value, str(child_key).lower())
            return
        if isinstance(item, list):
            for child in item:
                visit(child, key)
            return
        if not isinstance(item, str):
            return

        if key in PATH_KEYS and looks_like_source_path(item):
            candidates.add(clean_candidate(item))
        for match in PATCH_PATH.finditer(item):
            candidates.add(clean_candidate(match.group("path")))

        absolute_matches = list(ABSOLUTE_PATH.finditer(item))
        for match in absolute_matches:
            candidates.add(clean_candidate(match.group("path")))

        for match in RELATIVE_PATH.finditer(item):
            if any(
                match.start() >= absolute.start() and match.end() <= absolute.end()
                for absolute in absolute_matches
            ):
                continue
            candidates.add(clean_candidate(match.group("path")))

    visit(value)
    return sorted(candidate for candidate in candidates if candidate)


def discover_roots(cwd: Path, candidates: Iterable[str]) -> list[Path]:
    roots: set[Path] = set()
    cwd_root = nearest_root(cwd)
    if cwd_root is not None:
        roots.add(cwd_root)

    for candidate in candidates:
        resolved = resolve_candidate(cwd, candidate)
        candidate_root = nearest_root(resolved.parent)
        if candidate_root is not None:
            roots.add(candidate_root)

    if not roots and cwd.is_dir():
        for child in cwd.iterdir():
            if child.is_dir() and has_root_marker(child):
                roots.add(child.resolve())

    return sorted(roots, key=lambda path: str(path).lower())


def nearest_root(start: Path) -> Path | None:
    current = start if start.is_dir() else start.parent
    for candidate in (current, *current.parents):
        if has_root_marker(candidate):
            return candidate.resolve()
    return None


def has_root_marker(path: Path) -> bool:
    return any((path / marker).exists() for marker in ROOT_MARKERS)


def paths_for_root(root: Path, cwd: Path, candidates: Iterable[str]) -> list[str]:
    relative: set[str] = set()
    for candidate in candidates:
        resolved = resolve_candidate(cwd, candidate)
        try:
            candidate_relative = resolved.relative_to(root)
        except ValueError:
            continue
        relative.add(candidate_relative.as_posix())
    return sorted(relative, key=str.lower)


def resolve_candidate(cwd: Path, candidate: str) -> Path:
    path = Path(candidate.replace("\\", os.sep).replace("/", os.sep))
    return (path if path.is_absolute() else cwd / path).resolve(strict=False)


def looks_like_source_path(value: str) -> bool:
    return re.search(rf"\.(?:{SOURCE_EXTENSIONS})$", value.strip(), re.IGNORECASE) is not None


def clean_candidate(value: str) -> str:
    return value.strip().strip("`\"' ,;()[]{}")


def optional_text(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text or None


def public_user_prompt(value: Any) -> str | None:
    """Return user-authored prompt text without Codex host context envelopes.

    Codex can prepend an ambient browser-state block before the real request. That
    block is host metadata rather than public user conversation and must neither be
    retained nor presented as the project instruction root. Only the exact leading
    host envelope is removed so user-authored markup elsewhere remains untouched.
    """
    text = optional_text(value)
    if text is None:
        return None

    removed_host_context = False
    while True:
        match = IN_APP_BROWSER_CONTEXT.match(text)
        if match is None:
            break
        text = text[match.end():].lstrip()
        removed_host_context = True

    if removed_host_context:
        text = MY_REQUEST_HEADING.sub("", text, count=1)
    return optional_text(text)


def summary_for(
    payload: dict[str, Any],
    paths: list[str],
    phase: str | None,
) -> str:
    kind = str(payload.get("hook_event_name") or "Activity")
    tool_name = optional_text(payload.get("tool_name"))
    if paths and phase:
        return f"{phase}: {paths[0]}"
    if phase:
        return {
            "Thinking": "Thinking through the current instruction",
            "Reading": "Reading workspace evidence",
            "Editing": "Editing the workspace",
            "Validating": "Validating the current work",
            "Working": f"Using {tool_name}" if tool_name else "Working",
        }[phase]
    return {
        "SessionStart": "Session started",
        "SessionEnd": "Session completed",
        "UserPromptSubmit": "New instruction received",
        "SubagentStart": "Subagent started",
        "SubagentStop": "Subagent completed",
        "Stop": "Waiting for the next turn",
        "Interrupt": "Turn interrupted",
    }.get(kind, kind)


def activity_phase_for(payload: dict[str, Any]) -> str | None:
    """Classify only activity metadata; never persist the inspected tool input."""
    kind = str(payload.get("hook_event_name") or "")
    if kind in TERMINAL_EVENT_KINDS:
        return None
    if kind in THINKING_EVENT_KINDS:
        return "Thinking"

    tool_name = str(payload.get("tool_name") or "").strip()
    if not tool_name:
        return "Working"
    normalized_tool_name = tool_name.lower()
    serialized_tool_input = serialize_tool_input(payload)
    if normalized_tool_name in THINKING_TOOL_NAMES:
        return "Thinking"
    if is_mutation(payload, serialized_tool_input):
        return "Editing"
    if normalized_tool_name == "functions.exec" and any(
        f"tools.{thinking_tool}(" in serialized_tool_input
        for thinking_tool in THINKING_TOOL_NAMES
    ):
        return "Thinking"

    searchable = f"{tool_name}\n{serialized_tool_input}"
    if VALIDATION_ACTIVITY.search(searchable):
        return "Validating"
    if READ_ACTIVITY.search(searchable):
        return "Reading"
    return "Working"


def hook_response_for(payload: dict[str, Any]) -> dict[str, bool] | None:
    """Return the valid no-op response required by Codex stop-family hooks."""
    kind = str(payload.get("hook_event_name") or "")
    return {"continue": True} if kind in {"Stop", "SubagentStop"} else None


def claim_browser_messages_for_stop(
    root: Path,
    occurred_at: datetime,
    payload: dict[str, Any],
) -> dict[str, str] | None:
    """Hand queued browser messages to the Codex process that already owns the task.

    A separate App Server process cannot resume a task while Codex Desktop owns its
    writer. The synchronous Stop hook runs inside that owner and can ask Codex to
    continue with a generated user prompt. Claim only durable messages bound to the
    exact session; other tasks and in-flight deliveries remain untouched.
    """
    if str(payload.get("hook_event_name") or "") != "Stop":
        return None

    session_id = optional_text(payload.get("session_id"))
    if session_id is None:
        return None

    outbox = root / ".cave" / "conversation" / "control" / "outbox"
    if not outbox.is_dir():
        return None

    claimed: list[dict[str, str]] = []
    try:
        with conversation_control_lock(root):
            if has_running_owner_hook_delivery(outbox, session_id):
                return None
            for path in sorted(outbox.glob("*.json"), key=lambda candidate: candidate.name):
                if len(claimed) >= MAX_BROWSER_MESSAGES_PER_CONTINUATION:
                    break
                try:
                    record = json.loads(path.read_text(encoding="utf-8"))
                except (OSError, json.JSONDecodeError):
                    continue
                if (
                    record.get("schemaVersion") != 1
                    or record.get("sessionId") != session_id
                    or record.get("state") != "Queued"
                ):
                    continue

                message_id = optional_text(record.get("messageId"))
                text = optional_text(record.get("text"))
                if message_id is None or text is None:
                    continue

                updated = dict(record)
                updated.update({
                    "turnId": optional_text(payload.get("turn_id")),
                    "state": "Running",
                    "updatedAtUtc": occurred_at.isoformat().replace("+00:00", "Z"),
                    "bridgeInstanceId": CAVE_OWNER_HOOK_INSTANCE_ID,
                })
                updated.pop("error", None)
                write_json_atomic(path, updated)
                append_browser_conversation_message_if_enabled(
                    root,
                    occurred_at + timedelta(microseconds=len(claimed)),
                    message_id,
                    payload,
                    text,
                )
                claimed.append({"messageId": message_id, "text": text})
    except TimeoutError:
        # The App Server bridge is claiming the queue. It will either deliver the
        # message or leave a durable recovery record; the owner hook must not race it.
        return None

    if not claimed:
        return None

    write_conversation_control_status(
        root,
        occurred_at,
        payload,
        state="Running",
        can_send=False,
        owner=CAVE_OWNER_HOOK_INSTANCE_ID,
    )
    reason = (
        CAVE_BROWSER_CONTINUATION_PREFIX
        + "The user intentionally submitted the following message(s) through the CAVE webpage "
        "to this exact Codex task. Treat them as user instructions in chronological order and "
        "respond normally. Do not discuss the delivery mechanism unless the user asks.\n"
        + json.dumps(claimed, ensure_ascii=False, separators=(",", ":"))
    )
    return {"decision": "block", "reason": reason}


def synchronize_answered_owner_hook_deliveries(
    root: Path,
    occurred_at: datetime,
    payload: dict[str, Any],
) -> None:
    """Complete owner-hook work from the retained final that follows its claim.

    The Stop that claims a queued browser message belongs to the response which
    preceded that browser prompt. Codex can reuse that same turn id for the generated
    continuation and its answer, so turn inequality is not a valid lifecycle signal.
    The canonical completion evidence is the first retained main-agent Final for the
    exact session whose timestamp is strictly later than the durable claim.
    """
    outbox = root / ".cave" / "conversation" / "control" / "outbox"
    if not outbox.is_dir():
        return

    session_id = optional_text(payload.get("session_id"))
    if session_id is None:
        return

    completed_any = False
    failed_any = False
    still_running = False
    try:
        with conversation_control_lock(root):
            for path in sorted(outbox.glob("*.json"), key=lambda candidate: candidate.name):
                try:
                    record = json.loads(path.read_text(encoding="utf-8"))
                except (OSError, json.JSONDecodeError):
                    continue
                if (
                    record.get("schemaVersion") != 1
                    or record.get("sessionId") != session_id
                    or record.get("state") != "Running"
                    or record.get("bridgeInstanceId") != CAVE_OWNER_HOOK_INSTANCE_ID
                ):
                    continue

                claimed_at = parse_utc_datetime(record.get("updatedAtUtc"))
                answer = find_main_agent_final_after(root, session_id, claimed_at)
                if answer is None:
                    if (
                        str(payload.get("hook_event_name") or "") in {"Stop", "Interrupt"}
                        and claimed_at is not None
                        and occurred_at > claimed_at
                    ):
                        updated = dict(record)
                        updated.update({
                            "state": "Failed",
                            "updatedAtUtc": occurred_at.isoformat().replace("+00:00", "Z"),
                            "bridgeInstanceId": CAVE_OWNER_HOOK_INSTANCE_ID,
                            "error": (
                                "Codex interrupted the browser turn without retaining a public answer."
                                if payload.get("hook_event_name") == "Interrupt"
                                else "Codex stopped without retaining a public answer."
                            ),
                        })
                        write_json_atomic(path, updated)
                        failed_any = True
                        continue
                    still_running = True
                    continue

                updated = dict(record)
                updated.update({
                    "turnId": optional_text(answer.get("turnId")),
                    "state": "Completed",
                    "updatedAtUtc": answer["occurredAtUtc"],
                    "bridgeInstanceId": CAVE_OWNER_HOOK_INSTANCE_ID,
                })
                updated.pop("error", None)
                write_json_atomic(path, updated)
                completed_any = True
    except TimeoutError:
        return

    if (completed_any or failed_any) and not still_running:
        release_owner_hook_status(root, occurred_at, payload)


def has_running_owner_hook_delivery(outbox: Path, session_id: str) -> bool:
    for path in sorted(outbox.glob("*.json"), key=lambda candidate: candidate.name):
        try:
            record = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        if (
            record.get("schemaVersion") == 1
            and record.get("sessionId") == session_id
            and record.get("state") == "Running"
            and record.get("bridgeInstanceId") == CAVE_OWNER_HOOK_INSTANCE_ID
        ):
            return True
    return False


def find_main_agent_final_after(
    root: Path,
    session_id: str,
    claimed_at: datetime | None,
) -> dict[str, Any] | None:
    if claimed_at is None:
        return None

    inbox = root / ".cave" / "conversation" / "inbox"
    if not inbox.is_dir():
        return None
    for path in sorted(inbox.glob("*.json"), key=lambda candidate: candidate.name):
        try:
            message = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            continue
        answered_at = parse_utc_datetime(message.get("occurredAtUtc"))
        if (
            message.get("schemaVersion") == 1
            and message.get("sessionId") == session_id
            and message.get("role") == "Assistant"
            and message.get("kind") == "Final"
            and not bool(message.get("isSubagent"))
            and answered_at is not None
            and answered_at > claimed_at
        ):
            return message
    return None


def parse_utc_datetime(value: Any) -> datetime | None:
    text = optional_text(value)
    if text is None:
        return None
    try:
        parsed = datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError:
        return None
    if parsed.tzinfo is None:
        return parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


@contextmanager
def conversation_control_lock(root: Path):
    """Serialize queue claims with the .NET bridge across processes.

    Both implementations use the same one-byte operating-system lock. The file is
    persistent but the lock is released automatically if either process exits.
    """
    path = root / ".cave" / "conversation" / "control" / "queue.lock"
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a+b") as handle:
        handle.seek(0, os.SEEK_END)
        if handle.tell() == 0:
            handle.write(b"\0")
            handle.flush()
            os.fsync(handle.fileno())

        deadline = time.monotonic() + 2.0
        while True:
            try:
                handle.seek(0)
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError as error:
                if time.monotonic() >= deadline:
                    raise TimeoutError("CAVE conversation queue lock timed out") from error
                time.sleep(0.025)

        try:
            yield
        finally:
            handle.seek(0)
            if os.name == "nt":
                import msvcrt
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                import fcntl
                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


def write_conversation_control_status(
    root: Path,
    occurred_at: datetime,
    payload: dict[str, Any],
    *,
    state: str = "Ready",
    can_send: bool = True,
    owner: str | None = None,
) -> None:
    status_path = root / ".cave" / "conversation" / "control" / "status.json"
    write_json_atomic(status_path, {
        "schemaVersion": 1,
        "sessionId": optional_text(payload.get("session_id")),
        "turnId": optional_text(payload.get("turn_id")),
        "state": state,
        "canSend": can_send,
        "owner": owner,
        "updatedAtUtc": occurred_at.isoformat().replace("+00:00", "Z"),
    })


def release_owner_hook_status(
    root: Path,
    occurred_at: datetime,
    payload: dict[str, Any],
) -> None:
    """Mark a hook-owned continuation ready only after its retained answer appears."""
    status_path = root / ".cave" / "conversation" / "control" / "status.json"
    try:
        status = json.loads(status_path.read_text(encoding="utf-8"))
    except (FileNotFoundError, OSError, json.JSONDecodeError):
        return
    if (
        status.get("schemaVersion") != 1
        or status.get("state") != "Running"
        or status.get("owner") != CAVE_OWNER_HOOK_INSTANCE_ID
        or status.get("sessionId") != optional_text(payload.get("session_id"))
    ):
        return
    write_conversation_control_status(root, occurred_at, payload)


def write_json_atomic(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.parent / f".{path.name}.{uuid4().hex}.tmp"
    try:
        temporary.write_text(
            json.dumps(value, separators=(",", ":")),
            encoding="utf-8",
        )
        os.replace(temporary, path)
    finally:
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass


def serialize_tool_input(payload: dict[str, Any]) -> str:
    """Serialize transient tool metadata once for classification without retaining it."""
    return json.dumps(payload.get("tool_input"), separators=(",", ":"))


def is_mutation(
    payload: dict[str, Any],
    serialized_tool_input: str | None = None,
) -> bool:
    tool_name = str(payload.get("tool_name") or "").lower()
    if tool_name in MUTATION_TOOL_NAMES:
        return True
    if re.search(r"(?:^|__)(?:write|edit|create|delete|update|apply_patch)(?:_|$)", tool_name):
        return True

    # Codex Desktop can expose a composed `functions.exec` tool. Detect its
    # apply_patch orchestration without retaining the raw source in the event.
    if tool_name == "functions.exec":
        serialized_tool_input = serialized_tool_input or serialize_tool_input(payload)
        return "apply_patch" in serialized_tool_input
    return False


def append_event(root: Path, occurred_at: datetime, event_id: str, event: dict[str, Any]) -> None:
    directory = root / ".cave" / "activity" / "inbox"
    directory.mkdir(parents=True, exist_ok=True)
    timestamp = occurred_at.strftime("%Y%m%d%H%M%S%f")
    stem = f"{timestamp}-{event_id}"
    temporary = directory / f".{stem}.tmp"
    final = directory / f"{stem}.json"
    temporary.write_text(json.dumps(event, separators=(",", ":")), encoding="utf-8")
    os.replace(temporary, final)


def append_conversation_message_if_enabled(
    root: Path,
    occurred_at: datetime,
    event_id: str,
    payload: dict[str, Any],
) -> None:
    """Retain only public conversation boundaries after explicit workspace opt-in."""
    if not conversation_sharing_enabled(root):
        return

    kind = str(payload.get("hook_event_name") or "")
    if kind == "UserPromptSubmit":
        if str(payload.get("prompt") or "").startswith(CAVE_BROWSER_CONTINUATION_PREFIX):
            # The original browser messages are retained when the owner hook claims
            # them. Do not expose the generated continuation envelope as a duplicate.
            return
        role = "User"
        message_kind = "Prompt"
        text = public_user_prompt(payload.get("prompt"))
    elif kind in {"Stop", "SubagentStop"}:
        role = "Assistant"
        message_kind = "Final"
        text = optional_text(payload.get("last_assistant_message"))
    else:
        return

    if text is None:
        return

    is_truncated = len(text) > MAX_CONVERSATION_MESSAGE_CHARACTERS
    if is_truncated:
        text = text[:MAX_CONVERSATION_MESSAGE_CHARACTERS]

    event = {
        "schemaVersion": 1,
        "eventId": event_id,
        "occurredAtUtc": occurred_at.isoformat().replace("+00:00", "Z"),
        "sessionId": optional_text(payload.get("session_id")),
        "turnId": optional_text(payload.get("turn_id")),
        "agentId": optional_text(payload.get("agent_id")),
        "agentType": optional_text(payload.get("agent_type")),
        "isSubagent": bool(payload.get("agent_id")),
        "role": role,
        "kind": message_kind,
        "text": text,
        "isTruncated": is_truncated,
    }
    directory = root / ".cave" / "conversation" / "inbox"
    directory.mkdir(parents=True, exist_ok=True)
    timestamp = occurred_at.strftime("%Y%m%d%H%M%S%f")
    stem = f"{timestamp}-{event_id}"
    temporary = directory / f".{stem}.tmp"
    final = directory / f"{stem}.json"
    temporary.write_text(json.dumps(event, separators=(",", ":")), encoding="utf-8")
    os.replace(temporary, final)
    prune_conversation_messages(root)


def append_browser_conversation_message_if_enabled(
    root: Path,
    occurred_at: datetime,
    message_id: str,
    payload: dict[str, Any],
    text: str,
) -> None:
    """Retain the public browser prompt once, without its continuation envelope."""
    if not conversation_sharing_enabled(root):
        return

    is_truncated = len(text) > MAX_CONVERSATION_MESSAGE_CHARACTERS
    retained_text = text[:MAX_CONVERSATION_MESSAGE_CHARACTERS] if is_truncated else text
    event = {
        "schemaVersion": 1,
        "eventId": f"browser-{message_id}",
        "occurredAtUtc": occurred_at.isoformat().replace("+00:00", "Z"),
        "sessionId": optional_text(payload.get("session_id")),
        "turnId": optional_text(payload.get("turn_id")),
        "agentId": None,
        "agentType": None,
        "isSubagent": False,
        "role": "User",
        "kind": "Prompt",
        "text": retained_text,
        "isTruncated": is_truncated,
    }
    directory = root / ".cave" / "conversation" / "inbox"
    directory.mkdir(parents=True, exist_ok=True)
    timestamp = occurred_at.strftime("%Y%m%d%H%M%S%f")
    write_json_atomic(directory / f"{timestamp}-browser-{message_id}.json", event)
    prune_conversation_messages(root)


def conversation_sharing_enabled(root: Path) -> bool:
    """Fail closed unless a supported settings record explicitly enables sharing."""
    settings_path = root / ".cave" / "conversation" / "settings.json"
    try:
        settings = json.loads(settings_path.read_text(encoding="utf-8"))
    except (FileNotFoundError, OSError, json.JSONDecodeError):
        return False
    return (
        settings.get("schemaVersion") == 1
        and settings.get("conversationSharingEnabled") is True
    )


def prune_conversation_messages(
    root: Path,
    maximum: int = MAX_RETAINED_CONVERSATION_MESSAGES,
) -> None:
    """Bound the opt-in public conversation journal without touching other evidence."""
    directory = root / ".cave" / "conversation" / "inbox"
    if maximum < 1 or not directory.is_dir():
        return

    message_paths = sorted(directory.glob("*.json"), key=lambda path: path.name)
    for stale_path in message_paths[:-maximum]:
        try:
            stale_path.unlink()
        except FileNotFoundError:
            pass


def prune_events(root: Path, maximum: int = MAX_RETAINED_EVENTS) -> None:
    """Bound generated journal growth without touching non-CAVE files."""
    directory = root / ".cave" / "activity" / "inbox"
    if maximum < 1 or not directory.is_dir():
        return

    event_paths = sorted(directory.glob("*.json"), key=lambda path: path.name)
    for stale_path in event_paths[:-maximum]:
        try:
            stale_path.unlink()
        except FileNotFoundError:
            # Concurrent hook invocations may prune the same generated event.
            pass


def workspace_id_for(root: Path) -> str:
    """Create the opaque identity shared with MachineWorkspaceCatalogStore."""
    canonical = str(root.resolve()).rstrip("\\/")
    identity_path = canonical.lower() if os.name == "nt" else canonical
    return hashlib.sha256(identity_path.encode("utf-8")).hexdigest()[:24]


def register_workspace(root: Path, occurred_at: datetime, event_id: str) -> None:
    """Atomically refresh one machine-local workspace registration."""
    local_application_data = os.environ.get("LOCALAPPDATA")
    if not local_application_data:
        raise RuntimeError("LOCALAPPDATA is unavailable; the machine workspace catalog cannot be located.")

    canonical_root = root.resolve()
    workspace_id = workspace_id_for(canonical_root)
    directory = Path(local_application_data) / "CAVE" / "workspaces"
    directory.mkdir(parents=True, exist_ok=True)
    temporary = directory / f".{workspace_id}.{event_id}.tmp"
    final = directory / f"{workspace_id}.json"
    record = {
        "schemaVersion": 1,
        "workspaceId": workspace_id,
        "workspaceRoot": str(canonical_root),
        "lastSeenAtUtc": occurred_at.isoformat().replace("+00:00", "Z"),
    }
    temporary.write_text(json.dumps(record, indent=2), encoding="utf-8")
    os.replace(temporary, final)


if __name__ == "__main__":
    raise SystemExit(main())
