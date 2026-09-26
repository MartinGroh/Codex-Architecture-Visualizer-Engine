# Semantic workspace contract

Status: implementation contract for schema version 1.

## Boundary

CAVE has two related but distinct sources of truth:

- `ArchitectureGraph` is read-only evidence projected from CodeGraph. Its node and relation IDs remain canonical for source architecture.
- `SemanticWorkspace` is user/agent-authored engineering intent. It owns editable diagrams, manual entities, content, tracking, and view state. A semantic entity may bind to an `ArchitectureNode.Id`, but it never copies or mutates the architecture graph. Undo/redo history is application-session state rather than a second persisted model.

The architecture, diagram, Kanban, HTTP, MCP, and export surfaces are projections or adapters. None owns a second task, card, or relationship model.

## Identity

Every persisted object has a non-empty stable string ID that is unique across the workspace:

- workspace, diagram, entity, relationship;
- content block and checklist item;
- Kanban board and column;
- asset reference;
- transaction entry returned for a committed service-session batch.

IDs are opaque to clients. Creation operations accept an optional caller-provided ID for deterministic AI batches; otherwise the application creates a UUID. Array indexes are never identity.

## Semantic model

`SemanticWorkspace`

- `schemaVersion` (currently `1`)
- `id`, `name`, `updatedAtUtc`
- `diagrams[]`, `entities[]`, `relationships[]`, `boards[]`, `assets[]`

`SemanticDiagram`

- `id`, `title`, optional `purpose`, optional `parentDiagramId`
- optional `tracking`
- `view`: presentation state owned by this diagram

`SemanticEntity`

- `id`, `diagramId`, `type`, `title`, optional `description`
- optional `parentEntityId`, `childDiagramId`, `architectureNodeId`
- `blocks[]`, optional `tracking`, `tags[]`, metadata

`SemanticRelationship`

- `id`, `diagramId`, `sourceEntityId`, `targetEntityId`
- extensible string `type`; `generic` is always valid
- optional label, metadata

The initial relationship vocabulary is `generic`, `depends-on`, `calls`, `data-flow`, `implements`, `contains`, `triggers`, `references`, `blocks`, `produces`, `consumes`, and `expands-to`. Validation accepts other non-empty normalized tokens so the vocabulary can grow without a schema migration.

## Rich content

Content uses a discriminated block model. Every block has `id` and `kind`.

- `text`: Markdown-capable `text`
- `checklist`: ordered items with stable `id`, `text`, and `completed`
- `code`: `source`, `language`, optional `filename`
- `math`: machine-readable LaTeX `source`
- `image`: `assetId`, optional `caption`, required `altText`
- `drawing`: editable vector `strokes[]` foundation; each stroke has stable ID, points, colour, and width

Blocks are semantic. Expanded/collapsed state, preview length, and editor selection are presentation state.

## Tracking

Tracking is a facet on a diagram or entity, never a duplicated Kanban card:

- `boardId`, `columnId`
- progress `{ mode, value }`; schema v1 implements `manual`, while reserving `checklist` and `children`
- optional metadata

Boards contain ordered, user-configurable columns. Cards are derived by resolving every tracked diagram/entity against the selected board/column. Renaming the source immediately changes every projection.

## Semantic versus presentation state

Semantic state contains identity, descriptions, hierarchy, blocks, relationships, tracking, references, and provenance.

Presentation state is nested under a diagram view and contains only layout concerns:

- entity position and size;
- collapsed/expanded state;
- local style overrides;
- viewport/zoom;
- manually positioned flag.

Application operations create semantic entities without coordinates. The client/layout engine supplies defaults until a user records manual presentation state. Semantic export excludes view state by default.

## Operations and transactions

The application owns typed operations. Adapters do not patch JSON directly.

Initial commands cover:

- create/update/delete diagram and entity;
- create/update/delete relationship;
- add/update/delete content block;
- add/update/check/reorder checklist item;
- create/update/delete board and column;
- track/untrack a diagram or entity, move its column, set progress;
- link/unlink a child diagram;
- update diagram presentation state.

