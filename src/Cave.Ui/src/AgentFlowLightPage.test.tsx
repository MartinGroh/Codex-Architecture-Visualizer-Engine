import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readAgentFlowLight, readCaveInfo } from './api'
import { AgentFlowLightPage } from './AgentFlowLightPage'
import type { AgentFlowLightSnapshot, WorkspaceOverview } from './types'
import { useWorkspaceOverview } from './useWorkspaceOverview'

vi.mock('./api', () => ({
  readAgentFlowLight: vi.fn(),
  readCaveInfo: vi.fn(),
}))
vi.mock('./useWorkspaceOverview', () => ({ useWorkspaceOverview: vi.fn() }))

const workspaces: WorkspaceOverview[] = ['first', 'second'].map((workspaceId) => ({
  workspaceId,
  name: workspaceId === 'first' ? 'Atlas' : 'CAVE',
  rootPath: `C:\\${workspaceId}`,
  isAvailable: true,
  lastSeenAtUtc: '2026-09-28T08:00:00Z',
  knownAgentCount: 1,
  activeAgentCount: 1,
  activeSubagentCount: 0,
  currentPhase: 'Editing',
  activitySummary: 'Editing',
  lastActivityAtUtc: '2026-09-28T08:00:00Z',
  activityError: null,
}))

function feed(workspaceId: string, status: AgentFlowLightSnapshot['mainGoal']['status'] = 'Ready'): AgentFlowLightSnapshot {
  return {
    schemaVersion: 1,
    workspaceId,
    workspaceName: workspaceId === 'first' ? 'Atlas' : 'CAVE',
    sourceMode: 'Live',
    generatedAtUtc: '2026-09-28T08:00:00Z',
    sourceUpdatedAtUtc: '2026-09-28T08:00:00Z',
    sourceStatus: 'Ready',
    error: null,
    totalAgentCount: 1,
    agents: [{
      agentId: 'a'.repeat(64),
      displayName: 'Main agent',
      agentType: 'codex',
      isSubagent: false,
      state: 'Active',
      phase: 'Editing',
      summary: 'Editing files',
      currentFocus: 'Improve the viewer',
      focusEvidence: 'Declared',
      startedAtUtc: '2026-09-28T07:00:00Z',
      updatedAtUtc: '2026-09-28T08:00:00Z',
      evidence: 'Observed',
      summaryEvidence: 'Declared',
    }],
    mainGoal: {
      status,
      goal: status === 'Ready' ? {
        objective: 'Keep the architecture clear',
        isTruncated: false,
        status: 'Active',
        tokenBudget: null,
        tokensUsed: 100,
        timeUsedSeconds: 60,
        createdAt: 1,
        updatedAt: 2,
      } : null,
      retrievedAtUtc: status === 'Ready' ? '2026-09-28T08:00:00Z' : null,
      error: null,
    },
  }
}

beforeEach(() => {
  window.history.replaceState(null, '', '/activity/light')
  vi.mocked(useWorkspaceOverview).mockReturnValue({
    hostName: 'WORKSTATION', workspaces, diagnostics: [], error: null, isLoading: false,
    refresh: vi.fn(),
  })
  vi.mocked(readAgentFlowLight).mockImplementation(async (workspaceId) => feed(workspaceId))
  vi.mocked(readCaveInfo).mockRejectedValue(new Error('Usage unavailable'))
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({
    matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn(),
  }))
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.clearAllMocks()
  vi.unstubAllGlobals()
})

