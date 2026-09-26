import { describe, expect, it } from 'vitest'
import {
  collapseHierarchyExpansion,
  nextAutoProjectionLevel,
  createHierarchyPresentation,
  projectArchitecture,
  projectionLevelForZoom,
  toggleHierarchyExpansion,
} from './projection'
import type { ArchitectureNode, ArchitectureSnapshot } from './types'

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

const snapshot: ArchitectureSnapshot = {
  metadata: {
    workspaceName: 'Projection test',
    providerId: 'test',
    sourceKind: 'Sample',
    isLive: false,
    generatedAtUtc: '2026-08-17T00:00:00Z',
  },
  graph: {
    nodes: [
      node('group', 'ArchitectureGroup', null),
      node('project-a', 'Project', 'group'),
      node('namespace-a', 'Namespace', 'project-a'),
      node('class-a', 'Class', 'namespace-a'),
      node('namespace-a2', 'Namespace', 'project-a'),
      node('class-a2', 'Interface', 'namespace-a2'),
      node('project-b', 'Project', 'group', ['dotnet', 'project-type:test']),
      node('class-b', 'Class', 'project-b'),
    ],
    relations: [
      {
        id: 'class-dependency',
        sourceId: 'class-a',
        targetId: 'class-b',
        kind: 'DependsOn',
        weight: 2,
        evidenceCount: 3,
        confidence: 'Inferred',
      },
      {
        id: 'project-reference',
        sourceId: 'project-a',
        targetId: 'project-b',
        kind: 'ProjectReference',
        weight: 4,
        evidenceCount: 1,
        confidence: 'Exact',
      },
    ],
  },
  git: {
    status: 'Ready',
    baseline: { kind: 'Upstream', reference: 'origin/main', resolvedSha: 'abc123' },
    worktree: null,
    files: [],
    nodes: [{
      nodeId: 'project-a', kind: 'Modified', additions: 2, deletions: 1,
      changedFiles: 1, paths: ['src/App.ts'],
    }],
    unmappedFiles: [],
    error: null,
  },
  activity: {
    latestInstruction: null,
    agents: [{
      agentId: 'agent-1', agentType: 'main', isSubagent: false, state: 'Active', phase: 'Editing',
      summary: 'Editing class-a', hasObservedActivity: true, hasDeclaredScope: false,
      startedAtUtc: '2026-08-17T00:00:00Z', updatedAtUtc: '2026-08-17T00:00:01Z',
    }],
    nodes: [{
      agentId: 'agent-1', nodeId: 'class-a', evidence: 'Observed', isDirect: true,
      updatedAtUtc: '2026-08-17T00:00:01Z', paths: ['src/App.ts'],
    }],
    recentEdits: [{
      agentId: 'agent-1', filePath: 'src/App.ts', nodeIds: ['class-a'],
      observedAtUtc: '2026-08-17T00:00:01Z',
    }],
    unmappedPaths: [],
    error: null,
  },
  conversation: {
    sharingEnabled: false,
    status: 'Disabled',
    messages: [],
    control: { sessionId: null, turnId: null, state: 'Unavailable', canSend: false, deliveries: [], error: null },
    error: null,
  },
}

