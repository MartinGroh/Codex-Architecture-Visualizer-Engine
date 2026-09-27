import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { AgentWorkContext } from './AgentWorkContext'
import { sharedGoal } from './goalPresentation'
import type { ConversationOverlay } from './types'

const conversation: ConversationOverlay = {
  sharingEnabled: true, status: 'Ready', messages: [], error: null,
  control: { sessionId: 'task-a', turnId: null, state: 'Ready', canSend: true, deliveries: [], error: null },
  goal: {
    status: 'Ready', sessionId: 'task-a', retrievedAtUtc: '2026-09-27T12:00:00Z', error: null,
    goal: { objective: 'Improve the activity view', status: 'Active', tokenBudget: 1000, tokensUsed: 0, timeUsedSeconds: 60, createdAt: 1, updatedAt: 2 },
  },
}
afterEach(cleanup)
describe('AgentWorkContext', () => {
  it('shows the exact shared goal and an independently declared subgoal', () => {
    render(<AgentWorkContext conversation={conversation} agents={[{
      agentId: 'review-child', agentType: 'default', isSubagent: true, state: 'Active', phase: 'Validating',
      summary: 'Verify mobile disclosure', hasDeclaredScope: true, hasObservedActivity: true,
      summaryEvidence: 'Declared', startedAtUtc: '2026-09-27T12:00:00Z', updatedAtUtc: '2026-09-27T12:01:00Z',
    }]} />)
    expect(screen.getAllByText('Improve the activity view').length).toBeGreaterThan(0)
    expect(screen.getByText('Current focus / subgoal: Verify mobile disclosure')).toBeTruthy()
    expect(screen.getByText('0 tokens used of 1,000 · 1m elapsed')).toBeTruthy()
    expect(screen.queryByText('default')).toBeNull()
  })
  it('removes the goal immediately when sharing is disabled or the task changes', () => {
    const view = render(<AgentWorkContext conversation={conversation} agents={[]} />)
    view.rerender(<AgentWorkContext conversation={{ ...conversation, sharingEnabled: false }} agents={[]} />)
    expect(screen.queryByText('Improve the activity view')).toBeNull()
    expect(sharedGoal({ ...conversation, control: { ...conversation.control, sessionId: 'task-b' } })).toBeNull()
  })
  it('distinguishes a task without a goal from a failed read', () => {
    const view = render(<AgentWorkContext conversation={{ ...conversation, goal: { ...conversation.goal!, goal: null } }} agents={[]} />)
    expect(screen.getByText('No main goal is set for this task.')).toBeTruthy()
    view.rerender(<AgentWorkContext conversation={{ ...conversation, goal: { ...conversation.goal!, status: 'Unavailable', goal: null, error: 'Goal read timed out.' } }} agents={[]} />)
    expect(screen.getByText('Goal read timed out.')).toBeTruthy()
  })
})