describe('AgentFlowLightPage', () => {
  it('keeps a completed agent briefly and allows immediate visual dismissal', async () => {
    window.history.replaceState(null, '', '/activity/light?workspace=first')
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-09-28T08:09:00Z'))
    vi.mocked(readAgentFlowLight).mockImplementation(async () => {
      const result = feed('first')
      result.agents[0].state = 'Completed'
      result.agents[0].phase = null
      return result
    })
    render(<AgentFlowLightPage />)
    expect(await screen.findByRole('link', { name: 'View flow as list' })).toBeTruthy()
    expect(screen.getByRole('link', { name: 'View flow as list' }).getAttribute('href')).toContain('workspace=first&agent=')
    fireEvent.click(screen.getByRole('button', { name: 'Dismiss Main agent' }))
    expect(screen.queryByRole('link', { name: 'View flow as list' })).toBeNull()
    fireEvent.change(screen.getByRole('combobox', { name: 'Project' }), { target: { value: 'all' } })
    fireEvent.change(screen.getByRole('combobox', { name: 'Project' }), { target: { value: 'first' } })
    expect(await screen.findByText('Keep the architecture clear')).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'View flow as list' })).toBeNull()
  })

  it('uses the light feed and shows the goal separately from declared agent focus', async () => {
    render(<AgentFlowLightPage />)

    expect(await screen.findAllByText('Keep the architecture clear')).toHaveLength(2)
    expect(screen.getAllByText('Improve the viewer')).toHaveLength(2)
    expect(screen.getAllByText('Declared focus')).toHaveLength(2)
    expect(screen.getByRole('link', { name: 'Full agent flow' }).getAttribute('href')).toBe('/activity')
    expect(readAgentFlowLight).toHaveBeenCalledWith('first', expect.any(AbortSignal))
    expect(readAgentFlowLight).toHaveBeenCalledWith('second', expect.any(AbortSignal))
    expect(window.location.search).toBe('?workspace=all')
  })

  it('clears a shared goal when the selected project changes or the feed fails', async () => {
    window.history.replaceState(null, '', '/activity/light?workspace=first')
    vi.mocked(readAgentFlowLight).mockImplementation(async (workspaceId) => feed(workspaceId, workspaceId === 'first' ? 'Ready' : 'Private'))
    render(<AgentFlowLightPage />)
    expect(await screen.findByText('Keep the architecture clear')).toBeTruthy()

    fireEvent.change(screen.getByRole('combobox', { name: 'Project' }), { target: { value: 'second' } })
    expect(screen.queryByText('Keep the architecture clear')).toBeNull()
    expect(await screen.findByText(/Goal is private/)).toBeTruthy()
    expect(window.location.search).toBe('?workspace=second')

    vi.mocked(readAgentFlowLight).mockRejectedValueOnce(new Error('Network disconnected'))
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))
    await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('Network disconnected'))
    expect(screen.queryByText('Improve the viewer')).toBeNull()
  })

  it('keeps an explicitly requested unavailable project bound until another project is chosen', async () => {
    window.history.replaceState(null, '', '/activity/light?workspace=missing')
    render(<AgentFlowLightPage />)

    expect(screen.getByText('This project is unavailable on this CAVE viewer. Choose another project.')).toBeTruthy()
    expect((screen.getByRole('combobox', { name: 'Project' }) as HTMLSelectElement).value).toBe('missing')
    expect(window.location.search).toBe('?workspace=missing')
    expect(readAgentFlowLight).not.toHaveBeenCalled()

    fireEvent.change(screen.getByRole('combobox', { name: 'Project' }), { target: { value: 'first' } })
    expect(await screen.findByText('Keep the architecture clear')).toBeTruthy()
    expect(window.location.search).toBe('?workspace=first')
    expect(readAgentFlowLight).toHaveBeenCalledWith('first', expect.any(AbortSignal))
  })

  it('does not switch to a different project when the chosen project leaves the catalog', async () => {
    window.history.replaceState(null, '', '/activity/light?workspace=first')
    const { rerender } = render(<AgentFlowLightPage />)
    expect(await screen.findByText('Keep the architecture clear')).toBeTruthy()
    expect(window.location.search).toBe('?workspace=first')

    vi.mocked(useWorkspaceOverview).mockReturnValue({
      hostName: 'WORKSTATION', workspaces: [workspaces[1]], diagnostics: [], error: null, isLoading: false,
      refresh: vi.fn(),
    })
    rerender(<AgentFlowLightPage />)

    expect(screen.getByText('This project is unavailable on this CAVE viewer. Choose another project.')).toBeTruthy()
    expect(screen.queryByText('Keep the architecture clear')).toBeNull()
    expect(window.location.search).toBe('?workspace=first')
    expect(readAgentFlowLight).not.toHaveBeenCalledWith('second', expect.anything())
  })
})
