// @vitest-environment jsdom
import { act, cleanup, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readCaveInfo } from './api'
import { MachinePageFrame } from './MachinePageFrame'
import type { CaveInfoSnapshot } from './types'

vi.mock('./api', () => ({ readCaveInfo: vi.fn() }))

beforeEach(() => {
  vi.useFakeTimers()
  vi.mocked(readCaveInfo).mockReset()
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })))
})
afterEach(() => { cleanup(); vi.useRealTimers(); vi.unstubAllGlobals() })

const ready: CaveInfoSnapshot = {
  viewerProtocolVersion: 2,
  usage: { status: 'Ready', summary: null, daily: [], rateLimits: [{ limitId: 'codex', limitName: null, window: 'primary', usedPercent: 25, windowDurationMinutes: null, resetsAtUtc: null }], retrievedAtUtc: '2026-09-27T12:00:00Z', error: null },
}

describe('machine account usage refresh', () => {
  it('recovers an initial unavailable snapshot and refreshes quota every 60 seconds', async () => {
    vi.mocked(readCaveInfo)
      .mockResolvedValueOnce({ ...ready, usage: { ...ready.usage, status: 'Unavailable', rateLimits: [], error: 'Account unavailable.' } })
      .mockResolvedValueOnce(ready)
      .mockResolvedValueOnce({ ...ready, usage: { ...ready.usage, rateLimits: [{ ...ready.usage.rateLimits[0], usedPercent: 50 }] } })
    await act(async () => { render(<MachinePageFrame className="test"><div>Content</div></MachinePageFrame>) })
    expect(screen.getByText('Quota unavailable')).toBeTruthy()
    await act(async () => { await vi.advanceTimersByTimeAsync(59_999) })
    expect(readCaveInfo).toHaveBeenCalledTimes(1)
    await act(async () => { await vi.advanceTimersByTimeAsync(1) })
    expect(screen.getByText('75% left')).toBeTruthy()
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000) })
    expect(screen.getByText('50% left')).toBeTruthy()
    expect(readCaveInfo).toHaveBeenCalledTimes(3)
  })
  it('prevents overlapping reads and aborts the request and timer when removed', async () => {
    let resolve: ((info: CaveInfoSnapshot) => void) | undefined
    vi.mocked(readCaveInfo).mockImplementation(() => new Promise((done) => { resolve = done }))
    const { unmount } = render(<MachinePageFrame className="test"><div>Content</div></MachinePageFrame>)
    const signal = vi.mocked(readCaveInfo).mock.calls[0][0]
    await act(async () => { await vi.advanceTimersByTimeAsync(180_000) })
    expect(readCaveInfo).toHaveBeenCalledTimes(1)
    unmount()
    expect(signal?.aborted).toBe(true)
    await act(async () => { resolve?.(ready); await vi.advanceTimersByTimeAsync(60_000) })
    expect(readCaveInfo).toHaveBeenCalledTimes(1)
    expect(vi.getTimerCount()).toBe(0)
  })
  it('clears a transport error after a subsequent successful refresh', async () => {
    vi.mocked(readCaveInfo).mockRejectedValueOnce(new Error('Connection failed.')).mockResolvedValueOnce(ready)
    await act(async () => { render(<MachinePageFrame className="test"><div>Content</div></MachinePageFrame>) })
    expect(screen.getByRole('button', { name: /usage unavailable/i }).title).toBe('Connection failed.')
    await act(async () => { await vi.advanceTimersByTimeAsync(60_000) })
    expect(screen.getByText('75% left')).toBeTruthy()
    expect(screen.getByRole('button', { name: /quota remaining/i }).title).toBe('')
  })
})
