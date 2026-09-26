import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type FormEvent,
  type ReactNode,
} from 'react'
import {
  Background,
  BackgroundVariant,
  Controls,
  Handle,
  MarkerType,
  MiniMap,
  Position,
  ReactFlow,
  type Connection,
  type Edge,
  type Node,
  type NodeProps,
} from '@xyflow/react'
import {
  ArrowLeft,
  Boxes,
  CheckSquare2,
  Code2,
  Download,
  GitBranch,
  Image as ImageIcon,
  LayoutPanelTop,
  Link2,
  ListTodo,
  Network,
  PenLine,
  Plus,
  Redo2,
  Save,
  Sigma,
  Tags,
  Trash2,
  Type,
  Undo2,
  X,
} from 'lucide-react'
import { KanbanBoard } from './KanbanBoard'
import { RichContentEditor } from './RichContent'
import type {
  SemanticContentBlock,
  SemanticDiagram,
  SemanticEntity,
  SemanticTrackable,
  SemanticWorkspace,
  SemanticWorkspaceBatch,
  SemanticWorkspaceOperation,
  SemanticWorkspaceOperationResult,
} from './semanticWorkspaceTypes'
import './EngineeringWorkspace.css'

export interface EngineeringWorkspaceClient {
  readSemanticWorkspace(): Promise<SemanticWorkspace>
  applySemanticBatch(batch: SemanticWorkspaceBatch): Promise<SemanticWorkspaceOperationResult>
  undoSemanticWorkspace(): Promise<SemanticWorkspaceOperationResult>
  redoSemanticWorkspace(): Promise<SemanticWorkspaceOperationResult>
  exportSemanticWorkspace(format: 'json' | 'markdown', includePresentation: boolean): Promise<string>
}

export interface EngineeringWorkspaceProps {
  client: EngineeringWorkspaceClient | null
  workspaceName: string
  onOpenArchitectureNode?: (architectureNodeId: string) => void
}

type WorkspaceProjection = 'diagram' | 'kanban'
type SemanticZoomBand = 'far' | 'medium' | 'close'

const semanticZoomThresholds = {
  medium: 0.56,
  close: 0.92,
} as const

const semanticNodeTypes = { semanticEntity: SemanticEntityNode }

/**
 * Hosts the editable semantic workspace while keeping diagram, Kanban, export, and AI operations
 * as projections over one canonical aggregate.
 */
