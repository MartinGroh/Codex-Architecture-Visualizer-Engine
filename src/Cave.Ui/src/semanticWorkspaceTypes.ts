export const semanticWorkspaceSchemaVersion = 1 as const

export interface SemanticWorkspace {
  schemaVersion: typeof semanticWorkspaceSchemaVersion
  id: string
  name: string
  revision: number
  updatedAtUtc: string
  diagrams: SemanticDiagram[]
  entities: SemanticEntity[]
  relationships: SemanticRelationship[]
  boards: SemanticKanbanBoard[]
  assets: SemanticAssetReference[]
}

export interface SemanticDiagram {
  id: string
  title: string
  purpose: string | null
  parentDiagramId: string | null
  tracking: SemanticTrackingFacet | null
  view: SemanticDiagramView
  metadata: Record<string, string>
}

export interface SemanticEntity {
  id: string
  diagramId: string
  type: string
  title: string
  description: string | null
  parentEntityId: string | null
  childDiagramId: string | null
  architectureNodeId: string | null
  blocks: SemanticContentBlock[]
  tracking: SemanticTrackingFacet | null
  tags: string[]
  metadata: Record<string, string>
}

export interface SemanticRelationship {
  id: string
  diagramId: string
  sourceEntityId: string
  targetEntityId: string
  type: string
  label: string | null
  metadata: Record<string, string>
}

export interface SemanticDiagramView {
  zoom: number | null
  viewportX: number | null
  viewportY: number | null
  entities: SemanticEntityPresentation[]
}

export interface SemanticEntityPresentation {
  entityId: string
  x: number
  y: number
  width: number | null
  height: number | null
  collapsed: boolean
  manuallyPositioned: boolean
  styleToken: string | null
}

export type SemanticProgressMode = 'Manual' | 'Checklist' | 'Children'

export interface SemanticTrackingFacet {
  boardId: string
  columnId: string
  progress: { mode: SemanticProgressMode; value: number | null }
  metadata: Record<string, string>
}

export interface SemanticKanbanBoard {
  id: string
  title: string
  columns: SemanticKanbanColumn[]
  metadata: Record<string, string>
}

export interface SemanticKanbanColumn {
  id: string
  title: string
  order: number
  colorToken: string | null
}

export interface SemanticAssetReference {
  id: string
  relativePath: string
  mediaType: string
  caption: string | null
  altText: string
  metadata: Record<string, string>
}

export type SemanticContentBlock =
  | { id: string; kind: 'text'; text: string }
  | { id: string; kind: 'checklist'; items: SemanticChecklistItem[] }
  | { id: string; kind: 'code'; source: string; language: string; filename: string | null }
  | { id: string; kind: 'math'; source: string }
  | { id: string; kind: 'image'; assetId: string; caption: string | null; altText: string }
  | { id: string; kind: 'drawing'; strokes: SemanticDrawingStroke[] }

export interface SemanticChecklistItem {
  id: string
  text: string
  completed: boolean
  order: number
}

export interface SemanticDrawingStroke {
  id: string
  points: Array<{ x: number; y: number; pressure: number }>
  color: string
  width: number
}

export type SemanticTrackable =
  | { kind: 'diagram'; value: SemanticDiagram }
  | { kind: 'entity'; value: SemanticEntity }

export type SemanticActorType = 'Human' | 'Codex' | 'Copilot' | 'Agent' | 'System'

export interface SemanticActor {
  type: SemanticActorType
  id: string | null
  name: string | null
}

export type SemanticWorkspaceOperation =
  | { operation: 'create-diagram'; diagram: SemanticDiagram }
  | { operation: 'update-diagram'; diagram: SemanticDiagram }
  | { operation: 'delete-diagram'; diagramId: string }
  | { operation: 'create-entity'; entity: SemanticEntity }
  | { operation: 'update-entity'; entity: SemanticEntity }
  | { operation: 'delete-entity'; entityId: string }
  | { operation: 'create-relationship'; relationship: SemanticRelationship }
  | { operation: 'update-relationship'; relationship: SemanticRelationship }
  | { operation: 'delete-relationship'; relationshipId: string }
  | { operation: 'add-content-block'; entityId: string; block: SemanticContentBlock }
  | { operation: 'update-content-block'; entityId: string; block: SemanticContentBlock }
  | { operation: 'delete-content-block'; entityId: string; blockId: string }
  | { operation: 'add-checklist-item'; entityId: string; blockId: string; item: SemanticChecklistItem }
  | { operation: 'update-checklist-item'; entityId: string; blockId: string; item: SemanticChecklistItem }
  | { operation: 'delete-checklist-item'; entityId: string; blockId: string; itemId: string }
  | { operation: 'reorder-checklist-items'; entityId: string; blockId: string; itemIds: string[] }
  | { operation: 'create-board'; board: SemanticKanbanBoard }
  | { operation: 'update-board'; board: SemanticKanbanBoard }
  | { operation: 'delete-board'; boardId: string }
  | { operation: 'add-board-column'; boardId: string; column: SemanticKanbanColumn }
  | { operation: 'update-board-column'; boardId: string; column: SemanticKanbanColumn }
  | { operation: 'delete-board-column'; boardId: string; columnId: string }
  | { operation: 'set-diagram-tracking'; diagramId: string; tracking: SemanticTrackingFacet | null }
  | { operation: 'set-entity-tracking'; entityId: string; tracking: SemanticTrackingFacet | null }
  | { operation: 'link-child-diagram'; entityId: string; childDiagramId: string | null }
  | { operation: 'update-diagram-view'; diagramId: string; view: SemanticDiagramView }
  | { operation: 'create-asset-reference'; asset: SemanticAssetReference }
  | { operation: 'update-asset-reference'; asset: SemanticAssetReference }
  | { operation: 'delete-asset-reference'; assetId: string }

export interface SemanticWorkspaceBatch {
  actor: SemanticActor
  summary: string
  operations: SemanticWorkspaceOperation[]
}

export interface SemanticWorkspaceError {
  code: string
  message: string
  operationIndex: number | null
  targetId: string | null
  alternatives: string[]
}

export interface SemanticWorkspaceTransaction {
  id: string
  actor: SemanticActor
  occurredAtUtc: string
  summary: string
  affectedIds: string[]
  operationCount: number
}

export interface SemanticWorkspaceOperationResult {
  succeeded: boolean
  workspace: SemanticWorkspace
  createdIds: string[]
  errors: SemanticWorkspaceError[]
  transaction: SemanticWorkspaceTransaction | null
}
