import { describe, expect, it } from 'vitest'
import { createUsagePresentation } from './usagePresentation'
import type { CodexRateLimitWindow } from './types'

const now = new Date('2026-09-27T12:00:00Z')
const primary: CodexRateLimitWindow = {
  limitId: 'codex', limitName: 'Codex', window: 'primary', usedPercent: 10,
  windowDurationMinutes: 300, resetsAtUtc: null,
}

describe('quota window presentation', () => {
  it('shows the constraining secondary window with its actual identity', () => {
    const secondary = { ...primary, window: 'secondary', usedPercent: 100, windowDurationMinutes: 10_080 }
    const presentation = createUsagePresentation([primary, secondary], now)
    expect(presentation?.remainingPercent).toBe(0)
    expect(presentation?.limitLabel).toBe('Codex secondary')
    expect(presentation?.windowLabel).toBe('1 week window')
  })
  it('does not substitute an unrelated metered bucket for Codex', () => {
    expect(createUsagePresentation([{ ...primary, limitId: 'code-review' }], now)).toBeNull()
  })
  it('preserves genuine zero and unavailable duration/reset/permission', () => {
    const presentation = createUsagePresentation([{ ...primary, usedPercent: 0, windowDurationMinutes: null }], now)
    expect(presentation?.remainingPercent).toBe(100)
    expect(presentation?.burnLabel).toBe('0% used')
    expect(presentation?.resetsLabel).toBeNull()
    expect(presentation?.ordinaryUsageAllowed).toBeNull()
  })
  it('never infers backend permission from remaining quota or an expired reset', () => {
    const window = { ...primary, usedPercent: 0, resetsAtUtc: '2026-09-27T10:00:00Z' }
    expect(createUsagePresentation([window], now, false)?.ordinaryUsageAllowed).toBe(false)
    expect(createUsagePresentation([window], now, null)?.ordinaryUsageAllowed).toBeNull()
  })
})
