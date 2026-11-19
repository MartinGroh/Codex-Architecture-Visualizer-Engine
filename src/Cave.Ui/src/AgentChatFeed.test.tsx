import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { AgentChatFeed } from './AgentChatFeed'
import type { AgentChatAgent, AgentChatMessage, AgentChatSession } from './agentChat'

afterEach(cleanup)

const session: AgentChatSession = {
  sharing: 'Shared', status: 'Available', periodId: 'period', agents: [],
  artifactUrl: (id) => '/api/agent-chat/artifacts?artifact=' + id, enableSharing: vi.fn(),
}

function item(id: string, text: string): AgentChatMessage {
  return {
    id, text, kind: 'Commentary', role: 'Assistant', occurredAtUtc: '2026-11-01T09:00:00Z',
    isTruncated: false, artifacts: [],
  }
}

function agent(name: string, ...messages: AgentChatMessage[]): AgentChatAgent {
  return { id: name, displayName: name, parentId: 'main', status: 'Available', startedAtUtc: null, messages }
}

it('pauses only the pane being read, then resumes following when asked', () => {
  const beforeLeft = agent('Kai', item('a', 'Older note'))
  const beforeRight = agent('Rowan', item('d', 'Separate note'))
  const view = (left: AgentChatAgent, right: AgentChatAgent) => <>
    <AgentChatFeed agent={left} chat={session} />
    <AgentChatFeed agent={right} chat={session} />
  </>
  const { rerender } = render(view(beforeLeft, beforeRight))
  const left = screen.getByRole('region', { name: 'Kai conversation and activity' })
  const right = screen.getByRole('region', { name: 'Rowan conversation and activity' })
  for (const pane of [left, right]) {
    Object.defineProperties(pane, { clientHeight: { value: 100 }, scrollHeight: { value: 500 } })
  }
  left.scrollTop = 30
  fireEvent.scroll(left)
  right.scrollTop = 400
  fireEvent.scroll(right)

  rerender(view(agent('Kai', ...beforeLeft.messages, item('b', 'New left note')),
    agent('Rowan', ...beforeRight.messages, item('e', 'New right note'))))
  expect(left.scrollTop).toBe(30)
  expect(right.scrollTop).toBe(500)
  const jump = screen.getByRole('button', { name: 'Jump to latest' })
  fireEvent.click(jump)
  expect(left.scrollTop).toBe(500)
  expect(screen.queryByRole('button', { name: 'Jump to latest' })).toBeNull()
})

it('removes the previous period content and previews when sharing is revoked', () => {
  const past = agent('Kai', item('a', 'Old shared content'))
  const { rerender } = render(<AgentChatFeed agent={past} chat={session} />)
  expect(screen.getByText('Old shared content')).toBeTruthy()
  rerender(<AgentChatFeed agent={past} chat={{ ...session, sharing: 'Private', periodId: null }} />)
  expect(screen.queryByText('Old shared content')).toBeNull()
  expect(screen.getByText('Conversation sharing is off.')).toBeTruthy()
})
