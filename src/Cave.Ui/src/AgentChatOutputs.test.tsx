import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { AgentChatOutputs } from './AgentChatOutputs'
import { useAgentChatOutputs } from './agentChatOutputIndex'
import type { AgentChatMessage } from './agentChat'
import { renderAgentChatDiagram } from './agentChatDiagram'

vi.mock('./agentChatDiagram', () => ({ renderAgentChatDiagram: vi.fn(async () => null) }))
afterEach(() => { cleanup(); vi.clearAllMocks() })

function message(id: string, text: string, time: string, artifacts: AgentChatMessage['artifacts'] = []): AgentChatMessage {
  return { id, text, artifacts, occurredAtUtc: time, kind: 'Final', role: 'Assistant', isTruncated: false }
}

const artifactUrl = vi.fn((id: string) => '/api/agent-chat/artifacts?artifact=' + id)
const openMessage = vi.fn()
function Gallery({ messages }: { messages?: AgentChatMessage[] }) {
  const outputs = useAgentChatOutputs(messages)
  return <AgentChatOutputs outputs={outputs} artifactUrl={artifactUrl} onOpenMessage={openMessage} />
}

it('indexes Markdown diagrams and scoped attachments in message order and opens the exact source', async () => {
  const diagram = message('first', [
    '> **Build plan**', '>', '> ~~~mermaid', '> flowchart LR', '> A --> B', '> ~~~',
    '', '````text', '```mermaid', 'not a diagram', '```', '````',
    '', '![Remote diagram](https://example.invalid/diagram.svg)',
  ].join('\n'), '2026-11-01T09:00:00Z')
  const photo = message('second', 'A **new** screenshot.', '2026-11-01T09:01:00Z', [
    { id: 'published', contentType: 'image/png' }, { id: 'file-only', contentType: null },
  ])
  const { rerender } = render(<Gallery messages={[diagram, photo]} />)
  const items = screen.getAllByRole('listitem')
  expect(items).toHaveLength(3)
  expect(within(items[0]).getByText('A new screenshot.')).toBeTruthy()
  expect(within(items[1]).getByText('Image preview unavailable')).toBeTruthy()
  expect(within(items[2]).getByText('Build plan')).toBeTruthy()
  expect(await within(items[2]).findByText('Diagram preview unavailable')).toBeTruthy()
  expect(renderAgentChatDiagram).toHaveBeenCalledTimes(1)
  expect(renderAgentChatDiagram).toHaveBeenCalledWith(expect.any(String), 'flowchart LR\nA --> B', expect.any(AbortSignal), expect.any(Object))
  expect(artifactUrl).toHaveBeenCalledWith('published')
  expect(artifactUrl).not.toHaveBeenCalledWith('file-only')
  expect(document.querySelector('img[src*="example.invalid"]')).toBeNull()
  fireEvent.click(within(items[2]).getByRole('button', { name: 'View message' }))
  expect(openMessage).toHaveBeenCalledWith('first')

  rerender(<Gallery messages={[{ ...diagram, text: 'Removed in the current message' }]} />)
  expect(screen.queryByRole('listitem')).toBeNull()
  expect(screen.queryByRole('button', { name: /preview/i })).toBeNull()
  rerender(<Gallery />)
  expect(screen.queryByText('Build plan')).toBeNull()
})
