import { describe, expect, it } from 'vitest'
import { agentDisplayName, agentFocusLabel } from './agentIdentity'
import type { AgentActivity } from './types'

describe('agentDisplayName', () => {
  it('uses the shared API name without adding surnames or identifiers', () => {
    expect(agentDisplayName({ isSubagent: true, displayName: 'Ada' })).toBe('Ada')
    expect(agentDisplayName({ isSubagent: true, displayName: 'Luna' })).toBe('Luna')
  })
  it('keeps unenriched activity clearly identified by role', () => {
    expect(agentDisplayName({ isSubagent: true })).toBe('Subagent')
    expect(agentDisplayName({ isSubagent: false, displayName: null })).toBe('Main agent')
  })
  it('does not infer summary provenance from a historical scope declaration', () => {
    const agent: AgentActivity = {
      agentId: 'child', isSubagent: true, agentType: 'default', state: 'Active', phase: 'Reading',
      summary: 'Check documentation', hasDeclaredScope: true, hasObservedActivity: true,
      startedAtUtc: '2026-09-27T12:00:00Z', updatedAtUtc: '2026-09-27T12:01:00Z',
    }
    expect(agentFocusLabel(agent)).toBe('Work summary')
    expect(agentFocusLabel({ ...agent, summaryEvidence: 'Observed' })).toBe('Observed action')
    expect(agentFocusLabel({ ...agent, summaryEvidence: 'Declared' })).toBe('Current focus / subgoal')
  })
})
