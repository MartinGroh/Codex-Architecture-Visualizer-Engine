import { MarkerType } from '@xyflow/react'
import { describe, expect, it } from 'vitest'
import {
  createAgentNodeDecorations,
  createFlowEdges,
  formatNodeKind,
  highlightConnectedEdges,
} from './graphPresentation'
import type { ArchitectureSnapshot } from './types'

const snapshot: ArchitectureSnapshot = {
  metadata: {
    workspaceName: 'Test', providerId: 'test', sourceKind: 'Sample',
    isLive: false, generatedAtUtc: '2026-08-16T12:00:00Z',
  },
  graph: {
    nodes: [
      {
        id: 'group', kind: 'ArchitectureGroup', name: 'Group', parentId: null,
        qualifiedName: null, description: null, categoryId: 'core', tags: [], sourceLocations: [],
      },
      {
        id: 'project', kind: 'Project', name: 'Project', parentId: 'group',
        qualifiedName: null, description: null, categoryId: 'core', tags: [], sourceLocations: [],
      },
    ],
    relations: [
      {
        id: 'dependency', sourceId: 'project', targetId: 'group', kind: 'DependsOn',
        weight: 1, evidenceCount: 1, confidence: 'Exact',
      },
    ],
  },
  git: {
    status: 'Ready',
    baseline: { kind: 'Upstream', reference: 'origin/main', resolvedSha: 'abc123' },
    worktree: null,
    files: [],
    nodes: [],
    unmappedFiles: [],
    error: null,
  },
  activity: { agents: [], nodes: [], latestInstruction: null, recentEdits: [], unmappedPaths: [], error: null },
  conversation: {
    sharingEnabled: false,
    status: 'Disabled',
    messages: [],
    control: { sessionId: null, turnId: null, state: 'Unavailable', canSend: false, deliveries: [], error: null },
    error: null,
  },
}

