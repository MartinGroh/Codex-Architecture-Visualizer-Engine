import { describe, expect, it } from 'vitest'
import { focusActivitySnapshot } from './activityFocus'
import type { ArchitectureNode, ArchitectureSnapshot } from './types'

const node = (
  id: string,
  kind: ArchitectureNode['kind'],
  parentId: string | null,
): ArchitectureNode => ({
  id,
  kind,
  parentId,
  name: id,
  qualifiedName: id,
  description: null,
  categoryId: null,
  tags: [],
  sourceLocations: [],
})

function snapshot(): ArchitectureSnapshot {
  return {
    metadata: {
      workspaceName: 'Mobile test',
      providerId: 'test',
      sourceKind: 'Sample',
      isLive: true,
      generatedAtUtc: '2026-08-23T10:00:00Z',
    },
    graph: {
      nodes: [
        node('project-a', 'Project', null),
        node('class-a', 'Class', 'project-a'),
        node('project-b', 'Project', null),
      ],
      relations: [
        {
          id: 'visible-relation',
          sourceId: 'class-a',
          targetId: 'project-b',
          kind: 'DependsOn',
          weight: 1,
          evidenceCount: 1,
          confidence: 'Exact',
        },
        {
          id: 'hidden-relation',
          sourceId: 'project-a',
          targetId: 'project-b',
          kind: 'DependsOn',
          weight: 1,
          evidenceCount: 1,
          confidence: 'Exact',
        },
      ],
    },
    git: {
      status: 'Ready',
      baseline: null,
      worktree: null,
      files: [],
      nodes: [
        { nodeId: 'class-a', kind: 'Modified', additions: 1, deletions: 0, changedFiles: 1, paths: ['a.cs'] },
        { nodeId: 'project-a', kind: 'Modified', additions: 1, deletions: 0, changedFiles: 1, paths: ['a.csproj'] },
      ],
      unmappedFiles: [],
      error: null,
    },
    activity: {
      agents: [],
      nodes: [
        { agentId: 'agent', nodeId: 'class-a', evidence: 'Observed', isDirect: true, updatedAtUtc: '2026-08-23T10:00:00Z', paths: ['a.cs'] },
        { agentId: 'agent', nodeId: 'project-a', evidence: 'Observed', isDirect: false, updatedAtUtc: '2026-08-23T10:00:00Z', paths: ['a.csproj'] },
      ],
      recentEdits: [{
        agentId: 'agent',
        filePath: 'a.cs',
        nodeIds: ['class-a', 'project-a'],
        observedAtUtc: '2026-08-23T10:00:00Z',
      }],
      latestInstruction: null,
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

describe('active-work focus', () => {
  it('keeps only the active scope and relations fully contained by it', () => {
    const focused = focusActivitySnapshot(
      snapshot(),
      new Set(['class-a', 'project-b', 'missing']),
    )

    expect(focused.graph.nodes.map((item) => item.id)).toEqual(['class-a', 'project-b'])
    expect(focused.graph.nodes[0].parentId).toBeNull()
    expect(focused.graph.relations.map((relation) => relation.id)).toEqual(['visible-relation'])
    expect(focused.git.nodes.map((delta) => delta.nodeId)).toEqual(['class-a'])
    expect(focused.activity.nodes.map((activity) => activity.nodeId)).toEqual(['class-a'])
    expect(focused.activity.recentEdits[0].nodeIds).toEqual(['class-a'])
  })

  it('returns an explicit empty graph when no active location is mapped', () => {
    const source = snapshot()
    const focused = focusActivitySnapshot(source, new Set())

    expect(focused.graph).toEqual({ nodes: [], relations: [] })
    expect(focused.metadata).toBe(source.metadata)
    expect(focused.conversation).toBe(source.conversation)
  })
})
