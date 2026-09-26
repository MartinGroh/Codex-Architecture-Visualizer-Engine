import { describe, expect, it } from 'vitest'
import {
  createAutoViewState,
  deriveAutoViewPlan,
  nextAutoViewTransitionAt,
  pauseAutoView,
  reconcileAutoViewState,
  resumeAutoView,
} from './autoView'
import type { AgentActivityPhase, ArchitectureNode, ArchitectureSnapshot } from './types'

const node = (
  id: string,
  kind: ArchitectureNode['kind'],
  parentId: string | null,
  tags: string[] = [],
): ArchitectureNode => ({
  id,
  kind,
  parentId,
  name: id,
  qualifiedName: id,
  description: null,
  categoryId: null,
  tags,
  sourceLocations: [],
})

function snapshot(
  activeNodeIds: string[],
  phase: AgentActivityPhase = 'Editing',
): ArchitectureSnapshot {
  const agents = activeNodeIds.map((nodeId, index) => ({
    agentId: `agent-${index}`,
    agentType: index === 0 ? 'main' : 'subagent',
    isSubagent: index > 0,
    state: 'Active' as const,
    phase,
    summary: `Working on ${nodeId}`,
    hasObservedActivity: true,
    hasDeclaredScope: false,
    startedAtUtc: '2026-08-23T10:00:00Z',
    updatedAtUtc: '2026-08-23T10:00:01Z',
  }))
  return {
    metadata: {
      workspaceName: 'Auto test',
      providerId: 'test',
      sourceKind: 'Sample',
      isLive: true,
      generatedAtUtc: '2026-08-23T10:00:01Z',
    },
    graph: {
      nodes: [
        node('group', 'ArchitectureGroup', null),
        node('project-a', 'Project', 'group'),
        node('namespace-a', 'Namespace', 'project-a'),
        node('class-a', 'Class', 'namespace-a'),
        node('class-a2', 'Interface', 'namespace-a'),
        node('project-b', 'Project', 'group'),
        node('namespace-b', 'Namespace', 'project-b'),
        node('class-b', 'Class', 'namespace-b'),
        node('project-test', 'Project', 'group', ['project-type:test']),
        node('test-class', 'Class', 'project-test'),
      ],
      relations: [{
        id: 'class-dependency',
        sourceId: 'class-a',
        targetId: 'class-b',
        kind: 'DependsOn',
        weight: 1,
        evidenceCount: 1,
        confidence: 'Exact',
      }],
    },
    git: {
      status: 'Ready',
      baseline: null,
      worktree: null,
      files: [],
      nodes: [],
      unmappedFiles: [],
      error: null,
    },
    activity: {
      agents,
      nodes: activeNodeIds.map((nodeId, index) => ({
        agentId: `agent-${index}`,
        nodeId,
        evidence: 'Observed' as const,
        isDirect: true,
        updatedAtUtc: '2026-08-23T10:00:01Z',
        paths: [`src/${nodeId}.cs`],
      })),
      recentEdits: [],
      latestInstruction: { id: 'instruction-1', observedAtUtc: '2026-08-23T10:00:00Z' },
      unmappedPaths: [],
      error: null,
    },
    conversation: {
      sharingEnabled: false,
      status: 'Disabled',
      messages: [],
      control: {
        sessionId: null,
        turnId: null,
        state: 'Unavailable',
        canSend: false,
        deliveries: [],
        error: null,
      },
      error: null,
    },
  }
}

