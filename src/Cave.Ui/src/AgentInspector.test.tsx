import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { AgentInspector } from './AgentInspector'
import type { AgentChatAgent, AgentChatSession } from './agentChat'
import type { MachineActivityLane } from './machineActivityTimeline'

const timestamp = '2026-11-19T13:00:00Z'
const lane: MachineActivityLane = {
  agent: {
    agentId: 'native-review', publicId: 'review', displayName: 'Kai', agentType: 'Reviewer', isSubagent: true,
    state: 'Active', phase: 'Validating', summary: 'Review contracts', summaryEvidence: 'Declared',
    lastObservedActivity: 'Ran checks', hasObservedActivity: true, hasDeclaredScope: true,
    startedAtUtc: timestamp, updatedAtUtc: timestamp,
  },
  active: true, durationMs: 0, durationPercent: 0, completionSummary: null,
  milestones: [{ id: 'edit', label: 'Endpoints.cs', detail: 'Observed edit', kind: 'Edit', observedAtUtc: timestamp, positionPercent: 50 }],
}
function agent(id: string): AgentChatAgent {
  return { id, parentId: 'main', displayName: 'Kai', startedAtUtc: timestamp, status: 'Available',
    messages: [{ id: 'source', kind: 'Final', role: 'Assistant', occurredAtUtc: timestamp,
      text: id + ' result', isTruncated: false, artifacts: [{ id: id + '-image', contentType: 'image/png' }] }] }
}
const chat: AgentChatSession = {
  sharing: 'Shared', status: 'Available', periodId: 'one', agents: [agent('review'), agent('build')],
  artifactUrl: (id) => '/api/agent-chat/artifacts?artifact=' + id, enableSharing: vi.fn(),
}
const props = {
  lane, chat, workspaceId: 'workspace', taskId: 'main', sharingEnabled: true, operation: 'Validating', width: null,
  agentOptions: [], onSelectAgent: vi.fn(), onResize: vi.fn(), onClose: vi.fn(), onFocusBranch: vi.fn(), onOpenWall: vi.fn(),
}

beforeEach(() => {
  // jsdom exposes the element but has no top layer. Real focus trapping is exercised in a browser.
  Object.assign(HTMLDialogElement.prototype, {
    show(this: HTMLDialogElement) { this.open = true },
    showModal(this: HTMLDialogElement) { this.open = true },
    close(this: HTMLDialogElement) { this.open = false },
  })
  vi.stubGlobal('ResizeObserver', class { observe() {} disconnect() {} })
  vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
})
afterEach(() => { cleanup(); vi.unstubAllGlobals(); vi.clearAllMocks() })

it('keeps outputs scoped across same-name agents and returns to the exact message', () => {
  const { rerender } = render(<AgentInspector {...props} />)
  expect(screen.getByText('review result')).toBeTruthy()
  expect(screen.queryByText('build result')).toBeNull()
  expect(screen.queryByText('Endpoints.cs')).toBeNull()
  fireEvent.keyDown(screen.getByRole('tab', { name: 'Conversation' }), { key: 'ArrowRight' })
  const gallery = () => screen.getByRole('tabpanel', { name: /Outputs/ })
  expect(within(gallery()).getByText('review result')).toBeTruthy()

  rerender(<AgentInspector {...props} lane={{ ...lane, agent: { ...lane.agent, agentId: 'native-build', publicId: 'build' } }} />)
  expect(screen.queryByText('review result')).toBeNull()
  expect(within(gallery()).getByText('build result')).toBeTruthy()
  fireEvent.click(within(gallery()).getByRole('button', { name: 'View message' }))
  expect(document.activeElement).toBe(screen.getByText('build result').closest('li'))
  expect(screen.getByRole('tab', { name: 'Conversation' }).getAttribute('aria-selected')).toBe('true')
})

it('clears open previews on revocation and still exposes independently observed activity', () => {
  const { rerender } = render(<AgentInspector {...props} />)
  fireEvent.click(screen.getByRole('tab', { name: /Outputs/ }))
  fireEvent.click(screen.getByRole('button', { name: 'Open shared image preview' }))
  expect(screen.getByRole('dialog', { name: 'Shared image' })).toBeTruthy()
  rerender(<AgentInspector {...props} sharingEnabled={false} chat={{ ...chat, sharing: 'Private', periodId: null, status: 'Idle' }} />)
  expect(screen.queryByRole('dialog', { name: 'Shared image' })).toBeNull()
  expect(document.querySelector('img')).toBeNull()
  expect(screen.queryByText('review result')).toBeNull()
  expect(within(screen.getByRole('tabpanel', { name: 'Outputs' })).getByRole('status').textContent).toBe('Conversation sharing is off.')
  fireEvent.click(screen.getByRole('tab', { name: 'Activity' }))
  expect(screen.getByText('Review contracts').previousElementSibling?.textContent).toBe('Current focus / subgoal')
  expect(screen.getByText('Ran checks').previousElementSibling?.textContent).toBe('Last observed action')
  expect(screen.getByText('Endpoints.cs')).toBeTruthy()
})
