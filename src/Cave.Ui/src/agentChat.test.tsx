import { cleanup, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mergeChatHistory, useAgentChat, type AgentChatMessage, type AgentChatResponse } from './agentChat'

function message(id: string, text: string, seconds: number): AgentChatMessage {
  return {
    id, text, role: 'Assistant', kind: 'Commentary', occurredAtUtc: new Date(seconds * 1000).toISOString(),
    isTruncated: false, artifacts: [],
  }
}

function response(periodId: string, ...messages: AgentChatMessage[]): AgentChatResponse {
  return {
    periodId, sharing: 'Shared', status: 'Available',
    agents: [{ id: 'task', parentId: null, displayName: 'Main agent', startedAtUtc: null, status: 'Available', messages }],
  }
}

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('agent chat history', () => {
  it('retains the previous rolling page and replaces an updated message in place', () => {
    const before = response('period-1', message('a', 'Older', 1), message('b', 'Partial', 2))
    const incoming = response('period-1', message('b', 'Published', 2), message('c', 'Newest', 3))
    expect(mergeChatHistory(before, incoming).agents[0].messages.map(({ text }) => text))
      .toEqual(['Older', 'Published', 'Newest'])
    expect(mergeChatHistory(before, { ...incoming, periodId: 'period-2' }).agents[0].messages.map(({ text }) => text))
      .toEqual(['Published', 'Newest'])
    expect(mergeChatHistory(before, { ...incoming, sharing: 'Private' }).agents).toEqual([])
  })

  it('never retains messages for an absent agent or an unavailable response', () => {
    const before = response('period-1', message('a', 'Older', 1))
    const incoming = response('period-1', message('b', 'Newer', 2))
    expect(mergeChatHistory(before, { ...incoming, status: 'Unavailable' }).agents[0].messages.map(({ id }) => id))
      .toEqual(['b'])
    expect(mergeChatHistory(before, { ...incoming, agents: [] }).agents).toEqual([])
  })
})

describe('agent chat polling', () => {
  it('stops the in-flight read and omits content when the observed sharing flag turns off', async () => {
    const fetcher = vi.fn<typeof fetch>(() => new Promise<Response>(() => {}))
    vi.stubGlobal('fetch', fetcher)
    const { result, rerender } = renderHook(({ shared }) =>
      useAgentChat('workspace-a', 'task-1', shared), { initialProps: { shared: true } })
    await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(1))
    const signal = fetcher.mock.calls[0][1]?.signal
    rerender({ shared: false })
    expect(signal?.aborted).toBe(true)
    expect(result.current.sharing).toBe('Private')
    expect(result.current.agents).toEqual([])
  })

  it('clears the previous task synchronously when the selection changes', async () => {
    const fetcher = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(Response.json(response('period-1', message('a', 'Old task', 1))))
      .mockImplementation(() => new Promise<Response>(() => {}))
    vi.stubGlobal('fetch', fetcher)
    const { result, rerender } = renderHook(({ taskId }) =>
      useAgentChat('workspace-a', taskId, true), { initialProps: { taskId: 'task-1' } })
    await waitFor(() => expect(result.current.agents).toHaveLength(1))
    rerender({ taskId: 'task-2' })
    expect(result.current.agents).toEqual([])
    expect(result.current.status).toBe('Loading')
    expect(fetcher.mock.calls[1][0]).toContain('task=task-2')
  })
})