describe('adaptive Auto view planning', () => {
  it('reveals an active class through its project and namespace without revealing siblings', () => {
    const plan = deriveAutoViewPlan(snapshot(['class-a']))

    expect(plan).not.toBeNull()
    expect(plan?.projectionLevel).toBe('Projects')
    expect(plan?.activeNodeIds).toEqual(new Set(['class-a']))
    expect(plan?.expandedNodeIds).toEqual(new Set(['project-a', 'namespace-a']))
    expect(plan?.revealedNodeIds).toEqual(new Set(['namespace-a', 'class-a']))
    expect(plan?.focusNodeIds).toEqual(new Set([
      'project-a', 'namespace-a', 'class-a',
    ]))
    expect(plan?.contextNodeIds).toEqual(new Set())
  })

  it('collapses work across projects to readable project groups', () => {
    const plan = deriveAutoViewPlan(snapshot(['class-a', 'class-b']))

    expect(plan?.activeNodeIds).toEqual(new Set(['class-a', 'class-b']))
    expect(plan?.expandedNodeIds).toEqual(new Set())
    expect(plan?.revealedNodeIds).toEqual(new Set())
    expect(plan?.fitNodeIds).toEqual(new Set(['project-a', 'project-b']))
  })

  it('follows only the newest activity batch instead of accumulated task history', () => {
    const source = snapshot(['class-a'])
    source.activity.nodes[0].updatedAtUtc = '2026-08-23T10:00:01Z'
    source.activity.nodes.push({
      agentId: 'agent-0',
      nodeId: 'class-b',
      evidence: 'Observed',
      isDirect: true,
      updatedAtUtc: '2026-08-23T10:00:05Z',
      paths: ['src/class-b.cs'],
    })

    const plan = deriveAutoViewPlan(source)

    expect(plan?.activeNodeIds).toEqual(new Set(['class-b']))
    expect(plan?.fitNodeIds).toEqual(new Set(['project-b', 'namespace-b', 'class-b']))
  })

  it('does not invent children for project-only activity', () => {
    const plan = deriveAutoViewPlan(snapshot(['project-a']))

    expect(plan?.activeNodeIds).toEqual(new Set(['project-a']))
    expect(plan?.expandedNodeIds).toEqual(new Set())
    expect(plan?.revealedNodeIds).toEqual(new Set())
    expect(plan?.focusNodeIds).toEqual(new Set(['project-a']))
  })

  it('uses current-instruction edits as bounded evidence-backed context', () => {
    const source = snapshot(['namespace-a'])
    source.activity.recentEdits = [
      {
        agentId: 'agent-0', filePath: 'src/class-a.cs', nodeIds: ['class-a'],
        observedAtUtc: '2026-08-23T10:00:03Z',
      },
      {
        agentId: 'agent-0', filePath: 'src/class-a2.cs', nodeIds: ['class-a2'],
        observedAtUtc: '2026-08-23T10:00:02Z',
      },
    ]

    const plan = deriveAutoViewPlan(source, { maxRevealedNodes: 2 })

    expect(plan?.revealedNodeIds).toEqual(new Set(['namespace-a', 'class-a']))
    expect(plan?.contextNodeIds).toEqual(new Set(['class-a']))
  })

  it('respects projection filters instead of falling back to a hidden ancestor', () => {
    const plan = deriveAutoViewPlan(snapshot(['test-class']), { hideTests: true })

    expect(plan).toBeNull()
  })

  it('does not guess a node when active activity has no mapped location', () => {
    const source = snapshot(['project-a'])
    source.activity.nodes = []

    expect(deriveAutoViewPlan(source)).toBeNull()
  })

  it('keeps a stable signature when only the activity phase changes', () => {
    const editing = deriveAutoViewPlan(snapshot(['class-a'], 'Editing'))
    const reading = deriveAutoViewPlan(snapshot(['class-a'], 'Reading'))

    expect(reading?.signature).toBe(editing?.signature)
    expect(reading?.dominantPhase).toBe('Reading')
  })
})

describe('adaptive Auto view stability', () => {
  it('settles a new editing plan before committing it', () => {
    const plan = deriveAutoViewPlan(snapshot(['class-a']))!
    const settling = reconcileAutoViewState(createAutoViewState(), plan, 'instruction-1', 1_000)

    expect(settling.status).toBe('Settling')
    expect(nextAutoViewTransitionAt(settling)).toBe(1_300)

    const committed = reconcileAutoViewState(settling, plan, 'instruction-1', 1_300)
    expect(committed.status).toBe('Following')
    expect(committed.committedPlan).toBe(plan)
    expect(committed.cameraRevision).toBe(1)
  })

  it('holds non-editing movement until the current plan has dwelled', () => {
    const editing = deriveAutoViewPlan(snapshot(['class-a']))!
    const firstCandidate = reconcileAutoViewState(createAutoViewState(), editing, 'instruction-1', 0)
    const committed = reconcileAutoViewState(firstCandidate, editing, 'instruction-1', 300)
    const reading = deriveAutoViewPlan(snapshot(['class-b'], 'Reading'))!
    const settling = reconcileAutoViewState(committed, reading, 'instruction-1', 400)

    expect(nextAutoViewTransitionAt(settling)).toBe(1_800)
    expect(reconcileAutoViewState(settling, reading, 'instruction-1', 1_799).status)
      .toBe('Settling')
    expect(reconcileAutoViewState(settling, reading, 'instruction-1', 1_800).committedPlan)
      .toBe(reading)
  })

  it('pauses predictably and resumes the retained plan', () => {
    const plan = deriveAutoViewPlan(snapshot(['class-a']))!
    const candidate = reconcileAutoViewState(createAutoViewState(), plan, 'instruction-1', 0)
    const committed = reconcileAutoViewState(candidate, plan, 'instruction-1', 300)
    const paused = pauseAutoView(committed)

    expect(reconcileAutoViewState(paused, plan, 'instruction-1', 500)).toBe(paused)
    const resumed = resumeAutoView(paused)
    expect(resumed.status).toBe('Following')
    expect(resumed.cameraRevision).toBe(2)
  })

  it('resumes automatically when a new instruction arrives', () => {
    const oldPlan = deriveAutoViewPlan(snapshot(['class-a']))!
    const candidate = reconcileAutoViewState(createAutoViewState(), oldPlan, 'instruction-1', 0)
    const paused = pauseAutoView(reconcileAutoViewState(candidate, oldPlan, 'instruction-1', 300))
    const nextSnapshot = snapshot(['class-b'])
    nextSnapshot.activity.latestInstruction = {
      id: 'instruction-2',
      observedAtUtc: '2026-08-23T10:01:00Z',
    }
    const nextPlan = deriveAutoViewPlan(nextSnapshot)!

    const resumed = reconcileAutoViewState(paused, nextPlan, 'instruction-2', 1_000)
    expect(resumed.status).toBe('Settling')
    expect(resumed.candidatePlan).toBe(nextPlan)
  })
})