`ApplyOperations` validates a whole batch against a working copy and commits atomically. It returns created IDs, the new revision, and structured errors. Every write uses optimistic revision compare-and-swap while holding the workspace's cross-process write lock. Concurrent HTTP and MCP writers therefore cannot both replace the same base revision: the first complete batch commits and a stale candidate receives `revision_conflict` without overwriting it. One committed batch is one undo step in the application-service session that accepted it. Undo/redo operate on semantic-workspace transactions, not CodeGraph evidence, and use the same cross-process revision boundary. History is not embedded in or exported with the schema-v1 aggregate; a service restart starts a new history session, and a revision mismatch clears session history without overwriting externally written state.

Every transaction records actor type (`human`, `codex`, `copilot`, `agent`, `system`), optional actor ID/name, timestamp, summary, and affected semantic IDs. Provenance is history metadata and a temporary UI cue, not permanent node chrome.

## Validation

Validation is centralized in the domain/application service and includes:

- unique stable IDs and supported schema version;
- existing diagram/entity/relationship endpoints;
- parent and child-diagram cycle checks;
- board/column references for tracking;
- progress in `0..100`;
- content-block and asset references;
- presentation entries that reference entities in the owning diagram.

Errors carry a stable code, message, operation index when applicable, target ID, and useful alternatives. Invalid batches make no durable change.

## Persistence and compatibility

The canonical file is `.cave/semantic-workspace.json`. Infrastructure owns atomic replace/write and serializes camel-case JSON with an explicit schema version. Missing state means an empty schema-v1 workspace; it is not an error and does not alter existing architecture snapshots.

There is no pre-existing writable diagram store to migrate. Existing CAVE architecture, Git, activity, conversation, usage, dashboard, and settings payloads remain backward compatible. Future migrations are explicit `n -> n+1` transformations; unsupported future versions fail clearly instead of silently dropping data.

## Adapters and projections

- HTTP exposes version-stable semantic DTOs and thin route handlers.
- MCP exposes the same application operations and read models; it has no independent schema.
- Kanban derives cards from trackable source objects.
- Semantic JSON export is versioned and excludes layout by default.
- Human-readable export is generated from the canonical semantic model.
- Architecture UI may show a binding/tracking marker when a semantic entity references an architecture node.

### HTTP surface

Every route resolves the opaque workspace catalog ID first and passes its absolute registered root to the single application service.

- `GET /api/semantic-workspace?workspace={workspaceId}`
- `POST /api/semantic-workspace/operations?workspace={workspaceId}`
- `POST /api/semantic-workspace/undo?workspace={workspaceId}`
- `POST /api/semantic-workspace/redo?workspace={workspaceId}`
- `GET /api/semantic-workspace/summary?workspace={workspaceId}`
- `GET /api/semantic-workspace/diagrams/{diagramId}?workspace={workspaceId}`
- `GET /api/semantic-workspace/entities/{entityId}/context?workspace={workspaceId}&depth={0..8}&maximumEntities={1..500}`
- `GET /api/semantic-workspace/export?workspace={workspaceId}&format=json|markdown&includePresentation=false`

### MCP surface

The MCP adapter exposes the same contracts for the exact workspace supplied to the CAVE app:

- `cave_get_semantic_workspace`
- `cave_get_semantic_summary`
- `cave_get_semantic_diagram`
- `cave_get_semantic_entity_context`
- `cave_apply_semantic_operations`
- `cave_undo_semantic_workspace`
- `cave_redo_semantic_workspace`
- `cave_export_semantic_workspace`

Both HTTP and MCP accept the same polymorphic operation discriminator, `operation`, and both exports return `{ "text": "..." }`. A complete versioned example is in `docs/examples/Semantic_Workspace_Example.json`.

## Initial delivery slice

The first coherent slice includes the schema-v1 aggregate, validation, atomic persistence, batch/undo/redo, semantic JSON and text export, configurable Kanban boards, entity/diagram tracking with manual progress, text/checklist/code/math/image-reference/drawing-foundation blocks, child diagrams, cross-navigation, HTTP/MCP operations, and responsive UI projections.

Checklist/child-derived progress, binary asset upload/storage, full drawing tools, import, collaboration/CRDT, and richer syntax/math renderers remain prepared extension points rather than hidden alternate implementations.
