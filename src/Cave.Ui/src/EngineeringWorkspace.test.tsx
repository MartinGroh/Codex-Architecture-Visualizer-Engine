import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { EngineeringWorkspace, type EngineeringWorkspaceClient } from './EngineeringWorkspace'
import type { SemanticWorkspace, SemanticWorkspaceBatch } from './semanticWorkspaceTypes'

afterEach(cleanup)

describe('EngineeringWorkspace', () => {
  it('creates a starter diagram and configurable board as one canonical batch', async () => {
    const empty = createEmptyWorkspace()
    const client = createClient(empty)
    client.applySemanticBatch = vi.fn().mockImplementation(async (batch: SemanticWorkspaceBatch) => {
      const board = batch.operations.find((operation) => operation.operation === 'create-board')
      const diagram = batch.operations.find((operation) => operation.operation === 'create-diagram')
      if (board?.operation !== 'create-board' || diagram?.operation !== 'create-diagram') throw new Error('Unexpected starter batch')
      return {
        succeeded: true,
        workspace: { ...empty, revision: 1, boards: [board.board], diagrams: [diagram.diagram] },
        createdIds: [board.board.id, diagram.diagram.id],
        errors: [],
        transaction: null,
      }
    })

    render(<EngineeringWorkspace client={client} workspaceName="CAVE" />)
    await screen.findByRole('button', { name: /Create starter workspace/i })
    fireEvent.click(screen.getByRole('button', { name: /Create starter workspace/i }))

    await waitFor(() => expect(client.applySemanticBatch).toHaveBeenCalledOnce())
    const batch = vi.mocked(client.applySemanticBatch).mock.calls[0][0]
    expect(batch.operations.map((operation) => operation.operation)).toEqual(['create-board', 'create-diagram'])
    expect(batch.actor.type).toBe('Human')
    expect(await screen.findByRole('heading', { name: 'System design' })).toBeTruthy()
  })

  it('keeps diagram and Kanban as switchable projections of the same workspace', async () => {
    const workspace = createEmptyWorkspace()
    workspace.diagrams = [{
      id: 'diagram-1', title: 'System design', purpose: null, parentDiagramId: null,
      tracking: null, metadata: {}, view: { zoom: null, viewportX: null, viewportY: null, entities: [] },
    }]
    workspace.boards = [{
      id: 'board-1', title: 'Development', metadata: {},
      columns: [{ id: 'planned', title: 'Planned', order: 0, colorToken: null }],
    }]

    render(<EngineeringWorkspace client={createClient(workspace)} workspaceName="CAVE" />)
    await screen.findByRole('heading', { name: 'System design' })
    fireEvent.click(screen.getByRole('tab', { name: /Kanban/i }))

    expect(await screen.findByRole('heading', { name: 'Planned' })).toBeTruthy()
    expect((screen.getByRole('combobox', { name: /Board/i }) as HTMLSelectElement).value).toBe('board-1')
  })
})

function createClient(workspace: SemanticWorkspace): EngineeringWorkspaceClient {
  return {
    readSemanticWorkspace: vi.fn().mockResolvedValue(workspace),
    applySemanticBatch: vi.fn(),
    undoSemanticWorkspace: vi.fn(),
    redoSemanticWorkspace: vi.fn(),
    exportSemanticWorkspace: vi.fn(),
  }
}

function createEmptyWorkspace(): SemanticWorkspace {
  return {
    schemaVersion: 1,
    id: 'workspace-1',
    name: 'CAVE',
    revision: 0,
    updatedAtUtc: '2026-08-30T08:00:00Z',
    diagrams: [],
    entities: [],
    relationships: [],
    boards: [],
    assets: [],
  }
}