describe('graph presentation', () => {
  it('keeps containment separate from semantic dependency evidence', () => {
    const edges = createFlowEdges(snapshot)

    expect(edges).toHaveLength(2)
    expect(edges.find((edge) => edge.id.startsWith('contains:'))).toMatchObject({
      source: 'group',
      target: 'project',
      ariaLabel: 'Group contains Project',
      className: 'containment-edge',
      interactionWidth: 24,
      markerEnd: {
        type: MarkerType.ArrowClosed,
        width: 15,
        height: 15,
      },
      selectable: false,
    })
    const dependency = edges.find((edge) => edge.id === 'dependency')
    expect(dependency).toMatchObject({ ariaLabel: 'Project depends on Group' })
    expect(dependency).not.toHaveProperty('label')
  })

  it('emphasizes only routes connected to the selected card', () => {
    const edges = createFlowEdges({
      ...snapshot,
      graph: {
        nodes: [
          ...snapshot.graph.nodes,
          {
            id: 'other', kind: 'Project', name: 'Other', parentId: null,
            qualifiedName: null, description: null, categoryId: null, tags: [], sourceLocations: [],
          },
        ],
        relations: [
          ...snapshot.graph.relations,
          {
            id: 'unrelated', sourceId: 'other', targetId: 'group', kind: 'DependsOn',
            weight: 1, evidenceCount: 1, confidence: 'Exact',
          },
        ],
      },
    })
    const highlighted = highlightConnectedEdges(edges, 'project')

    expect(highlighted.find((edge) => edge.id.startsWith('contains:'))?.className)
      .toContain('is-selection-related')
    expect(highlighted.find((edge) => edge.id.startsWith('contains:'))?.markerEnd)
      .toMatchObject({ color: 'var(--violet)' })
    expect(highlighted.find((edge) => edge.id === 'dependency')?.className)
      .toContain('is-selection-related')
    expect(highlighted.find((edge) => edge.id === 'unrelated')?.className)
      .toBe('semantic-edge relation-dependson')
  })

  it('marks an active consumer dependency as an input without reversing the static arrow', () => {
    const edges = createFlowEdges({
      ...snapshot,
      activity: {
        latestInstruction: null,
        agents: [{
          agentId: 'agent-1', agentType: 'main', isSubagent: false, state: 'Active', phase: 'Editing',
          summary: 'Working on Project', hasObservedActivity: true, hasDeclaredScope: false,
          startedAtUtc: '2026-08-17T00:00:00Z', updatedAtUtc: '2026-08-17T00:00:01Z',
        }],
        nodes: [{
          agentId: 'agent-1', nodeId: 'project', evidence: 'Observed', isDirect: true,
          updatedAtUtc: '2026-08-17T00:00:01Z', paths: ['Project.cs'],
        }],
        recentEdits: [], unmappedPaths: [], error: null,
      },
    })

    const dependency = edges.find((edge) => edge.id === 'dependency')
    expect(dependency).toMatchObject({ source: 'project', target: 'group' })
    expect(dependency?.className).toContain('activity-edge--input')
    expect(dependency?.data?.activityDirection).toBe('input')
  })

  it('marks an active provider dependency as an output without reversing the static arrow', () => {
    const edges = createFlowEdges({
      ...snapshot,
      activity: {
        latestInstruction: null,
        agents: [{
          agentId: 'agent-1', agentType: 'main', isSubagent: false, state: 'Active', phase: 'Editing',
          summary: 'Working on Group', hasObservedActivity: true, hasDeclaredScope: false,
          startedAtUtc: '2026-08-17T00:00:00Z', updatedAtUtc: '2026-08-17T00:00:01Z',
        }],
        nodes: [{
          agentId: 'agent-1', nodeId: 'group', evidence: 'Observed', isDirect: true,
          updatedAtUtc: '2026-08-17T00:00:01Z', paths: ['Group.cs'],
        }],
        recentEdits: [], unmappedPaths: [], error: null,
      },
    })

    const dependency = edges.find((edge) => edge.id === 'dependency')
    expect(dependency).toMatchObject({ source: 'project', target: 'group' })
    expect(dependency?.className).toContain('activity-edge--output')
    expect(dependency?.data?.activityDirection).toBe('output')
  })

  it('does not animate a dependency after the related work completes', () => {
    const edges = createFlowEdges({
      ...snapshot,
      activity: {
        latestInstruction: null,
        agents: [{
          agentId: 'agent-1', agentType: 'main', isSubagent: false, state: 'Completed', phase: null,
          summary: 'Finished Project', hasObservedActivity: true, hasDeclaredScope: false,
          startedAtUtc: '2026-08-17T00:00:00Z', updatedAtUtc: '2026-08-17T00:00:02Z',
        }],
        nodes: [{
          agentId: 'agent-1', nodeId: 'project', evidence: 'Observed', isDirect: true,
          updatedAtUtc: '2026-08-17T00:00:01Z', paths: ['Project.cs'],
        }],
        recentEdits: [], unmappedPaths: [], error: null,
      },
    })

    const dependency = edges.find((edge) => edge.id === 'dependency')
    expect(dependency?.className).toBe('semantic-edge relation-dependson')
    expect(dependency?.data?.activityDirection).toBeUndefined()
  })

  it('keeps inheritance static even when an incident node is active', () => {
    const edges = createFlowEdges({
      ...snapshot,
      graph: {
        ...snapshot.graph,
        relations: [{
          id: 'inheritance', sourceId: 'project', targetId: 'group', kind: 'Inherits',
          weight: 1, evidenceCount: 1, confidence: 'Exact',
        }],
      },
      activity: {
        latestInstruction: null,
        agents: [{
          agentId: 'agent-1', agentType: 'main', isSubagent: false, state: 'Active', phase: 'Reading',
          summary: 'Working on Project', hasObservedActivity: true, hasDeclaredScope: false,
          startedAtUtc: '2026-08-17T00:00:00Z', updatedAtUtc: '2026-08-17T00:00:01Z',
        }],
        nodes: [{
          agentId: 'agent-1', nodeId: 'project', evidence: 'Observed', isDirect: true,
          updatedAtUtc: '2026-08-17T00:00:01Z', paths: ['Project.cs'],
        }],
        recentEdits: [], unmappedPaths: [], error: null,
      },
    })

    const inheritance = edges.find((edge) => edge.id === 'inheritance')
    expect(inheritance?.className).toBe('semantic-edge relation-inherits')
    expect(inheritance?.data?.activityDirection).toBeUndefined()
  })

  it('pulses the latest mapped node when newer pathless activity keeps the agent active', () => {
    const decorations = createAgentNodeDecorations({
      ...snapshot,
      activity: {
        latestInstruction: null,
        agents: [{
          agentId: 'agent-main', agentType: 'main', isSubagent: false, state: 'Active', phase: 'Thinking',
          summary: 'Working on Project', hasObservedActivity: true, hasDeclaredScope: false,
          startedAtUtc: '2026-08-17T00:00:00Z', updatedAtUtc: '2026-08-17T00:00:03Z',
        }],
        nodes: [
          {
            agentId: 'agent-main', nodeId: 'group', evidence: 'Observed', isDirect: true,
            updatedAtUtc: '2026-08-17T00:00:01Z', paths: ['Old.cs'],
          },
          {
            agentId: 'agent-main', nodeId: 'project', evidence: 'Observed', isDirect: true,
            updatedAtUtc: '2026-08-17T00:00:02Z', paths: ['Current.cs'],
          },
        ],
        recentEdits: [], unmappedPaths: [], error: null,
      },
    })

    expect(decorations.has('group')).toBe(false)
    expect(decorations.get('project')).toEqual([
      expect.objectContaining({ state: 'Active', agent: expect.objectContaining({ isSubagent: false }) }),
    ])
  })

  it('keeps quiet markers on every touched node when a subagent finishes', () => {
    const decorations = createAgentNodeDecorations({
      ...snapshot,
      activity: {
        latestInstruction: null,
        agents: [{
          agentId: 'agent-sub', agentType: 'reviewer', isSubagent: true, state: 'Completed', phase: null,
          summary: 'Reviewed Project', hasObservedActivity: true, hasDeclaredScope: true,
          startedAtUtc: '2026-08-17T00:00:00Z', updatedAtUtc: '2026-08-17T00:00:03Z',
        }],
        nodes: [
          {
            agentId: 'agent-sub', nodeId: 'group', evidence: 'Observed', isDirect: true,
            updatedAtUtc: '2026-08-17T00:00:01Z', paths: ['Old.cs'],
          },
          {
            agentId: 'agent-sub', nodeId: 'project', evidence: 'Declared', isDirect: true,
            updatedAtUtc: '2026-08-17T00:00:02Z', paths: ['Current.cs'],
          },
        ],
        recentEdits: [], unmappedPaths: [], error: null,
      },
    })

    expect(decorations.get('group')).toEqual([
      expect.objectContaining({
        state: 'Worked',
        evidence: 'Observed',
        agent: expect.objectContaining({ isSubagent: true }),
      }),
    ])
    expect(decorations.get('project')).toEqual([
      expect.objectContaining({
        state: 'Worked',
        evidence: 'Declared',
        agent: expect.objectContaining({ isSubagent: true }),
      }),
    ])
  })

  it('removes a redundant inferred dependency when an exact project reference exists', () => {
    const edges = createFlowEdges({
      ...snapshot,
      graph: {
        ...snapshot.graph,
        relations: [
          ...snapshot.graph.relations,
          {
            id: 'project-reference', sourceId: 'project', targetId: 'group',
            kind: 'ProjectReference', weight: 1, evidenceCount: 1, confidence: 'Exact',
          },
        ],
      },
    })

    expect(edges.some((edge) => edge.id === 'dependency')).toBe(false)
    const projectReference = edges.find((edge) => edge.id === 'project-reference')
    expect(projectReference).toMatchObject({ ariaLabel: 'Project references Group' })
    expect(projectReference).not.toHaveProperty('label')
  })

  it('styles a NuGet reference as an external read-only package edge', () => {
    const edges = createFlowEdges({
      ...snapshot,
      graph: {
        nodes: [
          ...snapshot.graph.nodes,
          {
            id: 'package', kind: 'Project', name: 'Example.Package', parentId: 'group',
            qualifiedName: 'Example.Package', description: 'NuGet package · read-only',
            categoryId: 'external-package', tags: ['project-type:nuget-package', 'package:nuget', 'source:external', 'read-only'],
            sourceLocations: [],
          },
        ],
        relations: [{
          id: 'package-reference', sourceId: 'project', targetId: 'package',
          kind: 'ProjectReference', weight: 3, evidenceCount: 1, confidence: 'Exact',
        }],
      },
    })

    expect(edges.find((edge) => edge.id === 'package-reference')).toMatchObject({
      source: 'project',
      target: 'package',
      className: 'semantic-edge relation-projectreference external-package-edge',
    })
  })

  it('keeps distinctive inheritance semantics without crowding the canvas', () => {
    const edges = createFlowEdges({
      ...snapshot,
      graph: {
        ...snapshot.graph,
        relations: [{
          id: 'inheritance', sourceId: 'project', targetId: 'group', kind: 'Inherits',
          weight: 1, evidenceCount: 1, confidence: 'Exact',
        }],
      },
    })

    const inheritance = edges.find((edge) => edge.id === 'inheritance')
    expect(inheritance).toMatchObject({
      ariaLabel: 'Project inherits Group',
      className: 'semantic-edge relation-inherits',
    })
    expect(inheritance).not.toHaveProperty('label')
  })

  it('renders an inferred HTTP integration as a distinct consumer-to-provider edge', () => {
    const edges = createFlowEdges({
      ...snapshot,
      graph: {
        ...snapshot.graph,
        relations: [{
          id: 'http-api', sourceId: 'project', targetId: 'group', kind: 'HttpApi',
          weight: 4, evidenceCount: 2, confidence: 'Inferred',
        }],
      },
    })

    expect(edges.find((edge) => edge.id === 'http-api')).toMatchObject({
      source: 'project',
      target: 'group',
      ariaLabel: 'Project uses HTTP API from Group',
      className: 'semantic-edge relation-httpapi',
      data: { confidence: 'Inferred', evidenceCount: 2, relationKind: 'HttpApi' },
    })
  })

  it('formats compound node kinds for people', () => {
    expect(formatNodeKind('ArchitectureGroup')).toBe('Architecture area')
    expect(formatNodeKind('AbstractClass')).toBe('Abstract class')
  })
})
