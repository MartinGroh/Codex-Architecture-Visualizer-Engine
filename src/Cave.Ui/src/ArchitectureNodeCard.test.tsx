import { cleanup, render, screen, within } from '@testing-library/react'
import type { NodeProps } from '@xyflow/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ArchitectureNodeCard } from './ArchitectureNodeCard'
import type { ArchitectureFlowNode } from './graphPresentation'

vi.mock('@xyflow/react', async (importOriginal) => ({
  ...await importOriginal<typeof import('@xyflow/react')>(),
  Handle: () => null,
}))

afterEach(cleanup)

describe('ArchitectureNodeCard activity history', () => {
  it('retains declared versus observed provenance after the agent finishes', () => {
    const props: NodeProps<ArchitectureFlowNode> = {
      id: 'project',
      type: 'architecture',
      dragging: false,
      zIndex: 0,
      selectable: true,
      deletable: false,
      draggable: true,
      isConnectable: false,
      positionAbsoluteX: 0,
      positionAbsoluteY: 0,
      selected: false,
      data: {
        architecture: {
          id: 'project', kind: 'Project', name: 'Project', parentId: null,
          qualifiedName: null, description: null, categoryId: null, tags: [], sourceLocations: [],
        },
        gitDelta: null,
        activities: [
          { agentId: 'main', nodeId: 'project', evidence: 'Declared', isDirect: true, updatedAtUtc: '2026-08-23T10:00:00Z', paths: ['declared.cs'] },
          { agentId: 'main', nodeId: 'project', evidence: 'Observed', isDirect: true, updatedAtUtc: '2026-08-23T10:01:00Z', paths: ['observed.cs'] },
        ],
        agentDecorations: [{
          state: 'Worked', evidence: 'Declared',
          agent: {
            agentId: 'main', agentType: 'main', isSubagent: false, state: 'Completed', phase: null,
            summary: null, hasObservedActivity: true, hasDeclaredScope: true,
            startedAtUtc: '2026-08-23T10:00:00Z', updatedAtUtc: '2026-08-23T10:02:00Z',
          },
        }],
        recentEdits: [],
        hierarchy: { canExpand: false, isExpanded: false, isNested: false, isDimmed: false },
      },
    }

    render(<ArchitectureNodeCard {...props} />)

    const history = screen.getByRole('tooltip')
    const declared = within(history).getByText('declared.cs').closest('li')!
    const observed = within(history).getByText('observed.cs').closest('li')!
    expect(declared.textContent).toBe('Declared declared.cs')
    expect(observed.textContent).toBe('Observed observed.cs')
  })
})
