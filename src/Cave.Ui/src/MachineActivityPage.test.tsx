import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { connectArchitectureFeed, readCaveInfo } from './api'
import { MachineActivityPage } from './MachineActivityPage'
import type { LiveArchitectureSnapshot, WorkspaceOverview } from './types'
import { useWorkspaceOverview } from './useWorkspaceOverview'

vi.mock('./api', () => ({
  connectArchitectureFeed: vi.fn(),
  readCaveInfo: vi.fn(),
}))

vi.mock('./useWorkspaceOverview', () => ({
  useWorkspaceOverview: vi.fn(),
}))

const activeWorkspace: WorkspaceOverview = {
  workspaceId: 'workspace-1',
  name: 'Architecture Engine',
  rootPath: 'C:\\Code\\Architecture Engine',
  isAvailable: true,
  lastSeenAtUtc: '2026-08-23T10:04:00Z',
  knownAgentCount: 1,
  activeAgentCount: 1,
  activeSubagentCount: 0,
  currentPhase: 'Editing',
  activitySummary: 'Editing App',
  lastActivityAtUtc: '2026-08-23T10:04:00Z',
  activityError: null,
}

const activeUpdate: LiveArchitectureSnapshot = {
  version: 4,
  observedAtUtc: '2026-08-23T10:04:00Z',
  refreshError: null,
  changedPaths: [],
  activityChanged: true,
  conversationChanged: false,
  snapshot: {
    metadata: {
      workspaceName: 'Architecture Engine',
      sourceKind: 'CodeGraph',
      providerId: 'codegraph',
      isLive: true,
      generatedAtUtc: '2026-08-23T10:04:00Z',
    },
    graph: {
      nodes: [{
        id: 'project:app',
        kind: 'Project',
        parentId: null,
        name: 'App',
        qualifiedName: 'src/App',
        description: null,
        categoryId: null,
        sourceLocations: [],
        tags: [],
      }],
      relations: [],
    },
    git: {
      status: 'Unavailable', baseline: null, worktree: null, files: [], nodes: [],
      unmappedFiles: [], error: null,
    },
    activity: {
      agents: [{
        agentId: 'session:task-1',
        agentType: 'main',
        isSubagent: false,
        state: 'Active',
        phase: 'Editing',
        summary: 'Editing App',
        hasObservedActivity: true,
        hasDeclaredScope: true,
        startedAtUtc: '2026-08-23T10:00:00Z',
        updatedAtUtc: '2026-08-23T10:04:00Z',
      }],
      nodes: [{
        agentId: 'session:task-1', nodeId: 'project:app', evidence: 'Observed',
        isDirect: true, updatedAtUtc: '2026-08-23T10:02:00Z', paths: ['src/App.tsx'],
      }],
      recentEdits: [{
        agentId: 'session:task-1', filePath: 'src/App.tsx', nodeIds: ['project:app'],
        observedAtUtc: '2026-08-23T10:03:00Z',
      }],
      latestInstruction: { id: 'prompt-1', observedAtUtc: '2026-08-23T10:00:00Z' },
      unmappedPaths: [],
      error: null,
    },
    conversation: {
      sharingEnabled: false,
      status: 'Disabled',
      messages: [],
      error: null,
      control: {
        state: 'Unavailable', sessionId: null, turnId: null, canSend: false,
        deliveries: [], error: null,
      },
    },
  },
}

let overviewState: ReturnType<typeof useWorkspaceOverview>
let feedUpdate: ((update: LiveArchitectureSnapshot) => void) | null

beforeEach(() => {
  window.history.replaceState(null, '', '/activity')
  vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-08-23T10:06:00Z'))
  const storedValues = new Map<string, string>()
  vi.stubGlobal('localStorage', {
    getItem: vi.fn((key: string) => storedValues.get(key) ?? null),
    setItem: vi.fn((key: string, value: string) => storedValues.set(key, value)),
    removeItem: vi.fn((key: string) => storedValues.delete(key)),
    clear: vi.fn(() => storedValues.clear()),
  })
  overviewState = {
    hostName: 'NORWEGIANWOOD',
    workspaces: [],
    diagnostics: [],
    error: null,
    isLoading: false,
    refresh: vi.fn(),
  }
  feedUpdate = null
  vi.mocked(useWorkspaceOverview).mockImplementation(() => overviewState)
  vi.mocked(connectArchitectureFeed).mockImplementation(async (onUpdate) => {
    feedUpdate = onUpdate
    return () => undefined
  })
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
        limitId: 'codex', limitName: 'Codex', window: 'primary', usedPercent: 30,
        windowDurationMinutes: 10_080, resetsAtUtc: '2026-08-27T00:00:00Z',
      }],
      retrievedAtUtc: '2026-08-23T12:00:00Z',
      error: null,
    },
  })
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({
    matches: false,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }))
})

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
  vi.clearAllMocks()
  vi.unstubAllGlobals()
})