export function EngineeringWorkspace({
  client,
  workspaceName,
  onOpenArchitectureNode,
}: EngineeringWorkspaceProps) {
  const [workspace, setWorkspace] = useState<SemanticWorkspace | null>(null)
  const [projection, setProjection] = useState<WorkspaceProjection>('diagram')
  const [selectedDiagramId, setSelectedDiagramId] = useState<string | null>(null)
  const [selectedEntityId, setSelectedEntityId] = useState<string | null>(null)
  const [entityDraft, setEntityDraft] = useState<SemanticEntity | null>(null)
  const [selectedBoardId, setSelectedBoardId] = useState<string | null>(null)
  const [zoomBand, setZoomBand] = useState<SemanticZoomBand>('medium')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [newDiagramTitle, setNewDiagramTitle] = useState('')
  const [newEntityTitle, setNewEntityTitle] = useState('')
  const [newColumnTitle, setNewColumnTitle] = useState('')
  const [relationshipTargetId, setRelationshipTargetId] = useState('')
  const [relationshipType, setRelationshipType] = useState('generic')
  const [newAssetPath, setNewAssetPath] = useState('')
  const [newAssetAltText, setNewAssetAltText] = useState('')

  const refresh = useCallback(async () => {
    if (client === null) return
    setBusy(true)
    setError(null)
    try {
      const next = await client.readSemanticWorkspace()
      setWorkspace(next)
      setSelectedDiagramId((current) => current && next.diagrams.some((diagram) => diagram.id === current)
        ? current
        : next.diagrams[0]?.id ?? null)
      setSelectedBoardId((current) => current && next.boards.some((board) => board.id === current)
        ? current
        : next.boards[0]?.id ?? null)
    } catch (reason: unknown) {
      setError(errorMessage(reason))
    } finally {
      setBusy(false)
    }
  }, [client])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const commit = useCallback(async (summary: string, operations: SemanticWorkspaceOperation[]) => {
    if (client === null || operations.length === 0) return null
    setBusy(true)
    setError(null)
    try {
      const result = await client.applySemanticBatch({
        actor: { type: 'Human', id: null, name: 'CAVE user' },
        summary,
        operations,
      })
      setWorkspace(result.workspace)
      if (!result.succeeded) {
        setError(result.errors.map((item) => item.message).join(' '))
        return null
      }
      return result
    } catch (reason: unknown) {
      setError(errorMessage(reason))
      return null
    } finally {
      setBusy(false)
    }
  }, [client])

  const selectedDiagram = workspace?.diagrams.find((diagram) => diagram.id === selectedDiagramId) ?? null
  const selectedEntity = workspace?.entities.find((entity) => entity.id === selectedEntityId) ?? null
  const selectedBoard = workspace?.boards.find((board) => board.id === selectedBoardId) ?? null

  useEffect(() => {
    setEntityDraft(selectedEntity)
  }, [selectedEntity])

  useEffect(() => {
    if (workspace === null || selectedEntityId === null) return
    if (!workspace.entities.some((entity) => entity.id === selectedEntityId)) {
      setSelectedEntityId(null)
    }
  }, [selectedEntityId, workspace])

  const diagramEntities = useMemo(
    () => workspace?.entities.filter((entity) => entity.diagramId === selectedDiagramId) ?? [],
    [selectedDiagramId, workspace],
  )
  const diagramRelationships = useMemo(
    () => workspace?.relationships.filter((relationship) => relationship.diagramId === selectedDiagramId) ?? [],
    [selectedDiagramId, workspace],
  )
  const flowNodes = useMemo(
    () => createFlowNodes(selectedDiagram, diagramEntities, zoomBand),
    [diagramEntities, selectedDiagram, zoomBand],
  )
  const flowEdges = useMemo(
    () => createFlowEdges(diagramRelationships),
    [diagramRelationships],
  )

  const createStarterWorkspace = async () => {
    const boardId = stableId('board')
    const diagramId = stableId('diagram')
    const columns = ['Planned', 'Doing', 'Testing', 'Done'].map((title, order) => ({
      id: stableId('column'),
      title,
      order,
      colorToken: ['#8171f2', '#1f9c94', '#c98b32', '#4d8c67'][order],
    }))
    const result = await commit('Create the starter engineering workspace', [
      {
        operation: 'create-board',
        board: { id: boardId, title: 'Development', columns, metadata: {} },
      },
      {
        operation: 'create-diagram',
        diagram: emptyDiagram(diagramId, 'System design'),
      },
    ])
    if (result?.succeeded) {
      setSelectedBoardId(boardId)
      setSelectedDiagramId(diagramId)
    }
  }

  const addDiagram = async (event: FormEvent) => {
    event.preventDefault()
    const title = newDiagramTitle.trim()
    if (!title) return
    const id = stableId('diagram')
    const result = await commit(`Create diagram ${title}`, [{
      operation: 'create-diagram',
      diagram: emptyDiagram(id, title),
    }])
    if (result?.succeeded) {
      setNewDiagramTitle('')
      setSelectedDiagramId(id)
      setSelectedEntityId(null)
    }
  }

  const addEntity = async (event: FormEvent) => {
    event.preventDefault()
    const title = newEntityTitle.trim()
    if (!selectedDiagram || !title) return
    const id = stableId('entity')
    const entity: SemanticEntity = {
      id,
      diagramId: selectedDiagram.id,
      type: 'component',
      title,
      description: null,
      parentEntityId: null,
      childDiagramId: null,
      architectureNodeId: null,
      blocks: [],
      tracking: null,
      tags: [],
      metadata: {},
    }
    const result = await commit(`Create ${title}`, [{ operation: 'create-entity', entity }])
    if (result?.succeeded) {
      setNewEntityTitle('')
      setSelectedEntityId(id)
    }
  }

  const saveEntity = async () => {
    if (!entityDraft) return
    await commit(`Update ${entityDraft.title}`, [{ operation: 'update-entity', entity: entityDraft }])
  }

  const deleteSelectedEntity = async () => {
    if (!selectedEntity) return
    const dependentRelationships = workspace?.relationships.filter((relationship) =>
      relationship.sourceEntityId === selectedEntity.id || relationship.targetEntityId === selectedEntity.id) ?? []
    const operations: SemanticWorkspaceOperation[] = [
      ...dependentRelationships.map((relationship): SemanticWorkspaceOperation => ({
        operation: 'delete-relationship',
        relationshipId: relationship.id,
      })),
      { operation: 'delete-entity', entityId: selectedEntity.id },
    ]
    const result = await commit(`Delete ${selectedEntity.title}`, operations)
    if (result?.succeeded) setSelectedEntityId(null)
  }

  const connectSelectedEntity = async (event: FormEvent) => {
    event.preventDefault()
    if (!selectedDiagram || !selectedEntity || !relationshipTargetId) return
    await commit(`Connect ${selectedEntity.title}`, [{
      operation: 'create-relationship',
      relationship: {
        id: stableId('relationship'),
        diagramId: selectedDiagram.id,
        sourceEntityId: selectedEntity.id,
        targetEntityId: relationshipTargetId,
        type: relationshipType.trim() || 'generic',
        label: null,
        metadata: {},
      },
    }])
  }

  const connectFlowNodes = async (connection: Connection) => {
    if (!selectedDiagram || !connection.source || !connection.target) return
    await commit('Connect diagram entities', [{
      operation: 'create-relationship',
      relationship: {
        id: stableId('relationship'),
        diagramId: selectedDiagram.id,
        sourceEntityId: connection.source,
        targetEntityId: connection.target,
        type: 'generic',
        label: null,
        metadata: {},
      },
    }])
  }

  const persistPosition = async (node: Node) => {
    if (!selectedDiagram) return
    const current = selectedDiagram.view.entities.filter((item) => item.entityId !== node.id)
    await commit('Arrange semantic diagram', [{
      operation: 'update-diagram-view',
      diagramId: selectedDiagram.id,
      view: {
        ...selectedDiagram.view,
        entities: [...current, {
          entityId: node.id,
          x: node.position.x,
          y: node.position.y,
          width: null,
          height: null,
          collapsed: false,
          manuallyPositioned: true,
          styleToken: null,
        }],
      },
    }])
  }

  const createChildDiagram = async () => {
    if (!selectedEntity || !selectedDiagram) return
    const childId = stableId('diagram')
    const result = await commit(`Create detail diagram for ${selectedEntity.title}`, [
      {
        operation: 'create-diagram',
        diagram: {
          ...emptyDiagram(childId, `${selectedEntity.title} details`),
          parentDiagramId: selectedDiagram.id,
        },
      },
      { operation: 'link-child-diagram', entityId: selectedEntity.id, childDiagramId: childId },
    ])
    if (result?.succeeded) {
      setSelectedDiagramId(childId)
      setSelectedEntityId(null)
    }
  }

  const trackSelectedEntity = async () => {
    if (!selectedEntity || !selectedBoard || selectedBoard.columns.length === 0) return
    const column = [...selectedBoard.columns].sort((left, right) => left.order - right.order)[0]
    await commit(`Track ${selectedEntity.title} on ${selectedBoard.title}`, [{
      operation: 'set-entity-tracking',
      entityId: selectedEntity.id,
      tracking: {
        boardId: selectedBoard.id,
        columnId: column.id,
        progress: { mode: 'Manual', value: 0 },
        metadata: {},
      },
    }])
  }

  const moveTrackable = async (trackable: SemanticTrackable, columnId: string) => {
    const tracking = trackable.value.tracking
    if (!tracking) return
    const operation: SemanticWorkspaceOperation = trackable.kind === 'entity'
      ? {
          operation: 'set-entity-tracking',
          entityId: trackable.value.id,
          tracking: { ...tracking, columnId },
        }
      : {
          operation: 'set-diagram-tracking',
          diagramId: trackable.value.id,
          tracking: { ...tracking, columnId },
        }
    await commit(`Move ${trackable.value.title}`, [operation])
  }

  const setTrackableProgress = async (trackable: SemanticTrackable, value: number) => {
    const tracking = trackable.value.tracking
    if (!tracking) return
    const nextTracking = { ...tracking, progress: { mode: 'Manual' as const, value } }
    const operation: SemanticWorkspaceOperation = trackable.kind === 'entity'
      ? { operation: 'set-entity-tracking', entityId: trackable.value.id, tracking: nextTracking }
      : { operation: 'set-diagram-tracking', diagramId: trackable.value.id, tracking: nextTracking }
    await commit(`Set ${trackable.value.title} progress to ${value}%`, [operation])
  }

  const openTrackable = (trackable: SemanticTrackable) => {
    setProjection('diagram')
    if (trackable.kind === 'diagram') {
      setSelectedDiagramId(trackable.value.id)
      setSelectedEntityId(null)
    } else {
      setSelectedDiagramId(trackable.value.diagramId)
      setSelectedEntityId(trackable.value.id)
    }
  }

  const addBoardColumn = async (event: FormEvent) => {
    event.preventDefault()
    const title = newColumnTitle.trim()
    if (!selectedBoard || !title) return
    const order = selectedBoard.columns.reduce((max, column) => Math.max(max, column.order), -1) + 1
    const result = await commit(`Add ${title} column`, [{
      operation: 'add-board-column',
      boardId: selectedBoard.id,
      column: { id: stableId('column'), title, order, colorToken: null },
    }])
    if (result?.succeeded) setNewColumnTitle('')
  }

  const addAssetReference = async (event: FormEvent) => {
    event.preventDefault()
    const relativePath = newAssetPath.trim()
    const altText = newAssetAltText.trim()
    if (!relativePath || !altText) return
    const extension = relativePath.split('.').at(-1)?.toLowerCase()
    const mediaType = extension === 'svg'
      ? 'image/svg+xml'
      : extension === 'webp'
        ? 'image/webp'
        : extension === 'jpg' || extension === 'jpeg'
          ? 'image/jpeg'
          : 'image/png'
    const result = await commit(`Reference asset ${relativePath}`, [{
      operation: 'create-asset-reference',
      asset: {
        id: stableId('asset'),
        relativePath,
        mediaType,
        caption: null,
        altText,
        metadata: {},
      },
    }])
    if (result?.succeeded) {
      setNewAssetPath('')
      setNewAssetAltText('')
    }
  }

  const runHistory = async (direction: 'undo' | 'redo') => {
    if (client === null) return
    setBusy(true)
    setError(null)
    try {
      const result = direction === 'undo'
        ? await client.undoSemanticWorkspace()
        : await client.redoSemanticWorkspace()
      setWorkspace(result.workspace)
      if (!result.succeeded && result.errors.length > 0) {
        setError(result.errors.map((item) => item.message).join(' '))
      }
    } catch (reason: unknown) {
      setError(errorMessage(reason))
    } finally {
      setBusy(false)
    }
  }

  const downloadExport = async (format: 'json' | 'markdown') => {
    if (client === null) return
    setBusy(true)
    setError(null)
    try {
      const text = await client.exportSemanticWorkspace(format, false)
      const extension = format === 'json' ? 'json' : 'md'
      const blob = new Blob([text], { type: format === 'json' ? 'application/json' : 'text/markdown' })
      const href = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = href
      anchor.download = `${safeFileName(workspace?.name ?? workspaceName)}.semantic.${extension}`
      anchor.click()
      URL.revokeObjectURL(href)
    } catch (reason: unknown) {
      setError(errorMessage(reason))
    } finally {
      setBusy(false)
    }
  }

  if (client === null || workspace === null) {
    return (
      <section className="engineering-workspace engineering-workspace--loading" aria-live="polite">
        <Network size={24} aria-hidden="true" />
        <h2>{error ?? 'Connecting the semantic workspace…'}</h2>
        <p>The architecture graph remains available while the editable workspace is loaded.</p>
        {error && <button type="button" onClick={() => void refresh()}>Try again</button>}
      </section>
    )
  }

  if (workspace.diagrams.length === 0 && workspace.boards.length === 0) {
    return (
      <section className="engineering-workspace engineering-workspace--empty">
        <Network size={30} aria-hidden="true" />
        <h2>Start the engineering workspace</h2>
        <p>Create one semantic diagram and a configurable Development board. Both remain views over the same entities.</p>
        {error && <p className="engineering-workspace__error" role="alert">{error}</p>}
        <button type="button" disabled={busy} onClick={() => void createStarterWorkspace()}>
          <Plus size={16} /> Create starter workspace
        </button>
      </section>
    )
  }

  return (
    <section className="engineering-workspace" aria-label="Engineering workspace">
      <header className="engineering-workspace__toolbar">
        <div className="engineering-workspace__projection" role="tablist" aria-label="Workspace projection">
          <button
            type="button"
            role="tab"
            aria-selected={projection === 'diagram'}
            onClick={() => setProjection('diagram')}
          >
            <Network size={15} /> Diagrams
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={projection === 'kanban'}
            onClick={() => setProjection('kanban')}
          >
            <LayoutPanelTop size={15} /> Kanban
          </button>
        </div>
        <div className="engineering-workspace__actions">
          <button type="button" disabled={busy} title="Undo the last semantic transaction" onClick={() => void runHistory('undo')}>
            <Undo2 size={14} /> Undo
          </button>
          <button type="button" disabled={busy} title="Redo the last undone semantic transaction" onClick={() => void runHistory('redo')}>
            <Redo2 size={14} /> Redo
          </button>
          <button type="button" disabled={busy} onClick={() => void downloadExport('json')}>
            <Download size={14} /> JSON
          </button>
          <button type="button" disabled={busy} onClick={() => void downloadExport('markdown')}>
            <Download size={14} /> Markdown
          </button>
        </div>
        <span className="engineering-workspace__revision" title={workspace.updatedAtUtc}>
          Revision {workspace.revision}
        </span>
      </header>

      {error && <p className="engineering-workspace__error" role="alert">{error}</p>}

      {projection === 'kanban' ? (
        <div className="engineering-workspace__kanban">
          <div className="engineering-workspace__board-select">
            <label>
              <span>Board</span>
              <select value={selectedBoardId ?? ''} onChange={(event) => setSelectedBoardId(event.currentTarget.value)}>
                {workspace.boards.map((board) => <option key={board.id} value={board.id}>{board.title}</option>)}
              </select>
            </label>
            {selectedBoard && (
              <form onSubmit={(event) => void addBoardColumn(event)}>
                <label>
                  <span>New column</span>
                  <input value={newColumnTitle} onChange={(event) => setNewColumnTitle(event.currentTarget.value)} placeholder="Review" />
                </label>
                <button type="submit" disabled={busy || !newColumnTitle.trim()}><Plus size={14} /> Add column</button>
              </form>
            )}
          </div>
          {selectedBoard ? (
            <KanbanBoard
              workspace={workspace}
              boardId={selectedBoard.id}
              readOnly={busy}
              onMoveTrackable={(trackable, columnId) => void moveTrackable(trackable, columnId)}
              onSetManualProgress={(trackable, value) => void setTrackableProgress(trackable, value)}
              onOpenSource={openTrackable}
              onOpenChildDiagram={(diagramId) => {
                setProjection('diagram')
                setSelectedDiagramId(diagramId)
                setSelectedEntityId(null)
              }}
            />
          ) : (
            <p className="engineering-workspace__empty-note">Create a board through the starter workspace before tracking work.</p>
          )}
        </div>
      ) : (
        <div className={`engineering-workspace__diagram ${entityDraft ? 'has-inspector' : ''}`}>
          <aside className="semantic-diagram-list" aria-label="Semantic diagrams">
            <div className="semantic-diagram-list__heading">
              <strong>Diagrams</strong>
              <span>{workspace.diagrams.length}</span>
            </div>
            {workspace.diagrams.map((diagram) => (
              <button
                key={diagram.id}
                type="button"
                className={diagram.id === selectedDiagramId ? 'is-active' : ''}
                onClick={() => {
                  setSelectedDiagramId(diagram.id)
                  setSelectedEntityId(null)
                }}
              >
                <Network size={14} />
                <span>{diagram.title}</span>
                {diagram.parentDiagramId && <GitBranch size={12} aria-label="Child diagram" />}
              </button>
            ))}
            <form onSubmit={(event) => void addDiagram(event)}>
              <label>
                <span>New diagram</span>
                <input value={newDiagramTitle} onChange={(event) => setNewDiagramTitle(event.currentTarget.value)} placeholder="Telemetry flow" />
              </label>
              <button type="submit" disabled={busy || !newDiagramTitle.trim()}><Plus size={14} /> Add</button>
            </form>
          </aside>

          <section className="semantic-diagram-stage" aria-label={selectedDiagram?.title ?? 'Semantic diagram'}>
            <header>
              <div>
                {selectedDiagram?.parentDiagramId && (
                  <button
                    type="button"
                    onClick={() => {
                      setSelectedDiagramId(selectedDiagram.parentDiagramId)
                      setSelectedEntityId(null)
                    }}
                  >
                    <ArrowLeft size={13} /> Parent diagram
                  </button>
                )}
                <h2>{selectedDiagram?.title ?? 'Choose a diagram'}</h2>
                {selectedDiagram?.purpose && <p>{selectedDiagram.purpose}</p>}
              </div>
              {selectedDiagram && (
                <form onSubmit={(event) => void addEntity(event)}>
                  <label>
                    <span>New entity</span>
                    <input value={newEntityTitle} onChange={(event) => setNewEntityTitle(event.currentTarget.value)} placeholder="Request handler" />
                  </label>
                  <button type="submit" disabled={busy || !newEntityTitle.trim()}><Plus size={14} /> Add entity</button>
                </form>
              )}
            </header>

            <div className={`semantic-flow semantic-flow--${zoomBand}`}>
              {selectedDiagram && flowNodes.length > 0 ? (
                <ReactFlow
                  key={selectedDiagram.id}
                  nodes={flowNodes}
                  edges={flowEdges}
                  nodeTypes={semanticNodeTypes}
                  fitView
                  fitViewOptions={{ padding: 0.2, maxZoom: 1 }}
                  minZoom={0.18}
                  maxZoom={1.65}
                  nodesDraggable={!busy}
                  nodesConnectable={!busy}
                  onNodeClick={(_, node) => setSelectedEntityId(node.id)}
                  onNodeDragStop={(_, node) => void persistPosition(node)}
                  onConnect={(connection) => void connectFlowNodes(connection)}
                  onMove={(_, viewport) => setZoomBand(semanticZoomBand(viewport.zoom))}
                >
                  <Background variant={BackgroundVariant.Dots} gap={20} size={1} />
                  <Controls showInteractive={false} />
                  <MiniMap pannable zoomable />
                </ReactFlow>
              ) : (
                <div className="semantic-flow__empty">
                  <Boxes size={25} />
                  <strong>{selectedDiagram ? 'Add the first entity' : 'Choose or create a diagram'}</strong>
                  <span>Semantic elements receive automatic positions until you arrange them.</span>
                </div>
              )}
            </div>
          </section>

          {entityDraft && selectedDiagram && (
            <aside className="semantic-entity-inspector" aria-label={`Edit ${entityDraft.title}`}>
              <header>
                <div>
                  <span>{entityDraft.type}</span>
                  <h2>{entityDraft.title}</h2>
                </div>
                <button type="button" aria-label="Close entity editor" onClick={() => setSelectedEntityId(null)}><X size={16} /></button>
              </header>

              <div className="semantic-entity-inspector__fields">
                <label>
                  <span>Title</span>
                  <input value={entityDraft.title} onChange={(event) => setEntityDraft({ ...entityDraft, title: event.currentTarget.value })} />
                </label>
                <label>
                  <span>Type</span>
                  <input value={entityDraft.type} onChange={(event) => setEntityDraft({ ...entityDraft, type: event.currentTarget.value })} />
                </label>
                <label>
                  <span>Description</span>
                  <textarea value={entityDraft.description ?? ''} onChange={(event) => setEntityDraft({ ...entityDraft, description: event.currentTarget.value || null })} />
                </label>
                <label>
                  <span>Tags</span>
                  <input
                    value={entityDraft.tags.join(', ')}
                    onChange={(event) => setEntityDraft({
                      ...entityDraft,
                      tags: event.currentTarget.value.split(',').map((tag) => tag.trim()).filter(Boolean),
                    })}
                  />
                </label>
              </div>

              <div className="semantic-entity-inspector__buttons">
                <button type="button" disabled={busy || !entityDraft.title.trim()} onClick={() => void saveEntity()}><Save size={14} /> Save</button>
                {entityDraft.architectureNodeId && onOpenArchitectureNode && (
                  <button type="button" onClick={() => onOpenArchitectureNode(entityDraft.architectureNodeId!)}><Link2 size={14} /> Architecture</button>
                )}
                {entityDraft.childDiagramId ? (
                  <button
                    type="button"
                    onClick={() => {
                      setSelectedDiagramId(entityDraft.childDiagramId)
                      setSelectedEntityId(null)
                    }}
                  ><GitBranch size={14} /> Open details</button>
                ) : (
                  <button type="button" disabled={busy} onClick={() => void createChildDiagram()}><GitBranch size={14} /> Create details</button>
                )}
                {entityDraft.tracking ? (
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() => void commit(`Untrack ${entityDraft.title}`, [{
                      operation: 'set-entity-tracking', entityId: entityDraft.id, tracking: null,
                    }])}
                  ><ListTodo size={14} /> Untrack</button>
                ) : (
                  <button type="button" disabled={busy || !selectedBoard} onClick={() => void trackSelectedEntity()}><ListTodo size={14} /> Track</button>
                )}
                <button className="is-destructive" type="button" disabled={busy} onClick={() => void deleteSelectedEntity()}><Trash2 size={14} /> Delete</button>
              </div>

              <form className="semantic-relationship-form" onSubmit={(event) => void connectSelectedEntity(event)}>
                <strong>Relationship</strong>
                <label>
                  <span>Type</span>
                  <input value={relationshipType} onChange={(event) => setRelationshipType(event.currentTarget.value)} />
                </label>
                <label>
                  <span>Target</span>
                  <select value={relationshipTargetId} onChange={(event) => setRelationshipTargetId(event.currentTarget.value)}>
                    <option value="">Choose an entity</option>
                    {diagramEntities.filter((entity) => entity.id !== entityDraft.id).map((entity) => (
                      <option key={entity.id} value={entity.id}>{entity.title}</option>
                    ))}
                  </select>
                </label>
                <button type="submit" disabled={busy || !relationshipTargetId}><Link2 size={14} /> Connect</button>
              </form>

              <form className="semantic-asset-form" onSubmit={(event) => void addAssetReference(event)}>
                <strong>Asset reference</strong>
                <label>
                  <span>Workspace-relative image path</span>
                  <input value={newAssetPath} onChange={(event) => setNewAssetPath(event.currentTarget.value)} placeholder="docs/assets/diagram.png" />
                </label>
                <label>
                  <span>Alternative text</span>
                  <input value={newAssetAltText} onChange={(event) => setNewAssetAltText(event.currentTarget.value)} placeholder="Describe the image" />
                </label>
                <button type="submit" disabled={busy || !newAssetPath.trim() || !newAssetAltText.trim()}>
                  <ImageIcon size={14} /> Add reference
                </button>
              </form>

              <div className="semantic-block-actions" aria-label="Add content block">
                <span>Add content</span>
                {contentBlockFactories.map(({ kind, label, icon, create }) => (
                  <button
                    key={kind}
                    type="button"
                    title={`Add ${label.toLowerCase()}`}
                    disabled={kind === 'image' && workspace.assets.length === 0}
                    onClick={() => {
                      const block = create()
                      const nextBlock = block.kind === 'image'
                        ? {
                            ...block,
                            assetId: workspace.assets[0].id,
                            altText: workspace.assets[0].altText,
                          }
                        : block
                      setEntityDraft({ ...entityDraft, blocks: [...entityDraft.blocks, nextBlock] })
                    }}
                  >
                    {icon} <span>{label}</span>
                  </button>
                ))}
              </div>
              <RichContentEditor
                heading={`${entityDraft.title} content`}
                blocks={entityDraft.blocks}
                assets={workspace.assets}
                onChangeBlock={(block) => setEntityDraft({
                  ...entityDraft,
                  blocks: entityDraft.blocks.map((candidate) => candidate.id === block.id ? block : candidate),
                })}
                onDeleteBlock={(blockId) => setEntityDraft({
                  ...entityDraft,
                  blocks: entityDraft.blocks.filter((block) => block.id !== blockId),
                })}
              />
            </aside>
          )}
        </div>
      )}
    </section>
  )
}

