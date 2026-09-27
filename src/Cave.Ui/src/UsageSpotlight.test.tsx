// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { UsageSpotlight } from './UsageSpotlight'
import { createUsagePresentation } from './usagePresentation'
import type { CaveInfoSnapshot } from './types'

describe('usage spotlight', () => {
  afterEach(cleanup)
  it('shows remaining quota and an elapsed-window burn rate', () => {
    const result = createUsagePresentation([{
      limitId: 'codex', limitName: 'Codex', window: 'primary', usedPercent: 30,
      windowDurationMinutes: 10_080, resetsAtUtc: '2026-08-27T00:00:00Z',
    }], new Date('2026-08-23T12:00:00Z'))
    expect(result?.remainingPercent).toBe(70)
    expect(result?.windowLabel).toBe('1 week window')
    expect(result?.burnLabel).toBe('Avg 8.6%/day')
  })
  it('opens detailed usage from the front-and-center summary', () => {
    const onOpen = vi.fn()
    render(<UsageSpotlight info={null} error={null} onOpen={onOpen} />)
    screen.getByRole('button', { name: /usage unavailable/i }).click()
    expect(onOpen).toHaveBeenCalledOnce()
  })
  it('distinguishes initial loading from a completed unavailable response', () => {
    const { rerender } = render(<UsageSpotlight info={null} error={null} onOpen={vi.fn()} />)
    expect(screen.getByText('Loading…')).toBeTruthy()
    rerender(<UsageSpotlight info={makeInfo([], 'Unavailable', 'Account read failed.')} error={null} onOpen={vi.fn()} />)
    expect(screen.getByText('Quota unavailable')).toBeTruthy()
    expect(screen.queryByText('Loading…')).toBeNull()
    expect(screen.getByRole('button').title).toBe('Account read failed.')
  })
  it('keeps available quotas visible alongside a partial token-activity diagnostic', () => {
    render(<UsageSpotlight info={makeInfo([{ limitId: 'codex', limitName: null, window: 'primary', usedPercent: 0, windowDurationMinutes: null, resetsAtUtc: null }], 'Ready', 'Token activity unavailable.')} error={null} onOpen={vi.fn()} />)
    expect(screen.getByText('100% left')).toBeTruthy()
    expect(screen.getByText('0% used')).toBeTruthy()
    expect(screen.getByRole('button').title).toBe('Token activity unavailable.')
  })
  it('honors backend included-usage blocking despite an unused quota window', () => {
    const info = makeInfo([{ limitId: 'codex', limitName: 'Codex', window: 'secondary', usedPercent: 0, windowDurationMinutes: 10_080, resetsAtUtc: null }])
    info.usage.ordinaryUsageAllowed = false
    render(<UsageSpotlight info={info} error={null} onOpen={vi.fn()} />)
    expect(screen.getByRole('button', { name: /included usage blocked/i })).toBeTruthy()
    expect(screen.getByText('Included usage blocked')).toBeTruthy()
    expect(screen.queryByText('100% left')).toBeNull()
    expect(screen.getByText('Codex secondary · 1 week window')).toBeTruthy()
  })
  it('shows reported included-usage blocking even when no quota window is available', () => {
    const info = makeInfo([])
    info.usage.ordinaryUsageAllowed = false
    render(<UsageSpotlight info={info} error={null} onOpen={vi.fn()} />)
    expect(screen.getByRole('button', { name: /included usage blocked/i })).toBeTruthy()
    expect(screen.getByText('Quota unavailable')).toBeTruthy()
  })
})

function makeInfo(rateLimits: CaveInfoSnapshot['usage']['rateLimits'], status: 'Ready' | 'Unavailable' = 'Ready', error: string | null = null): CaveInfoSnapshot {
  return { viewerProtocolVersion: 2, usage: { status, summary: null, daily: [], rateLimits, retrievedAtUtc: '2026-09-27T12:00:00Z', error } }
}