describe('MachineActivityPage', () => {
  it('opens a finished agent from the light feed as an ordered list', async () => {
    window.history.replaceState(null, '', '/activity?workspace=workspace-1&agent=opaque-agent-key')
    overviewState = { ...overviewState, workspaces: [{ ...activeWorkspace, activeAgentCount: 0 }] }
    const completed = structuredClone(activeUpdate)
    completed.snapshot.activity.agents[0].state = 'Completed'
    completed.snapshot.activity.agents[0].publicId = 'opaque-agent-key'
    render(<MachineActivityPage />)
    await waitFor(() => expect(connectArchitectureFeed).toHaveBeenCalledOnce())
    await act(async () => feedUpdate?.(completed))
    expect(screen.getByRole('button', { name: 'Compact' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('list', { name: 'Main agent observed work timeline' })).toBeTruthy()
    expect(screen.getByText('Complete')).toBeTruthy()
  })

  it('defaults to compact on mobile and opens timeline details by tapping an agent', async () => {
    vi.mocked(window.matchMedia).mockReturnValue({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() } as unknown as MediaQueryList)
    overviewState = { ...overviewState, workspaces: [activeWorkspace] }
    const view = render(<MachineActivityPage />)
    await waitFor(() => expect(connectArchitectureFeed).toHaveBeenCalledOnce())
    await act(async () => feedUpdate?.(activeUpdate))
    expect(screen.getByRole('button', { name: 'Compact' }).getAttribute('aria-pressed')).toBe('true')
    const disclosure = view.container.querySelector('.agent-flow-compact__agent')!
    expect(disclosure.hasAttribute('open')).toBe(false)
    fireEvent.click(disclosure.querySelector('summary')!)
    expect(disclosure.hasAttribute('open')).toBe(true)
    expect(screen.getByRole('list', { name: 'Main agent observed work timeline' })).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Timeline' }))
    expect(window.localStorage.getItem('cave.machine-activity.view')).toBe('timeline')
    view.unmount()
    render(<MachineActivityPage />)
    expect(screen.getByRole('button', { name: 'Timeline' }).getAttribute('aria-pressed')).toBe('true')
  })
  it('keeps the machine overview separate, shows usage, and explains the quiet state', async () => {
    render(<MachineActivityPage />)

    expect(screen.getByRole('heading', { name: 'Agent flow' })).toBeTruthy()
    expect(screen.getByText('No agents are working right now.')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'Open project overview' }).getAttribute('href')).toBe('/')
    expect(screen.getByRole('link', { name: 'Open CAVE workspace overview' }).getAttribute('href')).toBe('/')
    expect(await screen.findByText('70% left')).toBeTruthy()
    expect(screen.queryByText(/per-agent token usage/i)).toBeNull()
  })

  it('retains work observed during the visit until its completed-project check is clicked', async () => {
    overviewState = { ...overviewState, workspaces: [activeWorkspace] }
    const view = render(<MachineActivityPage />)

    await waitFor(() => expect(connectArchitectureFeed).toHaveBeenCalledOnce())
    await act(async () => feedUpdate?.(activeUpdate))
    expect((await screen.findAllByText('Architecture Engine')).length).toBeGreaterThan(0)

    overviewState = {
      ...overviewState,
      workspaces: [{
        ...activeWorkspace,
        activeAgentCount: 0,
        currentPhase: null,
        activitySummary: 'Waiting for the next turn',
      }],
    }
    view.rerender(<MachineActivityPage />)

    const clear = await screen.findByRole('button', {
      name: 'Clear completed work for Architecture Engine',
    })
    expect(screen.getByText(/Done ·/i)).toBeTruthy()
    fireEvent.click(clear)

    expect(await screen.findByText('No agents are working right now.')).toBeTruthy()
    expect(view.container.querySelector('.machine-project-flow')).toBeNull()
  })

  it('renders concise labels for every visible timeline milestone', async () => {
    overviewState = { ...overviewState, workspaces: [activeWorkspace] }
    render(<MachineActivityPage />)

    await waitFor(() => expect(connectArchitectureFeed).toHaveBeenCalledOnce())
    await act(async () => feedUpdate?.(activeUpdate))

    expect(await screen.findByText('Main agent started')).toBeTruthy()
    expect(screen.getByText('App')).toBeTruthy()
    expect(screen.getByText('App.tsx')).toBeTruthy()
    expect(screen.getByText('Editing')).toBeTruthy()
    expect(screen.getByText('App').parentElement?.classList.contains('label-row-1')).toBe(true)
    expect(screen.getAllByText('Observed work detail').length).toBeGreaterThan(0)
  })

  it('keeps project slots stable and persists the selected project layout', async () => {
    const firstWorkspace = { ...activeWorkspace, workspaceId: 'workspace-b', name: 'Beta' }
    const secondWorkspace = { ...activeWorkspace, workspaceId: 'workspace-a', name: 'Alpha' }
    overviewState = { ...overviewState, workspaces: [firstWorkspace, secondWorkspace] }
    const view = render(<MachineActivityPage />)

    await waitFor(() => expect(connectArchitectureFeed).toHaveBeenCalledTimes(2))
    expect([...view.container.querySelectorAll('.machine-project-flow > header strong')]
      .map((element) => element.textContent)).toEqual(['Beta', 'Alpha'])

    overviewState = { ...overviewState, workspaces: [secondWorkspace, firstWorkspace] }
    view.rerender(<MachineActivityPage />)
    expect([...view.container.querySelectorAll('.machine-project-flow > header strong')]
      .map((element) => element.textContent)).toEqual(['Beta', 'Alpha'])

    fireEvent.click(screen.getByRole('button', { name: 'List' }))
    expect(view.container.querySelector('.machine-activity-projects--list')).toBeTruthy()
    expect(window.localStorage.getItem('cave.machine-activity.layout')).toBe('list')
  })

  it('shows the shared prompt and a retained public completion summary', async () => {
    overviewState = { ...overviewState, workspaces: [activeWorkspace] }
    const view = render(<MachineActivityPage />)
    const sharedActiveUpdate = structuredClone(activeUpdate)
    sharedActiveUpdate.snapshot.activity.latestInstruction = {
      id: 'task-1:turn-1',
      observedAtUtc: '2026-08-23T10:00:00Z',
    }
    sharedActiveUpdate.snapshot.conversation = {
      ...sharedActiveUpdate.snapshot.conversation,
      sharingEnabled: true,
      status: 'Ready',
      messages: [{
        eventId: 'prompt-event', sessionId: 'task-1', turnId: 'turn-1', agentId: null,
        agentType: 'main', isSubagent: false, role: 'User', kind: 'Prompt',
        text: 'Make the Agent flow timeline easier to understand.',
        isTruncated: false, isStreaming: false, occurredAtUtc: '2026-08-23T10:00:00Z',
      }],
    }

    await waitFor(() => expect(connectArchitectureFeed).toHaveBeenCalledOnce())
    await act(async () => feedUpdate?.(sharedActiveUpdate))
    expect((await screen.findAllByText('Make the Agent flow timeline easier to understand.')).length)
      .toBeGreaterThan(0)

    overviewState = {
      ...overviewState,
      workspaces: [{ ...activeWorkspace, activeAgentCount: 0, currentPhase: null }],
    }
    view.rerender(<MachineActivityPage />)
    const completedUpdate = structuredClone(sharedActiveUpdate)
    completedUpdate.observedAtUtc = '2026-08-23T10:05:00Z'
    completedUpdate.snapshot.activity.agents[0].state = 'Completed'
    completedUpdate.snapshot.conversation.messages.push({
      eventId: 'final-event', sessionId: 'task-1', turnId: 'turn-1', agentId: null,
      agentType: 'main', isSubagent: false, role: 'Assistant', kind: 'Final',
      text: 'The stable layout and readable timeline are now verified.',
      isTruncated: false, isStreaming: false, occurredAtUtc: '2026-08-23T10:05:00Z',
    })
    await act(async () => feedUpdate?.(completedUpdate))

    expect(await screen.findByText('Completed summary')).toBeTruthy()
    expect(screen.getAllByText('The stable layout and readable timeline are now verified.').length).toBeGreaterThan(0)
  })
})