interface SemanticEntityNodeData extends Record<string, unknown> {
  entity: SemanticEntity
  zoomBand: SemanticZoomBand
}

function SemanticEntityNode({ data, selected }: NodeProps<Node<SemanticEntityNodeData>>) {
  const { entity, zoomBand } = data
  const checklistCount = entity.blocks
    .filter((block) => block.kind === 'checklist')
    .reduce((count, block) => count + block.items.length, 0)
  const completeCount = entity.blocks
    .filter((block) => block.kind === 'checklist')
    .reduce((count, block) => count + block.items.filter((item) => item.completed).length, 0)
  const progress = entity.tracking?.progress.value

  return (
    <article className={`semantic-entity-node ${selected ? 'is-selected' : ''}`}>
      <Handle type="target" position={Position.Left} />
      <header>
        <Boxes size={16} aria-hidden="true" />
        <span>{entity.type}</span>
        {entity.childDiagramId && <GitBranch size={14} aria-label="Has detail diagram" />}
      </header>
      <strong>{entity.title}</strong>
      {zoomBand !== 'far' && entity.description && <p>{entity.description}</p>}
      {zoomBand !== 'far' && (entity.tracking || entity.blocks.length > 0) && (
        <footer>
          {entity.tracking && <span><ListTodo size={12} /> {progress ?? 0}%</span>}
          {checklistCount > 0 && <span><CheckSquare2 size={12} /> {completeCount}/{checklistCount}</span>}
          {entity.blocks.some((block) => block.kind === 'code') && <Code2 size={12} aria-label="Contains code" />}
          {entity.blocks.some((block) => block.kind === 'image') && <ImageIcon size={12} aria-label="Contains image" />}
          {entity.tags.length > 0 && <span><Tags size={12} /> {entity.tags.length}</span>}
        </footer>
      )}
      {zoomBand === 'close' && entity.tracking && (
        <div className="semantic-entity-node__progress">
          <span style={{ width: `${Math.max(0, Math.min(100, progress ?? 0))}%` }} />
        </div>
      )}
      <Handle type="source" position={Position.Right} />
    </article>
  )
}

