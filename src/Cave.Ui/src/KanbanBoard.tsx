import { useMemo, useState, type CSSProperties, type DragEvent } from 'react'
import {
  ArrowUpRight,
  CircleDot,
  GitBranch,
  GripVertical,
  LayoutPanelTop,
  Settings2,
} from 'lucide-react'
import type {
  SemanticEntity,
  SemanticKanbanBoard,
  SemanticTrackable,
  SemanticWorkspace,
} from './semanticWorkspaceTypes'
import './SemanticWorkspace.css'

export interface KanbanBoardProps {
  workspace: SemanticWorkspace
  boardId: string
  onMoveTrackable: (trackable: SemanticTrackable, columnId: string) => void
  onSetManualProgress: (trackable: SemanticTrackable, value: number) => void
  onOpenSource: (trackable: SemanticTrackable) => void
  onOpenChildDiagram?: (diagramId: string) => void
  onConfigureBoard?: (board: SemanticKanbanBoard) => void
  readOnly?: boolean
}

interface DraggedTrackable {
  kind: SemanticTrackable['kind']
  id: string
}

/**
 * Projects tracked semantic diagrams and entities into a configurable Kanban board.
 * Cards remain views of their source objects; this component never owns a second task model.
 */
export function KanbanBoard({
  workspace,
  boardId,
  onMoveTrackable,
  onSetManualProgress,
  onOpenSource,
  onOpenChildDiagram,
  onConfigureBoard,
  readOnly = false,
}: KanbanBoardProps) {
  const [dragged, setDragged] = useState<DraggedTrackable | null>(null)
  const board = workspace.boards.find((candidate) => candidate.id === boardId)
  const columns = useMemo(
    () => [...(board?.columns ?? [])].sort((left, right) => left.order - right.order),
    [board],
  )
  const tracked = useMemo(
    () => deriveTrackedSources(workspace, boardId),
    [workspace, boardId],
  )

  if (!board) {
    return (
      <section className="semantic-workspace-state semantic-workspace-state--error" role="alert">
        <LayoutPanelTop aria-hidden="true" />
        <div>
          <strong>Kanban board unavailable</strong>
          <span>The selected board is no longer part of this semantic workspace.</span>
        </div>
      </section>
    )
  }

  if (columns.length === 0) {
    return (
      <section className="semantic-workspace-state" role="status">
        <LayoutPanelTop aria-hidden="true" />
        <div>
          <strong>{board.title} has no columns yet</strong>
          <span>Add at least one ordered column before tracking engineering work here.</span>
        </div>
        {onConfigureBoard && (
          <button type="button" onClick={() => onConfigureBoard(board)}>
            <Settings2 size={15} /> Configure board
          </button>
        )}
      </section>
    )
  }

  const boardStyle = { '--kanban-column-count': columns.length } as CSSProperties

  const findDraggedTrackable = (event: DragEvent<HTMLElement>) => {
    const fromTransfer = readDraggedTrackable(event.dataTransfer.getData('application/x-cave-trackable'))
    const reference = fromTransfer ?? dragged
    return reference
      ? tracked.find((candidate) => candidate.kind === reference.kind && candidate.value.id === reference.id) ?? null
      : null
  }

  return (
    <section className="kanban-board" aria-labelledby={`kanban-board-${board.id}`}>
      <header className="kanban-board__header">
        <div>
          <h2 id={`kanban-board-${board.id}`}><LayoutPanelTop size={19} /> {board.title}</h2>
          <p>{tracked.length} {tracked.length === 1 ? 'source object' : 'source objects'} tracked without duplicating diagram data.</p>
        </div>
        {onConfigureBoard && (
          <button type="button" onClick={() => onConfigureBoard(board)}>
            <Settings2 size={15} /> Configure
          </button>
        )}
      </header>

      <div className="kanban-board__columns" style={boardStyle}>
        {columns.map((column) => {
          const cards = tracked.filter((trackable) => trackable.value.tracking?.columnId === column.id)
          return (
            <section
              key={column.id}
              className={`kanban-column ${dragged ? 'is-drop-ready' : ''}`}
              aria-labelledby={`kanban-column-${column.id}`}
              onDragOver={(event) => {
                if (!readOnly) {
                  event.preventDefault()
                  event.dataTransfer.dropEffect = 'move'
                }
              }}
              onDrop={(event) => {
                if (readOnly) return
                event.preventDefault()
                const trackable = findDraggedTrackable(event)
                if (trackable && trackable.value.tracking?.columnId !== column.id) {
                  onMoveTrackable(trackable, column.id)
                }
                setDragged(null)
              }}
            >
              <header className="kanban-column__header">
                <span
                  className="kanban-column__signal"
                  style={column.colorToken ? { backgroundColor: column.colorToken } : undefined}
                  aria-hidden="true"
                />
                <h3 id={`kanban-column-${column.id}`}>{column.title}</h3>
                <span aria-label={`${cards.length} cards`}>{cards.length}</span>
              </header>

              <div className="kanban-column__cards">
                {cards.map((trackable) => (
                  <KanbanCard
                    key={`${trackable.kind}:${trackable.value.id}`}
                    trackable={trackable}
                    workspace={workspace}
                    board={board}
                    readOnly={readOnly}
                    onDragStart={(event) => {
                      const reference: DraggedTrackable = { kind: trackable.kind, id: trackable.value.id }
                      setDragged(reference)
                      event.dataTransfer.effectAllowed = 'move'
                      event.dataTransfer.setData('application/x-cave-trackable', JSON.stringify(reference))
                    }}
                    onDragEnd={() => setDragged(null)}
                    onMove={(columnId) => onMoveTrackable(trackable, columnId)}
                    onProgress={(value) => onSetManualProgress(trackable, value)}
                    onOpen={() => onOpenSource(trackable)}
                    onOpenChildDiagram={onOpenChildDiagram}
                  />
                ))}
                {cards.length === 0 && (
                  <p className="kanban-column__empty">Drop tracked work here or choose this column from a card.</p>
                )}
              </div>
            </section>
          )
        })}
      </div>
    </section>
  )
}

