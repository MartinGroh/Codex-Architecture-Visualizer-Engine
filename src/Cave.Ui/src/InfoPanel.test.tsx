// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { InfoPanel } from './InfoPanel'
import type { CaveInfoSnapshot } from './types'

describe('account usage information', () => {
  afterEach(cleanup)

  it('retains valid quota data when token activity is unavailable', () => {
    render(<InfoPanel info={{ viewerProtocolVersion: 2, usage: {
      status: 'Ready', summary: null, daily: [], ordinaryUsageAllowed: false,
      rateLimits: [{ limitId: 'codex', limitName: 'Codex', window: 'secondary', usedPercent: 100, windowDurationMinutes: 10080, resetsAtUtc: null }],
      retrievedAtUtc: '2026-09-27T12:00:00Z', error: 'Token activity could not be read.',
    } }} error={null} onClose={vi.fn()} />)
    expect(screen.getByText('100% used')).toBeTruthy()
    expect(screen.getByText('Token activity could not be read.')).toBeTruthy()
    expect(screen.getByText('Included Codex usage is currently blocked.')).toBeTruthy()
    expect(screen.queryByText(/Loading/)).toBeNull()
  })

  it('distinguishes unavailable account fields from reported zero usage', () => {
    const info: CaveInfoSnapshot = {
      viewerProtocolVersion: 1,
      usage: {
        status: 'Ready',
        summary: {
          lifetimeTokens: null, peakDailyTokens: 0, currentStreakDays: 0,
          longestStreakDays: null, longestRunningTurnSeconds: null,
        },
        daily: [], rateLimits: [], retrievedAtUtc: '2026-09-26T12:00:00Z', error: null,
      },
    }
    render(<InfoPanel info={info} error={null} onClose={vi.fn()} />)

    expect(screen.getAllByText('Unavailable')).toHaveLength(3)
    expect(screen.getByText('Peak day').nextElementSibling?.textContent).toBe('0')
    expect(screen.getByText('Current streak').nextElementSibling?.textContent).toBe('0 days')
  })
})