function createFlowNodes(
  diagram: SemanticDiagram | null,
  entities: SemanticEntity[],
  zoomBand: SemanticZoomBand,
): Node<SemanticEntityNodeData>[] {
  if (!diagram) return []
  const positions = new Map(diagram.view.entities.map((item) => [item.entityId, item]))
  const columns = Math.max(1, Math.ceil(Math.sqrt(entities.length)))
  return entities.map((entity, index) => {
    const position = positions.get(entity.id)
    return {
      id: entity.id,
      type: 'semanticEntity',
      position: position ? { x: position.x, y: position.y } : {
        x: (index % columns) * 280,
        y: Math.floor(index / columns) * 190,
      },
      data: { entity, zoomBand },
    }
  })
}

function createFlowEdges(relationships: SemanticWorkspace['relationships']): Edge[] {
  return relationships.map((relationship) => ({
    id: relationship.id,
    source: relationship.sourceEntityId,
    target: relationship.targetEntityId,
    label: relationship.label || relationship.type,
    markerEnd: { type: MarkerType.ArrowClosed, width: 14, height: 14 },
    className: `semantic-relationship semantic-relationship--${relationship.type}`,
  }))
}

function semanticZoomBand(zoom: number): SemanticZoomBand {
  if (zoom < semanticZoomThresholds.medium) return 'far'
  if (zoom < semanticZoomThresholds.close) return 'medium'
  return 'close'
}