interface KanbanCardProps {
  trackable: SemanticTrackable
  workspace: SemanticWorkspace
  board: SemanticKanbanBoard
  readOnly: boolean
  onDragStart: (event: DragEvent<HTMLElement>) => void
  onDragEnd: () => void
  onMove: (columnId: string) => void
  onProgress: (value: number) => void
  onOpen: () => void
  onOpenChildDiagram?: (diagramId: string) => void
}

function KanbanCard({
  trackable,
  workspace,
  board,
  readOnly,
  onDragStart,
  onDragEnd,
  onMove,
  onProgress,
  onOpen,
  onOpenChildDiagram,
}: KanbanCardProps) {
  const tracking = trackable.value.tracking
  if (!tracking) return null

  const isDiagram = trackable.kind === 'diagram'
  const entity = isDiagram ? null : trackable.value as SemanticEntity
  const title = trackable.value.title
  const description = isDiagram ? trackable.value.purpose : entity?.description
  const childDiagramId = entity?.childDiagramId ?? null
  const childCount = isDiagram
    ? workspace.diagrams.filter((diagram) => diagram.parentDiagramId === trackable.value.id).length
    : 0
  const progress = tracking.progress.value ?? 0
  const columns = [...board.columns].sort((left, right) => left.order - right.order)

  return (
    <article
      className="kanban-card"
      draggable={!readOnly}
      onDragStart={onDragStart}
      onDragEnd={onDragEnd}
      data-trackable-kind={trackable.kind}
      data-trackable-id={trackable.value.id}
    >
      <header>
        <GripVertical className="kanban-card__grip" size={15} aria-hidden="true" />
        <span>{isDiagram ? 'Diagram' : entity?.type || 'Entity'}</span>
        {(childDiagramId || childCount > 0) && <GitBranch size={14} aria-label="Has a child diagram" />}
      </header>

      <button className="kanban-card__source" type="button" onClick={onOpen}>
        <strong>{title}</strong>
        <ArrowUpRight size={15} aria-hidden="true" />
        <span className="visually-hidden">Open source {trackable.kind}</span>
      </button>
      {description && <p>{description}</p>}

      {!isDiagram && entity && entity.tags.length > 0 && (
        <div className="kanban-card__tags" aria-label="Tags">
          {entity.tags.slice(0, 3).map((tag) => <span key={tag}>{tag}</span>)}
          {entity.tags.length > 3 && <span>+{entity.tags.length - 3}</span>}
        </div>
      )}

      {(childDiagramId || childCount > 0) && onOpenChildDiagram && (
        <div className="kanban-card__child-links">
          {childDiagramId && (
            <button type="button" onClick={() => onOpenChildDiagram(childDiagramId)}>
              <GitBranch size={13} /> Open child diagram
            </button>
          )}
          {childCount > 0 && (
            <span><GitBranch size={13} /> {childCount} nested {childCount === 1 ? 'diagram' : 'diagrams'}</span>
          )}
        </div>
      )}

      <div className="kanban-card__progress">
        <div>
          <span>{tracking.progress.mode === 'Manual' ? 'Manual progress' : `${tracking.progress.mode} progress`}</span>
          <output>{progress}%</output>
        </div>
        {tracking.progress.mode === 'Manual'
          ? (
              <input
                type="range"
                min="0"
                max="100"
                step="5"
                value={progress}
                disabled={readOnly}
                aria-label={`Progress for ${title}`}
                onChange={(event) => onProgress(Number(event.currentTarget.value))}
              />
            )
          : (
              <span className="kanban-card__derived-progress">
                <CircleDot size={12} /> Derived progress is read-only in schema version 1
              </span>
            )}
      </div>

      <label className="kanban-card__move">
        <span>Column</span>
        <select
          value={tracking.columnId}
          disabled={readOnly}
          aria-label={`Move ${title} to column`}
          onChange={(event) => onMove(event.currentTarget.value)}
        >
          {columns.map((column) => <option key={column.id} value={column.id}>{column.title}</option>)}
        </select>
      </label>
    </article>
  )
}

function deriveTrackedSources(workspace: SemanticWorkspace, boardId: string): SemanticTrackable[] {
  const diagrams: SemanticTrackable[] = workspace.diagrams
    .filter((diagram) => diagram.tracking?.boardId === boardId)
    .map((diagram) => ({ kind: 'diagram', value: diagram }))
  const entities: SemanticTrackable[] = workspace.entities
    .filter((entity) => entity.tracking?.boardId === boardId)
    .map((entity) => ({ kind: 'entity', value: entity }))

  return [...diagrams, ...entities]
}

function readDraggedTrackable(value: string): DraggedTrackable | null {
  if (!value) return null
  try {
    const parsed = JSON.parse(value) as Partial<DraggedTrackable>
    if ((parsed.kind === 'diagram' || parsed.kind === 'entity') && typeof parsed.id === 'string') {
      return { kind: parsed.kind, id: parsed.id }
    }
  } catch {
    return null
  }
  return null
}
