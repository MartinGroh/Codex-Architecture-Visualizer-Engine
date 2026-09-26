import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { KanbanBoard } from './KanbanBoard'
import type { SemanticWorkspace } from './semanticWorkspaceTypes'

afterEach(cleanup)

describe('KanbanBoard', () => {
  it('derives cards from tracked diagrams and entities and preserves configured column order', () => {
    renderBoard()

    const headings = screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)
    expect(headings).toEqual(['Ready', 'In progress', 'Done'])
    expect(screen.getByRole('button', { name: /Architecture plan/i })).toBeTruthy()
    expect(screen.getByRole('button', { name: /Payment boundary/i })).toBeTruthy()
    expect(screen.getAllByText('Payment boundary')).toHaveLength(1)
  })

  it('moves source objects with an accessible control and reports manual progress', () => {
    const onMoveTrackable = vi.fn()
    const onSetManualProgress = vi.fn()
    renderBoard({ onMoveTrackable, onSetManualProgress })

    fireEvent.change(screen.getByRole('combobox', { name: 'Move Payment boundary to column' }), {
      target: { value: 'done' },
    })
    fireEvent.change(screen.getByRole('slider', { name: 'Progress for Payment boundary' }), {
      target: { value: '65' },
    })

    expect(onMoveTrackable).toHaveBeenCalledWith(expect.objectContaining({ kind: 'entity' }), 'done')
    expect(onSetManualProgress).toHaveBeenCalledWith(expect.objectContaining({ kind: 'entity' }), 65)
  })

  it('supports drag and drop while retaining source and child-diagram navigation', () => {
    const onMoveTrackable = vi.fn()
    const onOpenSource = vi.fn()
    const onOpenChildDiagram = vi.fn()
    const { container } = renderBoard({ onMoveTrackable, onOpenSource, onOpenChildDiagram })
    const card = container.querySelector('[data-trackable-id="entity-payment"]') as HTMLElement
    const transfer = createDataTransfer()

    fireEvent.dragStart(card, { dataTransfer: transfer })
    fireEvent.drop(screen.getByRole('heading', { name: 'Done' }).closest('section')!, { dataTransfer: transfer })
    fireEvent.click(screen.getByRole('button', { name: /Payment boundary/i }))
    fireEvent.click(screen.getByRole('button', { name: 'Open child diagram' }))

    expect(onMoveTrackable).toHaveBeenCalledWith(expect.objectContaining({ kind: 'entity' }), 'done')
    expect(onOpenSource).toHaveBeenCalledWith(expect.objectContaining({ kind: 'entity' }))
    expect(onOpenChildDiagram).toHaveBeenCalledWith('diagram-payment')
  })
})

function renderBoard(overrides: Partial<Parameters<typeof KanbanBoard>[0]> = {}) {
  const props: Parameters<typeof KanbanBoard>[0] = {
    workspace: createWorkspace(),
    boardId: 'delivery',
    onMoveTrackable: vi.fn(),
    onSetManualProgress: vi.fn(),
    onOpenSource: vi.fn(),
    onOpenChildDiagram: vi.fn(),
    ...overrides,
  }
  return render(<KanbanBoard {...props} />)
}

function createWorkspace(): SemanticWorkspace {
  return {
    schemaVersion: 1,
    id: 'workspace-1',
    name: 'Checkout',
    revision: 3,
    updatedAtUtc: '2026-08-30T08:00:00Z',
    assets: [],
    relationships: [],
    boards: [{
      id: 'delivery',
      title: 'Delivery flow',
      metadata: {},
      columns: [
        { id: 'done', title: 'Done', order: 30, colorToken: '#168f84' },
        { id: 'ready', title: 'Ready', order: 10, colorToken: '#6f5ce7' },
        { id: 'doing', title: 'In progress', order: 20, colorToken: '#d7913b' },
      ],
    }],
    diagrams: [
      {
        id: 'diagram-plan', title: 'Architecture plan', purpose: 'Keep the boundary explicit.',
        parentDiagramId: null, metadata: {}, view: { zoom: null, viewportX: null, viewportY: null, entities: [] },
        tracking: { boardId: 'delivery', columnId: 'ready', progress: { mode: 'Manual', value: 10 }, metadata: {} },
      },
      {
        id: 'diagram-payment', title: 'Payment detail', purpose: null,
        parentDiagramId: 'diagram-plan', metadata: {}, view: { zoom: null, viewportX: null, viewportY: null, entities: [] },
        tracking: null,
      },
    ],
    entities: [{
      id: 'entity-payment', diagramId: 'diagram-plan', type: 'service', title: 'Payment boundary',
      description: 'Owns payment-provider orchestration.', parentEntityId: null, childDiagramId: 'diagram-payment',
      architectureNodeId: 'project:payment', blocks: [], tags: ['backend', 'critical'], metadata: {},
      tracking: { boardId: 'delivery', columnId: 'doing', progress: { mode: 'Manual', value: 40 }, metadata: {} },
    }],
  }
}

function createDataTransfer() {
  const data = new Map<string, string>()
  return {
    effectAllowed: 'all',
    dropEffect: 'none',
    setData: (type: string, value: string) => data.set(type, value),
    getData: (type: string) => data.get(type) ?? '',
  }
}
