import { describe, expect, it } from 'vitest'
import { buildMachineActivityTimelines, extractUserInstructionText } from './machineActivityTimeline'
import type { LiveArchitectureSnapshot, WorkspaceOverview } from './types'

const workspace = (workspaceId: string, activeAgentCount: number): WorkspaceOverview => ({
  workspaceId,
  name: workspaceId,
  rootPath: `C:\\Code\\${workspaceId}`,
  isAvailable: true,
  lastSeenAtUtc: '2026-08-23T10:00:00Z',
  knownAgentCount: activeAgentCount,
  activeAgentCount,
  activeSubagentCount: 0,
  currentPhase: 'Editing',
  activitySummary: 'Working on the architecture',
  lastActivityAtUtc: '2026-08-23T10:04:00Z',
  activityError: null,
})

const update = (agentId: string, startedAtUtc: string): LiveArchitectureSnapshot => ({
  version: 4,
  observedAtUtc: '2026-08-23T10:04:00Z',
  refreshError: null,
  changedPaths: [],
  activityChanged: true,
  conversationChanged: false,
  snapshot: {
    metadata: {
      workspaceName: 'Workspace',
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
    git: { status: 'Unavailable', baseline: null, worktree: null, files: [], nodes: [], unmappedFiles: [], error: null },
    activity: {
      agents: [{
        agentId,
        agentType: 'Codex',
        isSubagent: false,
        state: 'Active',
        phase: 'Editing',
        summary: 'Editing App',
        hasObservedActivity: true,
        hasDeclaredScope: true,
        startedAtUtc,
        updatedAtUtc: '2026-08-23T10:04:00Z',
      }],
      nodes: [{
        agentId,
        nodeId: 'project:app',
        evidence: 'Observed',
        isDirect: true,
        updatedAtUtc: '2026-08-23T10:02:00Z',
        paths: ['src/App.tsx'],
      }],
      recentEdits: [{
        agentId,
        filePath: 'src/App.tsx',
        nodeIds: ['project:app'],
        observedAtUtc: '2026-08-23T10:03:00Z',
      }],
      latestInstruction: { id: 'prompt-1', observedAtUtc: startedAtUtc },
      unmappedPaths: [],
      error: null,
    },
    conversation: {
      sharingEnabled: false,
      status: 'Disabled',
      messages: [],
      error: null,
      control: {
        state: 'Unavailable',
        sessionId: null,
        turnId: null,
        canSend: false,
        deliveries: [],
        error: null,
      },
    },
  },
})

describe('buildMachineActivityTimelines', () => {
  it('shows only the real user instruction when Codex ambient browser context is present', () => {
    const rawPrompt = `<in-app-browser-context source="ambient-ui-state">
This block is automatically supplied ambient UI state, not part of the user's request.
# In app browser:
- Current URL: http://127.0.0.1:5098/
</in-app-browser-context>

## My request:
Keep completed project activity visible until I dismiss it.`

    expect(extractUserInstructionText(rawPrompt)).toBe(
      'Keep completed project activity visible until I dismiss it.',
    )
  })

  it('preserves ordinary prompt text without a request envelope', () => {
    expect(extractUserInstructionText('Refine the agent flow timeline.')).toBe(
      'Refine the agent flow timeline.',
    )
  })

  it('preserves a user-authored ambient-context element that is not the leading host envelope', () => {
    const prompt = `Keep this prefix.

<in-app-browser-context source="ambient-ui-state">
This element was included by the user.
</in-app-browser-context>

Keep this suffix.`

    expect(extractUserInstructionText(prompt)).toBe(prompt)
  })

  it('preserves leading context markup from any source other than the host ambient envelope', () => {
    const prompt = `<in-app-browser-context source="user-authored">
Preserve this block.
</in-app-browser-context>

## My request:
Preserve the marker too.`

    expect(extractUserInstructionText(prompt)).toBe(prompt)
  })

  it('preserves an ordinary request heading when no host envelope preceded it', () => {
    const prompt = `Context the user intentionally wrote.

## My request:
Do not discard the context.`

    expect(extractUserInstructionText(prompt)).toBe(prompt)
  })

  it('uses one shared duration scale and keeps prompt, architecture, edit, and phase evidence', () => {
    const timelines = buildMachineActivityTimelines([
      { workspace: workspace('long', 1), update: update('main-long', '2026-08-23T10:00:00Z') },
      { workspace: workspace('short', 1), update: update('main-short', '2026-08-23T10:03:00Z') },
    ], Date.parse('2026-08-23T10:05:00Z'))

    expect(timelines).toHaveLength(2)
    expect(timelines[0].instructionId).toBe('prompt-1')
    expect(timelines[0].lanes[0].durationPercent).toBe(100)
    expect(timelines[1].lanes[0].durationPercent).toBe(40)
    expect(timelines[1].lanes[0].milestones.at(-1)?.positionPercent).toBe(100)
    expect(timelines[0].lanes[0].milestones.map((step) => step.kind)).toEqual([
      'Start',
      'Architecture',
      'Edit',
      'Phase',
    ])
    expect(timelines[0].lanes[0].milestones.map((step) => step.positionPercent)).toEqual([
      0,
      40,
      60,
      100,
    ])
  })

  it('freezes a retained project at its last active observation', () => {
    const timelines = buildMachineActivityTimelines([{
      workspace: workspace('complete', 0),
      update: update('main', '2026-08-23T10:00:00Z'),
    }], Date.parse('2026-08-23T10:10:00Z'))

    expect(timelines[0].lanes[0].durationMs).toBe(4 * 60 * 1_000)
    expect(timelines[0].lanes[0].milestones.at(-1)?.positionPercent).toBe(100)
  })

  it('binds shared prompt and final text only to the matching instruction', () => {
    const sharedUpdate = update('main', '2026-08-23T10:00:00Z')
    sharedUpdate.snapshot.activity.latestInstruction = {
      id: 'session-1:turn-1',
      observedAtUtc: '2026-08-23T10:00:00Z',
    }
    sharedUpdate.snapshot.conversation = {
      ...sharedUpdate.snapshot.conversation,
      sharingEnabled: true,
      status: 'Ready',
      messages: [{
        eventId: 'prompt-event', sessionId: 'session-1', turnId: 'turn-1', agentId: null,
        agentType: 'main', isSubagent: false, role: 'User', kind: 'Prompt',
        text: 'Keep the project cards stable and make the timeline readable.',
        isTruncated: false, isStreaming: false, occurredAtUtc: '2026-08-23T10:00:00Z',
      }, {
        eventId: 'final-event', sessionId: 'session-1', turnId: 'turn-1', agentId: null,
        agentType: 'main', isSubagent: false, role: 'Assistant', kind: 'Final',
        text: 'Stable placement and the full-width timeline are complete.',
        isTruncated: false, isStreaming: false, occurredAtUtc: '2026-08-23T10:04:30Z',
      }],
    }

    const [timeline] = buildMachineActivityTimelines(
      [{ workspace: workspace('shared', 1), update: sharedUpdate }],
      Date.parse('2026-08-23T10:05:00Z'),
    )

    expect(timeline.prompt?.text).toContain('project cards stable')
    expect(timeline.lanes[0].completionSummary?.text).toContain('full-width timeline')
  })
})
