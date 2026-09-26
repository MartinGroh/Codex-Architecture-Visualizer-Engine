import { describe, expect, it } from 'vitest'
import type { ArchitectureSnapshot } from './types'
import {
  applyWorkIndicatorCutoff,
  reconcileWorkIndicatorState,
  setWorkIndicatorResetMode,
  type WorkIndicatorContext,
  type WorkIndicatorState,
} from './workIndicators'

const context: WorkIndicatorContext = {
  gitIdentity: 'main\u0000head-1',
  instructionId: 'session:turn-1',
  instructionAtUtc: '2026-08-17T12:00:00Z',
  observedAtUtc: '2026-08-17T12:00:01Z',
}

function state(mode: WorkIndicatorState['mode']): WorkIndicatorState {
  return {
    mode,
    resetAtUtc: null,
    lastGitIdentity: context.gitIdentity,
    lastInstructionId: context.instructionId,
  }
}

describe('work indicator resets', () => {
  it('resets at the new instruction boundary', () => {
    const next = reconcileWorkIndicatorState(state('NewInstruction'), {
      ...context,
      instructionId: 'session:turn-2',
      instructionAtUtc: '2026-08-17T12:05:00Z',
    })

    expect(next.resetAtUtc).toBe('2026-08-17T12:05:00Z')
    expect(next.lastInstructionId).toBe('session:turn-2')
  })

  it('resets when either the branch or HEAD changes', () => {
    const next = reconcileWorkIndicatorState(state('GitChange'), {
      ...context,
      gitIdentity: 'feature/reset\u0000head-2',
    })

    expect(next.resetAtUtc).toBe(context.observedAtUtc)
    expect(next.lastGitIdentity).toBe('feature/reset\u0000head-2')
  })

  it('never auto-resets when the preference is Never', () => {
    const current = state('Never')
    const next = reconcileWorkIndicatorState(current, {
      ...context,
      gitIdentity: 'feature/reset\u0000head-2',
      instructionId: 'session:turn-2',
    })

    expect(next.resetAtUtc).toBeNull()
    expect(next.lastGitIdentity).toBe('feature/reset\u0000head-2')
    expect(next.lastInstructionId).toBe('session:turn-2')
  })

  it('does not move a manual cutoff backwards when the preference changes', () => {
    const current = {
      ...state('Never'),
      resetAtUtc: '2026-08-17T12:03:00Z',
    }

    const next = setWorkIndicatorResetMode(current, 'NewInstruction', context)

    expect(next.resetAtUtc).toBe('2026-08-17T12:03:00Z')
  })

  it('removes node evidence from before the reset while preserving newer evidence', () => {
    const snapshot = {
      activity: {
        agents: [],
        nodes: [
          { agentId: 'old', nodeId: 'old-node', evidence: 'Observed', isDirect: true, updatedAtUtc: '2026-08-17T11:59:59Z', paths: [] },
          { agentId: 'new', nodeId: 'new-node', evidence: 'Observed', isDirect: true, updatedAtUtc: '2026-08-17T12:00:01Z', paths: [] },
        ],
      },
    } as unknown as ArchitectureSnapshot

    const filtered = applyWorkIndicatorCutoff(snapshot, '2026-08-17T12:00:00Z')

    expect(filtered.activity.nodes.map((activity) => activity.nodeId)).toEqual(['new-node'])
    expect(snapshot.activity.nodes).toHaveLength(2)
  })

  it('hides a pre-reset location even when the agent is still active', () => {
    const snapshot = {
      activity: {
        agents: [{ agentId: 'active', state: 'Active' }],
        nodes: [
          { agentId: 'active', nodeId: 'active-node', evidence: 'Observed', isDirect: true, updatedAtUtc: '2026-08-17T11:59:59Z', paths: [] },
          { agentId: 'worked', nodeId: 'worked-node', evidence: 'Observed', isDirect: true, updatedAtUtc: '2026-08-17T11:59:59Z', paths: [] },
        ],
      },
    } as unknown as ArchitectureSnapshot

    const filtered = applyWorkIndicatorCutoff(snapshot, '2026-08-17T12:00:00Z')

    expect(filtered.activity.nodes).toEqual([])
  })
})
