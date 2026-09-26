# Privacy Notice

This repository documents the intended behavior of CAVE. A hosted plugin or website must publish a reviewed version of this notice at a stable HTTPS URL before external submission.

## Data CAVE processes

CAVE can process local architecture metadata, source-file paths, semantic relations, Git baseline and worktree metadata, agent phase and path activity, machine identity, account usage summaries, and local runtime information. Conversation sharing is off by default; when an authorized caller explicitly enables it for a workspace, CAVE stores bounded copies of user prompts and final assistant replies in a separate local journal. The engineering workspace stores authored entities, relationships, content, tracking, and diagram/board presentation independently of read-only code evidence.

Activity hooks are designed not to store prompt text, assistant replies, commands, tool payloads, tool results, or reasoning. Demo mode uses synthetic data only.

## Purpose and storage

Data is used to render architecture, change impact, active-work context, and engineering intent. Local indexes and journals remain on the user's machine in ignored workspace or application-data locations. The standalone browser can read shared content, change conversation sharing, queue exact-task chat, ask isolated node questions, and edit engineering intent. It has no HTTP authentication and should only be reachable through loopback or a trusted restricted network whose callers are authorized for those operations.

Shared chat uses a durable local delivery journal. Node questions use temporary isolated task context and return their answer to the requesting dialog. Codex requests use the existing Codex account and configured provider; that provider's data handling applies to requests sent through Codex. Third-party tools such as CodeGraph have their own telemetry settings and policies.

## Sharing and retention

CAVE does not intentionally sell personal data. Activity and shared-message projections are bounded, but turning conversation sharing off hides retained messages and stops new capture; it does not erase existing journals. Local data can be removed through the user's workspace and application-data state after the relevant processes stop. Engineering intent and durable delivery records remain local until removed by the user. Source publication does not publish those ignored local records. A future hosted service must document its processors, retention, deletion, authentication, regional handling, and contact channel before collecting data.

## User choices

Users can leave conversation sharing off, stop CAVE, remove local workspace state, remove the plugin, and restrict network access to the viewer. Security and privacy reports should use the private process in `SECURITY.md`.
