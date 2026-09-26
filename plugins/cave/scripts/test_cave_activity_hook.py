"""Regression tests for the privacy-minimized CAVE activity hook."""

from __future__ import annotations

import unittest
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
import io
import json
import os
from pathlib import Path
import tempfile
from unittest.mock import patch

from cave_activity_hook import (
    CAVE_BROWSER_CONTINUATION_PREFIX,
    activity_phase_for,
    append_event,
    append_conversation_message_if_enabled,
    conversation_sharing_enabled,
    claim_browser_messages_for_stop,
    extract_path_candidates,
    hook_response_for,
    is_cave_ephemeral_session,
    main,
    synchronize_answered_owner_hook_deliveries,
    prune_events,
    prune_conversation_messages,
    public_user_prompt,
    register_workspace,
    workspace_id_for,
)


class EphemeralSessionTests(unittest.TestCase):
    def test_marker_identifies_only_the_exact_temporary_session(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            session_id = "temporary-session"
            marker = (
                workspace
                / ".cave"
                / "conversation"
                / "ephemeral"
                / (hashlib.sha256(session_id.encode("utf-8")).hexdigest() + ".json")
            )
            marker.parent.mkdir(parents=True)
            marker.write_text("{}", encoding="utf-8")

            self.assertTrue(is_cave_ephemeral_session(workspace, {"session_id": session_id}))
            self.assertFalse(is_cave_ephemeral_session(workspace, {"session_id": "main-session"}))


class ActivityPathExtractionTests(unittest.TestCase):
    def test_absolute_windows_path_is_not_duplicated_as_a_relative_suffix(self) -> None:
        path = (
            r"C:\Work\CAVE\DemoWorkspace"
            r"\src\Cave.Host\Program.cs"
        )

        candidates = extract_path_candidates({"file_path": path})

        self.assertEqual([path], candidates)

    def test_short_extension_does_not_truncate_a_directory_name(self) -> None:
        path = (
            r"C:\Work\CAVE\DemoWorkspace"
            r"\src\Cave.Host\Program.cs"
        )

        candidates = extract_path_candidates({"command": f'open "{path}"'})

        self.assertEqual([path], candidates)

    def test_apply_patch_keeps_a_workspace_relative_path(self) -> None:
        patch = "*** Update File: src/Cave.Ui/src/api.ts\n@@\n"

        candidates = extract_path_candidates({"patch": patch})

        self.assertEqual(["src/Cave.Ui/src/api.ts"], candidates)

    def test_shell_verb_is_not_retained_as_part_of_relative_path(self) -> None:
        candidates = extract_path_candidates({
            "command": "Get-Content src/Cave.Ui/src/ArchitectureNodeCard.tsx",
        })

        self.assertEqual(["src/Cave.Ui/src/ArchitectureNodeCard.tsx"], candidates)


class ActivityPhaseTests(unittest.TestCase):
    def test_new_instruction_starts_in_thinking_phase(self) -> None:
        self.assertEqual(
            "Thinking",
            activity_phase_for({"hook_event_name": "UserPromptSubmit"}),
        )

    def test_read_command_is_classified_without_persisting_the_command(self) -> None:
        self.assertEqual(
            "Reading",
            activity_phase_for({
                "hook_event_name": "PreToolUse",
                "tool_name": "Bash",
                "tool_input": {"command": "Get-Content src/Cave.Ui/src/App.tsx"},
            }),
        )

    def test_apply_patch_is_editing(self) -> None:
        self.assertEqual(
            "Editing",
            activity_phase_for({
                "hook_event_name": "PreToolUse",
                "tool_name": "apply_patch",
                "tool_input": {"command": "*** Update File: App.tsx"},
            }),
        )

    def test_test_command_is_validating(self) -> None:
        self.assertEqual(
            "Validating",
            activity_phase_for({
                "hook_event_name": "PostToolUse",
                "tool_name": "Bash",
                "tool_input": {"command": "dotnet test CAVE.slnx"},
            }),
        )

    def test_composed_exec_classifies_nested_validation(self) -> None:
        self.assertEqual(
            "Validating",
            activity_phase_for({
                "hook_event_name": "PostToolUse",
                "tool_name": "functions.exec",
                "tool_input": {
                    "code": "await tools.exec_command({cmd: 'npm --prefix src/Cave.Ui run lint'})",
                },
            }),
        )

    def test_composed_exec_classifies_nested_read(self) -> None:
        self.assertEqual(
            "Reading",
            activity_phase_for({
                "hook_event_name": "PostToolUse",
                "tool_name": "functions.exec",
                "tool_input": {
                    "code": "await tools.exec_command({cmd: 'Get-Content Architecture.md'})",
                },
            }),
        )

    def test_update_plan_is_thinking_not_a_generic_update_mutation(self) -> None:
        self.assertEqual(
            "Thinking",
            activity_phase_for({
                "hook_event_name": "PreToolUse",
                "tool_name": "update_plan",
                "tool_input": {"plan": []},
            }),
        )

    def test_composed_exec_classifies_nested_update_plan_as_thinking(self) -> None:
        self.assertEqual(
            "Thinking",
            activity_phase_for({
                "hook_event_name": "PreToolUse",
                "tool_name": "functions.exec",
                "tool_input": {
                    "code": "await tools.update_plan({plan: []})",
                },
            }),
        )

    def test_composed_edit_takes_priority_over_nested_plan_update(self) -> None:
        self.assertEqual(
            "Editing",
            activity_phase_for({
                "hook_event_name": "PreToolUse",
                "tool_name": "functions.exec",
                "tool_input": {
                    "code": (
                        "await tools.update_plan({plan: []}); "
                        "await tools.apply_patch('*** Update File: App.tsx')"
                    ),
                },
            }),
        )

    def test_request_user_input_is_thinking(self) -> None:
        self.assertEqual(
            "Thinking",
            activity_phase_for({
                "hook_event_name": "PreToolUse",
                "tool_name": "request_user_input",
                "tool_input": {"questions": []},
            }),
        )

    def test_stop_has_no_active_phase(self) -> None:
        self.assertIsNone(activity_phase_for({"hook_event_name": "Stop"}))

    def test_stop_hooks_emit_the_required_valid_json_shape(self) -> None:
        self.assertEqual({"continue": True}, hook_response_for({"hook_event_name": "Stop"}))
        self.assertEqual(
            {"continue": True},
            hook_response_for({"hook_event_name": "SubagentStop"}),
        )
        self.assertIsNone(hook_response_for({"hook_event_name": "PostToolUse"}))


class ActivityRetentionTests(unittest.TestCase):
    def test_concurrent_event_writes_are_complete_atomic_and_unique(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            occurred_at = datetime(2026, 8, 18, 5, 0, tzinfo=timezone.utc)

            def write_event(index: int) -> None:
                event_id = f"{index:032x}"
                append_event(
                    workspace,
                    occurred_at,
                    event_id,
                    {
                        "schemaVersion": 1,
                        "eventId": event_id,
                        "kind": "PreToolUse",
                        "occurredAtUtc": occurred_at.isoformat(),
                        "sessionId": "concurrent-session",
                        "paths": [],
                    },
                )

            with ThreadPoolExecutor(max_workers=16) as executor:
                list(executor.map(write_event, range(64)))

            directory = workspace / ".cave" / "activity" / "inbox"
            event_paths = sorted(directory.glob("*.json"))
            self.assertEqual(64, len(event_paths))
            self.assertEqual([], list(directory.glob("*.tmp")))
            event_ids = {
                json.loads(path.read_text(encoding="utf-8"))["eventId"]
                for path in event_paths
            }
            self.assertEqual({f"{index:032x}" for index in range(64)}, event_ids)

    def test_pruning_keeps_only_the_newest_generated_events(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            directory = workspace / ".cave" / "activity" / "inbox"
            directory.mkdir(parents=True)
            for index in range(5):
                (directory / f"20260817120000000000{index}-event.json").write_text(
                    "{}",
                    encoding="utf-8",
                )

            prune_events(workspace, maximum=3)

            self.assertEqual(
                [
                    "202608171200000000002-event.json",
                    "202608171200000000003-event.json",
                    "202608171200000000004-event.json",
                ],
                sorted(path.name for path in directory.glob("*.json")),
            )


class ConversationSharingTests(unittest.TestCase):
    def test_public_prompt_removes_leading_ambient_browser_context(self) -> None:
        prompt = """<in-app-browser-context source="ambient-ui-state">
This block is automatically supplied ambient UI state, not part of the user's request.
# In app browser:
- Current URL: http://127.0.0.1:5098/activity
</in-app-browser-context>

## My request:
Show the actual project instruction.
"""

        self.assertEqual(
            "Show the actual project instruction.",
            public_user_prompt(prompt),
        )

    def test_public_prompt_preserves_user_authored_context_markup(self) -> None:
        prompt = "Explain this literal: <in-app-browser-context source=\"ambient-ui-state\">"

        self.assertEqual(prompt, public_user_prompt(prompt))

    def test_prompt_with_only_ambient_context_is_not_retained(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            settings = workspace / ".cave" / "conversation" / "settings.json"
            settings.parent.mkdir(parents=True)
            settings.write_text(
                json.dumps({"schemaVersion": 1, "conversationSharingEnabled": True}),
                encoding="utf-8",
            )

            append_conversation_message_if_enabled(
                workspace,
                datetime(2026, 8, 26, 13, 0, tzinfo=timezone.utc),
                "ambient-only",
                {
                    "hook_event_name": "UserPromptSubmit",
                    "prompt": (
                        '<in-app-browser-context source="ambient-ui-state">\n'
                        'Automatically supplied.\n'
                        '</in-app-browser-context>'
                    ),
                },
            )

            self.assertFalse((workspace / ".cave" / "conversation" / "inbox").exists())

    def test_sharing_fails_closed_without_supported_settings(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            settings = workspace / ".cave" / "conversation" / "settings.json"
            settings.parent.mkdir(parents=True)
            settings.write_text("{ invalid", encoding="utf-8")

            self.assertFalse(conversation_sharing_enabled(workspace))
            append_conversation_message_if_enabled(
                workspace,
                datetime(2026, 8, 19, tzinfo=timezone.utc),
                "event-1",
                {"hook_event_name": "UserPromptSubmit", "prompt": "private"},
            )
            self.assertFalse((workspace / ".cave" / "conversation" / "inbox").exists())

    def test_opt_in_retains_only_public_prompt_boundary(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            settings = workspace / ".cave" / "conversation" / "settings.json"
            settings.parent.mkdir(parents=True)
            settings.write_text(
                json.dumps({"schemaVersion": 1, "conversationSharingEnabled": True}),
                encoding="utf-8",
            )

            append_conversation_message_if_enabled(
                workspace,
                datetime(2026, 8, 19, 18, 30, tzinfo=timezone.utc),
                "event-1",
                {
                    "hook_event_name": "UserPromptSubmit",
                    "session_id": "session-1",
                    "turn_id": "turn-1",
                    "prompt": (
                        '<in-app-browser-context source="ambient-ui-state">\n'
                        'Automatically supplied browser state.\n'
                        '</in-app-browser-context>\n\n'
                        '## My request:\n'
                        'Explain the selected box'
                    ),
                    "tool_input": {"secret": "must not leak"},
                },
            )

            path = next((workspace / ".cave" / "conversation" / "inbox").glob("*.json"))
            text = path.read_text(encoding="utf-8")
            message = json.loads(text)
            self.assertEqual("User", message["role"])
            self.assertEqual("Prompt", message["kind"])
            self.assertEqual("Explain the selected box", message["text"])
            self.assertNotIn("must not leak", text)

    def test_conversation_retention_is_bounded_independently(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            directory = workspace / ".cave" / "conversation" / "inbox"
            directory.mkdir(parents=True)
            for index in range(5):
                (directory / f"20260819120000000000{index}-message.json").write_text(
                    "{}",
                    encoding="utf-8",
                )

            prune_conversation_messages(workspace, maximum=2)

            self.assertEqual(
                [
                    "202608191200000000003-message.json",
                    "202608191200000000004-message.json",
                ],
                sorted(path.name for path in directory.glob("*.json")),
            )

    def test_stop_claims_only_exact_task_messages_and_requests_owner_continuation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            settings = workspace / ".cave" / "conversation" / "settings.json"
            settings.parent.mkdir(parents=True)
            settings.write_text(
                json.dumps({"schemaVersion": 1, "conversationSharingEnabled": True}),
                encoding="utf-8",
            )
            outbox = workspace / ".cave" / "conversation" / "control" / "outbox"
            outbox.mkdir(parents=True)
            exact_path = outbox / "001-exact.json"
            second_exact_path = outbox / "002-exact.json"
            other_path = outbox / "003-other.json"
            exact_path.write_text(json.dumps({
                "schemaVersion": 1,
                "messageId": "message-exact",
                "sessionId": "session-1",
                "text": "Run the browser request",
                "state": "Queued",
                "queuedAtUtc": "2026-08-23T10:00:00Z",
                "updatedAtUtc": "2026-08-23T10:00:00Z",
                "error": "Codex Desktop still owns this task.",
            }), encoding="utf-8")
            second_exact_path.write_text(json.dumps({
                "schemaVersion": 1,
                "messageId": "message-exact-2",
                "sessionId": "session-1",
                "text": "Then verify the rendered result",
                "state": "Queued",
                "queuedAtUtc": "2026-08-23T10:00:01Z",
                "updatedAtUtc": "2026-08-23T10:00:01Z",
            }), encoding="utf-8")
            other_path.write_text(json.dumps({
                "schemaVersion": 1,
                "messageId": "message-other",
                "sessionId": "session-2",
                "text": "Do not cross task boundaries",
                "state": "Queued",
                "queuedAtUtc": "2026-08-23T10:00:01Z",
                "updatedAtUtc": "2026-08-23T10:00:01Z",
            }), encoding="utf-8")
            payload = {
                "hook_event_name": "Stop",
                "session_id": "session-1",
                "turn_id": "turn-1",
            }

            response = claim_browser_messages_for_stop(
                workspace,
                datetime(2026, 8, 23, 10, 5, tzinfo=timezone.utc),
                payload,
            )

            self.assertIsNotNone(response)
            assert response is not None
            self.assertEqual("block", response["decision"])
            self.assertTrue(response["reason"].startswith(CAVE_BROWSER_CONTINUATION_PREFIX))
            self.assertIn("Run the browser request", response["reason"])
            self.assertLess(
                response["reason"].index("Run the browser request"),
                response["reason"].index("Then verify the rendered result"),
            )
            self.assertNotIn("Do not cross task boundaries", response["reason"])
            exact = json.loads(exact_path.read_text(encoding="utf-8"))
            other = json.loads(other_path.read_text(encoding="utf-8"))
            self.assertEqual("Running", exact["state"])
            self.assertEqual("turn-1", exact["turnId"])
            self.assertNotIn("error", exact)
            self.assertEqual("Queued", other["state"])
            status = json.loads(
                (workspace / ".cave" / "conversation" / "control" / "status.json")
                .read_text(encoding="utf-8")
            )
            self.assertEqual("Running", status["state"])
            self.assertFalse(status["canSend"])
            self.assertEqual("codex-owner-hook", status["owner"])
            retained = [
                json.loads(path.read_text(encoding="utf-8"))
                for path in sorted(
                    (workspace / ".cave" / "conversation" / "inbox").glob("*.json")
                )
            ]
            self.assertEqual(
                ["Run the browser request", "Then verify the rendered result"],
                [message["text"] for message in retained],
            )

            # The final which caused the claim has the same timestamp as the claim
            # and must not be mistaken for the generated continuation's answer.
            append_conversation_message_if_enabled(
                workspace,
                datetime(2026, 8, 23, 10, 5, tzinfo=timezone.utc),
                "claiming-final",
                payload | {"last_assistant_message": "The response before the browser prompt."},
            )
            synchronize_answered_owner_hook_deliveries(
                workspace,
                datetime(2026, 8, 23, 10, 5, tzinfo=timezone.utc),
                payload,
            )
            same_turn = json.loads(exact_path.read_text(encoding="utf-8"))
            self.assertEqual("Running", same_turn["state"])
            same_turn_status = json.loads(
                (workspace / ".cave" / "conversation" / "control" / "status.json")
                .read_text(encoding="utf-8")
            )
            self.assertEqual("Running", same_turn_status["state"])

            # Codex intentionally reuses the claim turn for the generated answer.
            append_conversation_message_if_enabled(
                workspace,
                datetime(2026, 8, 23, 10, 6, tzinfo=timezone.utc),
                "answering-final",
                payload | {"last_assistant_message": "The browser answer."},
            )
            synchronize_answered_owner_hook_deliveries(
                workspace,
                datetime(2026, 8, 23, 10, 6, tzinfo=timezone.utc),
                payload,
            )
            completed = json.loads(exact_path.read_text(encoding="utf-8"))
            self.assertEqual("Completed", completed["state"])
            self.assertEqual("turn-1", completed["turnId"])
            self.assertEqual("2026-08-23T10:06:00Z", completed["updatedAtUtc"])
            ready_status = json.loads(
                (workspace / ".cave" / "conversation" / "control" / "status.json")
                .read_text(encoding="utf-8")
            )
            self.assertEqual("Ready", ready_status["state"])
            self.assertTrue(ready_status["canSend"])
            self.assertIsNone(ready_status["owner"])

    def test_generated_browser_continuation_is_not_retained_twice(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            settings = workspace / ".cave" / "conversation" / "settings.json"
            settings.parent.mkdir(parents=True)
            settings.write_text(
                json.dumps({"schemaVersion": 1, "conversationSharingEnabled": True}),
                encoding="utf-8",
            )

            append_conversation_message_if_enabled(
                workspace,
                datetime(2026, 8, 23, 10, 6, tzinfo=timezone.utc),
                "continuation-event",
                {
                    "hook_event_name": "UserPromptSubmit",
                    "prompt": CAVE_BROWSER_CONTINUATION_PREFIX + "generated envelope",
                },
            )

            self.assertFalse((workspace / ".cave" / "conversation" / "inbox").exists())

    def test_later_stop_without_a_public_answer_fails_and_releases_delivery(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            outbox = workspace / ".cave" / "conversation" / "control" / "outbox"
            outbox.mkdir(parents=True)
            delivery_path = outbox / "delivery.json"
            delivery_path.write_text(json.dumps({
                "schemaVersion": 1,
                "messageId": "message",
                "sessionId": "session-1",
                "text": "Explain this node",
                "state": "Running",
                "queuedAtUtc": "2026-08-23T10:00:00Z",
                "updatedAtUtc": "2026-08-23T10:05:00Z",
                "turnId": "turn-1",
                "bridgeInstanceId": "codex-owner-hook",
            }), encoding="utf-8")
            status = workspace / ".cave" / "conversation" / "control" / "status.json"
            status.write_text(json.dumps({
                "schemaVersion": 1,
                "sessionId": "session-1",
                "turnId": "turn-1",
                "state": "Running",
                "canSend": False,
                "owner": "codex-owner-hook",
                "updatedAtUtc": "2026-08-23T10:05:00Z",
            }), encoding="utf-8")
            payload = {
                "hook_event_name": "Stop",
                "session_id": "session-1",
                "turn_id": "turn-1",
            }

            synchronize_answered_owner_hook_deliveries(
                workspace,
                datetime(2026, 8, 23, 10, 6, tzinfo=timezone.utc),
                payload,
            )

            failed = json.loads(delivery_path.read_text(encoding="utf-8"))
            self.assertEqual("Failed", failed["state"])
            self.assertIn("without retaining a public answer", failed["error"])
            ready = json.loads(status.read_text(encoding="utf-8"))
            self.assertEqual("Ready", ready["state"])
            self.assertTrue(ready["canSend"])


class ActivityHookIntegrationTests(unittest.TestCase):
    def test_interrupt_fails_only_its_running_owner_delivery_without_replaying_queue(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary)
            outbox = workspace / ".cave" / "conversation" / "control" / "outbox"
            outbox.mkdir(parents=True)
            for name, session, state in (
                ("running", "session-1", "Running"),
                ("other-task", "session-2", "Running"),
                ("queued", "session-1", "Queued"),
            ):
                (outbox / f"{name}.json").write_text(json.dumps({
                    "schemaVersion": 1, "messageId": name, "sessionId": session,
                    "turnId": "turn-1", "text": "Continue", "state": state,
                    "updatedAtUtc": "2026-09-26T12:00:00Z",
                    "bridgeInstanceId": "codex-owner-hook" if state == "Running" else None,
                }), encoding="utf-8")
            status = outbox.parent / "status.json"
            status.write_text(json.dumps({
                "schemaVersion": 1, "sessionId": "session-1", "turnId": "turn-1",
                "state": "Running", "canSend": False, "owner": "codex-owner-hook",
            }), encoding="utf-8")
            payload = {
                "hook_event_name": "Interrupt", "session_id": "session-1", "turn_id": "turn-1",
            }

            synchronize_answered_owner_hook_deliveries(
                workspace, datetime(2026, 9, 26, 12, 1, tzinfo=timezone.utc), payload,
            )

            failed = json.loads((outbox / "running.json").read_text(encoding="utf-8"))
            self.assertEqual("Failed", failed["state"])
            self.assertIn("interrupted", failed["error"])
            self.assertEqual("Running", json.loads((outbox / "other-task.json").read_text(encoding="utf-8"))["state"])
            self.assertEqual("Queued", json.loads((outbox / "queued.json").read_text(encoding="utf-8"))["state"])
            self.assertTrue(json.loads(status.read_text(encoding="utf-8"))["canSend"])
            self.assertIsNone(claim_browser_messages_for_stop(
                workspace, datetime(2026, 9, 26, 12, 1, tzinfo=timezone.utc), payload,
            ))

    def test_ephemeral_cave_session_writes_no_shared_workspace_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary) / "repo"
            (workspace / ".git").mkdir(parents=True)
            session_id = "temporary-session"
            marker = (
                workspace
                / ".cave"
                / "conversation"
                / "ephemeral"
                / (hashlib.sha256(session_id.encode("utf-8")).hexdigest() + ".json")
            )
            marker.parent.mkdir(parents=True)
            marker.write_text("{}", encoding="utf-8")
            payload = {
                "hook_event_name": "UserPromptSubmit",
                "session_id": session_id,
                "turn_id": "turn-1",
                "cwd": str(workspace),
                "prompt": "Temporary node question",
            }

            with (
                patch("sys.stdin", io.StringIO(json.dumps(payload))),
                patch("sys.stdout", io.StringIO()),
            ):
                exit_code = main()

            self.assertEqual(0, exit_code)
            self.assertFalse((workspace / ".cave" / "activity" / "inbox").exists())
            self.assertFalse((workspace / ".cave" / "conversation" / "inbox").exists())

    def test_stop_writes_privacy_minimized_event_and_valid_response(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary) / "repo"
            (workspace / ".git").mkdir(parents=True)
            local_data = Path(temporary) / "local"
            payload = {
                "hook_event_name": "Stop",
                "session_id": "session-1",
                "turn_id": "turn-1",
                "cwd": str(workspace),
                "last_assistant_message": "sensitive assistant text",
            }
            standard_input = io.StringIO(json.dumps(payload))
            standard_output = io.StringIO()

            with (
                patch.dict(os.environ, {"LOCALAPPDATA": str(local_data)}),
                patch("sys.stdin", standard_input),
                patch("sys.stdout", standard_output),
            ):
                exit_code = main()

            self.assertEqual(0, exit_code)
            self.assertEqual({"continue": True}, json.loads(standard_output.getvalue()))
            event_path = next((workspace / ".cave" / "activity" / "inbox").glob("*.json"))
            event_text = event_path.read_text(encoding="utf-8")
            event = json.loads(event_text)
            self.assertEqual("Stop", event["kind"])
            self.assertEqual("session-1", event["sessionId"])
            self.assertNotIn("sensitive assistant text", event_text)

    def test_interrupt_records_terminal_activity_without_public_text_or_continuation(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            workspace = Path(temporary) / "repo"
            (workspace / ".git").mkdir(parents=True)
            local_data = Path(temporary) / "local"
            payload = {
                "hook_event_name": "Interrupt",
                "session_id": "session-1",
                "turn_id": "turn-1",
                "cwd": str(workspace),
                "last_assistant_message": "partial private text",
            }
            standard_output = io.StringIO()

            with (
                patch.dict(os.environ, {"LOCALAPPDATA": str(local_data)}),
                patch("sys.stdin", io.StringIO(json.dumps(payload))),
                patch("sys.stdout", standard_output),
            ):
                self.assertEqual(0, main())

            self.assertEqual("", standard_output.getvalue())
            event_path = next((workspace / ".cave" / "activity" / "inbox").glob("*.json"))
            event_text = event_path.read_text(encoding="utf-8")
            event = json.loads(event_text)
            self.assertEqual("Interrupt", event["kind"])
            self.assertIsNone(event["phase"])
            self.assertEqual("Turn interrupted", event["summary"])
            self.assertNotIn("partial private text", event_text)
            self.assertFalse((workspace / ".cave" / "conversation" / "inbox").exists())


class WorkspaceCatalogTests(unittest.TestCase):
    def test_workspace_identity_matches_dotnet_contract(self) -> None:
        if os.name != "nt":
            self.skipTest("The pinned identity is the Windows machine contract.")
        self.assertEqual(
            "31156ac323b2a0f270416368",
            workspace_id_for(Path(r"C:\Code\Example")),
        )

    def test_registration_writes_the_shared_machine_catalog_contract(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            local_data = Path(temporary) / "local"
            workspace = Path(temporary) / "repo"
            workspace.mkdir()
            observed_at = datetime(2026, 8, 17, 12, 30, tzinfo=timezone.utc)

            with patch.dict(os.environ, {"LOCALAPPDATA": str(local_data)}):
                register_workspace(workspace, observed_at, "event-1")

            workspace_id = workspace_id_for(workspace)
            record_path = local_data / "CAVE" / "workspaces" / f"{workspace_id}.json"
            record = json.loads(record_path.read_text(encoding="utf-8"))
            self.assertEqual(1, record["schemaVersion"])
            self.assertEqual(workspace_id, record["workspaceId"])
            self.assertEqual(str(workspace.resolve()), record["workspaceRoot"])
            self.assertEqual("2026-08-17T12:30:00Z", record["lastSeenAtUtc"])


if __name__ == "__main__":
    unittest.main()
