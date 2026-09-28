import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readCaveInfo, readWorkspaceOverview } from './api'
import { WorkspaceDashboard } from './WorkspaceDashboard'

vi.mock('./api', () => ({
  readCaveInfo: vi.fn(),
  readWorkspaceOverview: vi.fn(),
}))

beforeEach(() => {
  vi.mocked(readCaveInfo).mockResolvedValue({
    viewerProtocolVersion: 1,
    usage: {
      status: 'Ready',
      summary: {
        lifetimeTokens: 1_000,
        peakDailyTokens: 500,
        longestRunningTurnSeconds: 120,
        currentStreakDays: 2,
        longestStreakDays: 4,
      },
      daily: [],
      rateLimits: [{
        limitId: 'codex',
        limitName: 'Codex',
        window: 'primary',
        usedPercent: 30,
        windowDurationMinutes: 10_080,
        resetsAtUtc: '2026-08-27T00:00:00Z',
      }],
      retrievedAtUtc: '2026-08-23T12:00:00Z',
      error: null,
    },
  })
  vi.mocked(readWorkspaceOverview).mockResolvedValue({
    hostName: 'NORWEGIANWOOD',
    workspaces: [{
      workspaceId: 'workspace-1',
      name: 'Architecture Engine',
      rootPath: 'C:\\Code\\Architecture Engine',
      isAvailable: true,
      lastSeenAtUtc: '2026-08-17T12:00:00Z',
      knownAgentCount: 2,
      activeAgentCount: 1,
      activeSubagentCount: 1,
      currentPhase: 'Editing',
      activitySummary: 'Editing the workspace',
      lastActivityAtUtc: '2026-08-17T12:00:00Z',
      activityError: null,
    }],
    errors: [],
  })
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({
    matches: false,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }))
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('WorkspaceDashboard', () => {
  it('links an available project to its scoped architecture view and shows live activity', async () => {
    render(<WorkspaceDashboard />)

    const project = await screen.findByRole('link', { name: /Architecture Engine/i })
    expect(project.getAttribute('href')).toBe('/?workspace=workspace-1')
    expect(screen.getByText('Editing')).toBeTruthy()
    expect(screen.getByText('1 active')).toBeTruthy()
    expect(screen.getByText('1 subagent')).toBeTruthy()
    expect(project.classList.contains('phase-editing')).toBe(true)
    expect(project.querySelector('.agent-avatar--editing')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Agent flow' }).getAttribute('href')).toBe('/activity')
    expect(screen.getByRole('link', { name: 'Agent Flow Light' }).getAttribute('href')).toBe('/activity/light')
    expect(screen.getByRole('link', { name: 'Open machine settings' }).getAttribute('href')).toBe('/settings/service-probes')
    expect(screen.getByRole('link', { name: 'Open CAVE workspace overview' }).getAttribute('href')).toBe('/')
    expect(screen.getByText('Live on NORWEGIANWOOD')).toBeTruthy()
    expect(await screen.findByText('70% left')).toBeTruthy()
  })

  it('applies a manually selected Code Dark theme to the landing-page scope', async () => {
    render(<WorkspaceDashboard />)

    const themeControl = screen.getByRole('combobox', { name: /Color theme/i })
    fireEvent.change(themeControl, { target: { value: 'code-dark' } })

    expect(themeControl.getAttribute('aria-label')).toContain('Manual Code Dark')
    expect(document.querySelector('.workspace-dashboard')?.getAttribute('data-theme')).toBe('code-dark')
  })
})