describe('semantic projection', () => {
  it('projects hidden class evidence onto projects when zoomed out', () => {
    const projected = projectArchitecture(snapshot, 'Projects')

    expect(projected.graph.nodes.map((candidate) => candidate.id))
      .toEqual(['project-a', 'project-b'])
    expect(projected.git.nodes.map((delta) => delta.nodeId)).toEqual(['project-a'])
    expect(projected.activity.nodes).toEqual([
      expect.objectContaining({ nodeId: 'project-a', isDirect: false }),
    ])
    expect(projected.activity.recentEdits[0].nodeIds).toEqual(['project-a'])
    expect(projected.graph.relations).toEqual(expect.arrayContaining([
      expect.objectContaining({
        sourceId: 'project-a',
        targetId: 'project-b',
        kind: 'DependsOn',
        weight: 2,
        evidenceCount: 3,
      }),
      expect.objectContaining({
        sourceId: 'project-a',
        targetId: 'project-b',
        kind: 'ProjectReference',
      }),
    ]))
  })

  it('uses namespace nodes while preserving projects as their visible parents', () => {
    const projected = projectArchitecture(snapshot, 'Namespaces')

    expect(projected.graph.nodes.map((candidate) => candidate.id))
      .toEqual(['project-a', 'namespace-a', 'namespace-a2', 'project-b'])
    expect(projected.git.nodes.map((delta) => delta.nodeId)).toEqual(['project-a'])
    expect(projected.graph.nodes.find((candidate) => candidate.id === 'namespace-a')?.parentId)
      .toBe('project-a')
    expect(projected.graph.relations).toContainEqual(expect.objectContaining({
      sourceId: 'namespace-a',
      targetId: 'project-b',
      kind: 'DependsOn',
    }))
  })

  it('shows classes at the innermost level and drops relations with no visible endpoint', () => {
    const projected = projectArchitecture(snapshot, 'Classes')

    expect(projected.graph.nodes.map((candidate) => candidate.id))
      .toEqual(['namespace-a', 'class-a', 'namespace-a2', 'class-a2', 'class-b'])
    expect(projected.git.nodes).toEqual([])
    expect(projected.graph.relations).toHaveLength(1)
    expect(projected.graph.relations[0]).toEqual(expect.objectContaining({
      sourceId: 'class-a',
      targetId: 'class-b',
      kind: 'DependsOn',
    }))
  })

  it('removes test projects, their classes, and their relations when requested', () => {
    const projects = projectArchitecture(snapshot, 'Projects', { hideTests: true })
    const classes = projectArchitecture(snapshot, 'Classes', { hideTests: true })

    expect(projects.graph.nodes.map((candidate) => candidate.id)).toEqual(['project-a'])
    expect(projects.graph.relations).toEqual([])
    expect(classes.graph.nodes.map((candidate) => candidate.id))
      .toEqual(['namespace-a', 'class-a', 'namespace-a2', 'class-a2'])
    expect(classes.graph.relations).toEqual([])
  })

  it('keeps one read-only package node visible at every semantic level', () => {
    const packageSnapshot: ArchitectureSnapshot = {
      ...snapshot,
      graph: {
        nodes: [
          ...snapshot.graph.nodes,
          node('package:nuget:Example.Package', 'Project', 'group', [
            'project-type:nuget-package', 'package:nuget', 'source:external', 'read-only',
          ]),
        ],
        relations: [
          ...snapshot.graph.relations,
          {
            id: 'package-reference', sourceId: 'project-a', targetId: 'package:nuget:Example.Package',
            kind: 'ProjectReference', weight: 3, evidenceCount: 1, confidence: 'Exact',
          },
        ],
      },
    }

    for (const level of ['Projects', 'Namespaces', 'Classes'] as const) {
      const projected = projectArchitecture(packageSnapshot, level)
      expect(projected.graph.nodes.filter((candidate) => candidate.tags.includes('package:nuget')))
        .toEqual([expect.objectContaining({ id: 'package:nuget:Example.Package', kind: 'Project' })])

      const presentation = createHierarchyPresentation(
        packageSnapshot,
        projected,
        level,
        collapseHierarchyExpansion(),
      )
      expect(presentation.nestedNodeIds.has('package:nuget:Example.Package')).toBe(false)
    }
  })

  it('hides all read-only external packages, their descendants, and incident routes', () => {
    const packageSnapshot: ArchitectureSnapshot = {
      ...snapshot,
      graph: {
        nodes: [
          ...snapshot.graph.nodes,
          node('package:nuget:Example.Package', 'Project', 'group', [
            'project-type:nuget-package', 'package:nuget', 'source:external', 'read-only',
          ]),
          node('package:unity:com.unity.inputsystem', 'Project', 'group', [
            'project-type:unity-package', 'package:unity', 'source:external', 'read-only',
          ]),
          node('namespace:unity-input', 'Namespace', 'package:unity:com.unity.inputsystem'),
        ],
        relations: [
          ...snapshot.graph.relations,
          {
            id: 'package-reference', sourceId: 'project-a', targetId: 'package:nuget:Example.Package',
            kind: 'ProjectReference', weight: 3, evidenceCount: 1, confidence: 'Exact',
          },
          {
            id: 'unity-reference', sourceId: 'project-a', targetId: 'package:unity:com.unity.inputsystem',
            kind: 'ProjectReference', weight: 2, evidenceCount: 1, confidence: 'Exact',
          },
        ],
      },
    }

    for (const level of ['Projects', 'Namespaces', 'Classes'] as const) {
      const projected = projectArchitecture(packageSnapshot, level, { hideExternalPackages: true })
      expect(projected.graph.nodes.some((candidate) => (
        candidate.tags.includes('package:nuget')
        || candidate.tags.includes('package:unity')
        || candidate.id === 'namespace:unity-input'
      ))).toBe(false)
      expect(projected.graph.relations.some((relation) => (
        relation.sourceId === 'package:nuget:Example.Package'
        || relation.targetId === 'package:nuget:Example.Package'
        || relation.sourceId === 'package:unity:com.unity.inputsystem'
        || relation.targetId === 'package:unity:com.unity.inputsystem'
      ))).toBe(false)
    }

    expect(packageSnapshot.graph.nodes.some((candidate) => candidate.tags.includes('package:nuget')))
      .toBe(true)
  })

  it('hides packages referenced only by hidden test projects', () => {
    const packageSnapshot: ArchitectureSnapshot = {
      ...snapshot,
      graph: {
        nodes: [
          ...snapshot.graph.nodes,
          node('package:nuget:Test.Package', 'Project', 'group', [
            'project-type:nuget-package', 'package:nuget', 'source:external', 'read-only',
          ]),
        ],
        relations: [
          ...snapshot.graph.relations,
          {
            id: 'test-package-reference', sourceId: 'project-b', targetId: 'package:nuget:Test.Package',
            kind: 'ProjectReference', weight: 3, evidenceCount: 1, confidence: 'Exact',
          },
        ],
      },
    }

    const projected = projectArchitecture(packageSnapshot, 'Classes', { hideTests: true })
    expect(projected.graph.nodes.some((candidate) => candidate.id === 'package:nuget:Test.Package'))
      .toBe(false)
  })

  it('reveals namespaces and then classes along one focused hierarchy branch', () => {
    const projectExpansion = toggleHierarchyExpansion(snapshot, {
      expandedNodeIds: new Set(),
      focusNodeId: null,
    }, 'project-a')
    const projects = projectArchitecture(snapshot, 'Projects', {
      expandedNodeIds: projectExpansion.expandedNodeIds,
    })

    expect(projects.graph.nodes.map((candidate) => candidate.id))
      .toEqual(['project-a', 'namespace-a', 'namespace-a2', 'project-b'])

    const namespaceExpansion = toggleHierarchyExpansion(
      snapshot,
      projectExpansion,
      'namespace-a',
    )
    const classes = projectArchitecture(snapshot, 'Projects', {
      expandedNodeIds: namespaceExpansion.expandedNodeIds,
    })
    const presentation = createHierarchyPresentation(
      snapshot,
      classes,
      'Projects',
      namespaceExpansion,
    )

    expect(classes.graph.nodes.map((candidate) => candidate.id))
      .toEqual(['project-a', 'namespace-a', 'class-a', 'namespace-a2', 'project-b'])
    expect(presentation.nestedNodeIds).toEqual(new Set(['namespace-a', 'class-a', 'namespace-a2']))
    expect(presentation.dimmedNodeIds).toEqual(new Set(['namespace-a2', 'project-b']))
    expect(presentation.expandableNodeIds)
      .toEqual(new Set(['project-a', 'namespace-a', 'namespace-a2']))
  })

  it('selectively reveals one exact leaf and closes its visible ancestor path', () => {
    const projected = projectArchitecture(snapshot, 'Projects', {
      revealedNodeIds: new Set(['class-a']),
    })

    expect(projected.graph.nodes.map((candidate) => candidate.id))
      .toEqual(['project-a', 'namespace-a', 'class-a', 'project-b'])
    expect(projected.graph.nodes.find((candidate) => candidate.id === 'namespace-a')?.parentId)
      .toBe('project-a')
    expect(projected.graph.nodes.find((candidate) => candidate.id === 'class-a')?.parentId)
      .toBe('namespace-a')
    expect(projected.graph.nodes.some((candidate) => candidate.id === 'class-a2')).toBe(false)
    expect(projected.graph.relations).toContainEqual(expect.objectContaining({
      sourceId: 'class-a',
      targetId: 'project-b',
      kind: 'DependsOn',
    }))
  })

  it('keeps the union of multiple focus branches undimmed', () => {
    const expansion = {
      expandedNodeIds: new Set(['project-a', 'namespace-a', 'namespace-a2']),
      focusNodeId: null,
    }
    const projected = projectArchitecture(snapshot, 'Projects', {
      revealedNodeIds: new Set(['class-a', 'class-a2']),
    })
    const presentation = createHierarchyPresentation(
      snapshot,
      projected,
      'Projects',
      expansion,
      new Set(['class-a', 'class-a2']),
    )

    expect(presentation.dimmedNodeIds).toEqual(new Set(['project-b']))
    expect(presentation.nestedNodeIds).toEqual(new Set([
      'namespace-a', 'class-a', 'namespace-a2', 'class-a2',
    ]))
  })

  it('collapses every open hierarchy branch and clears its focus', () => {
    const collapsed = collapseHierarchyExpansion()

    expect(collapsed.expandedNodeIds).toEqual(new Set())
    expect(collapsed.focusNodeId).toBeNull()
  })

  it('uses hysteresis around automatic zoom thresholds', () => {
    expect(projectionLevelForZoom(0.4)).toBe('Projects')
    expect(nextAutoProjectionLevel(0.73, 'Projects')).toBe('Namespaces')
    expect(nextAutoProjectionLevel(0.68, 'Namespaces')).toBe('Namespaces')
    expect(nextAutoProjectionLevel(1.19, 'Namespaces')).toBe('Classes')
    expect(nextAutoProjectionLevel(1.0, 'Classes')).toBe('Classes')
    expect(nextAutoProjectionLevel(0.91, 'Classes')).toBe('Namespaces')
  })
})