function emptyDiagram(id: string, title: string): SemanticDiagram {
  return {
    id,
    title,
    purpose: null,
    parentDiagramId: null,
    tracking: null,
    view: { zoom: null, viewportX: null, viewportY: null, entities: [] },
    metadata: {},
  }
}

const contentBlockFactories: Array<{
  kind: SemanticContentBlock['kind']
  label: string
  icon: ReactNode
  create: () => SemanticContentBlock
}> = [
  { kind: 'text', label: 'Text', icon: <Type size={13} />, create: () => ({ id: stableId('block'), kind: 'text', text: '' }) },
  { kind: 'checklist', label: 'Checklist', icon: <CheckSquare2 size={13} />, create: () => ({ id: stableId('block'), kind: 'checklist', items: [{ id: stableId('item'), text: 'First item', completed: false, order: 0 }] }) },
  { kind: 'code', label: 'Code', icon: <Code2 size={13} />, create: () => ({ id: stableId('block'), kind: 'code', source: '', language: 'text', filename: null }) },
  { kind: 'math', label: 'Formula', icon: <Sigma size={13} />, create: () => ({ id: stableId('block'), kind: 'math', source: '' }) },
  { kind: 'image', label: 'Image', icon: <ImageIcon size={13} />, create: () => ({ id: stableId('block'), kind: 'image', assetId: '', caption: null, altText: '' }) },
  { kind: 'drawing', label: 'Drawing', icon: <PenLine size={13} />, create: () => ({ id: stableId('block'), kind: 'drawing', strokes: [] }) },
]

function stableId(prefix: string): string {
  return `${prefix}:${crypto.randomUUID()}`
}

function safeFileName(value: string): string {
  return value.trim().replace(/[^a-z0-9._-]+/gi, '-').replace(/^-+|-+$/g, '') || 'workspace'
}

function errorMessage(reason: unknown): string {
  return reason instanceof Error ? reason.message : String(reason)
}
