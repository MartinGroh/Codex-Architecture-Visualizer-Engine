// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { UsageSpotlight } from './UsageSpotlight'
import { createUsagePresentation } from './usagePresentation'

describe('usage spotlight', () => {
  afterEach(cleanup)
  it('shows remaining quota and an elapsed-window burn rate', () => {
    const result = createUsagePresentation([{
      limitId: 'codex', limitName: 'Codex', window: 'primary', usedPercent: 30,
      windowDurationMinutes: 10_080, resetsAtUtc: '2026-08-27T00:00:00Z',
    }], new Date('2026-08-23T12:00:00Z'))
    expect(result?.remainingPercent).toBe(70)
    expect(result?.windowLabel).toBe('1 week window')
    expect(result?.burnLabel).toBe('8.6%/day')
  })
  it('opens detailed usage from the front-and-center summary', () => {
    const onOpen = vi.fn()
    render(<UsageSpotlight info={null} error={null} onOpen={onOpen} />)
    screen.getByRole('button', { name: /usage unavailable/i }).click()
    expect(onOpen).toHaveBeenCalledOnce()
  })
})
