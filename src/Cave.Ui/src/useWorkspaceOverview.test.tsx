import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readWorkspaceOverview } from './api'
import type { WorkspaceOverviewSnapshot } from './types'
import { useWorkspaceOverview } from './useWorkspaceOverview'

vi.mock('./api', () => ({ readWorkspaceOverview: vi.fn() }))
const snapshot = (activeAgentCount = 0): WorkspaceOverviewSnapshot => ({
  hostName: 'lab', errors: [], workspaces: [{
    workspaceId: 'one', name: 'Project', rootPath: 'C:/Project', isAvailable: true,
    lastSeenAtUtc: '', knownAgentCount: 1, activeAgentCount, activeSubagentCount: 0,
    currentPhase: null, activitySummary: null, lastActivityAtUtc: null, activityError: null,
  }],
})
const advance = async (ms: number) => { await act(async () => { await vi.advanceTimersByTimeAsync(ms) }) }
beforeEach(() => {
  vi.useFakeTimers()
  vi.mocked(readWorkspaceOverview).mockReset().mockResolvedValue(snapshot())
  Object.defineProperty(document, 'hidden', { configurable: true, value: false })
})
afterEach(() => { cleanup(); vi.useRealTimers() })

describe('activity-aware workspace polling', () => {
  it('reduces idle polls then returns to fast polling when work appears', async () => {
    renderHook(() => useWorkspaceOverview())
    await advance(0)
    await advance(14_999)
    expect(readWorkspaceOverview).toHaveBeenCalledTimes(1)
    vi.mocked(readWorkspaceOverview).mockResolvedValue(snapshot(1))
    await advance(1)
    expect(readWorkspaceOverview).toHaveBeenCalledTimes(2)
    await advance(2_000)
    expect(readWorkspaceOverview).toHaveBeenCalledTimes(3)
  })
  it('backs off while hidden and wakes immediately on visibility', async () => {
    Object.defineProperty(document, 'hidden', { configurable: true, value: true })
    renderHook(() => useWorkspaceOverview())
    await advance(0)
    await advance(59_999)
    expect(readWorkspaceOverview).toHaveBeenCalledTimes(1)
    Object.defineProperty(document, 'hidden', { configurable: true, value: false })
    await act(async () => { document.dispatchEvent(new Event('visibilitychange')) })
    expect(readWorkspaceOverview).toHaveBeenCalledTimes(2)
  })
  it('never overlaps requests even with repeated focus and manual refresh', async () => {
    let resolve!: (value: WorkspaceOverviewSnapshot) => void
    vi.mocked(readWorkspaceOverview).mockReturnValue(new Promise((done) => { resolve = done }))
    const hook = renderHook(() => useWorkspaceOverview())
    await act(async () => {
      window.dispatchEvent(new Event('focus'))
      window.dispatchEvent(new Event('focus'))
      void hook.result.current.refresh()
    })
    await advance(60_000)
    expect(readWorkspaceOverview).toHaveBeenCalledTimes(1)
    await act(async () => { resolve(snapshot()) })
    vi.mocked(readWorkspaceOverview).mockResolvedValue(snapshot())
    await advance(0)
    expect(readWorkspaceOverview).toHaveBeenCalledTimes(2)
    hook.unmount()
    await advance(60_000)
    expect(readWorkspaceOverview).toHaveBeenCalledTimes(2)
  })
  it('retries failures and does not publish an aborted response', async () => {
    vi.mocked(readWorkspaceOverview).mockRejectedValueOnce(new Error('offline'))
    const hook = renderHook(() => useWorkspaceOverview())
    await advance(0)
    expect(hook.result.current.error).toBe('offline')
    await advance(15_000)
    expect(hook.result.current.error).toBeNull()
    const signal = vi.mocked(readWorkspaceOverview).mock.calls.at(-1)?.[0]
    hook.unmount()
    expect(signal?.aborted).toBe(true)
  })
})
