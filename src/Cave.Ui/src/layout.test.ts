import { describe, expect, it } from 'vitest'
import { architectureNodeDimensions, ElkLayoutEngine } from './layout'
import type { ArchitectureNode, ArchitectureSnapshot } from './types'

const node = (id: string): ArchitectureNode => ({
  id,
  kind: 'Project',
  parentId: null,
  name: `Project ${id}`,
  qualifiedName: id,
  description: `Long descriptions must not change the measured card width for ${id}.`,
  categoryId: 'project',
  tags: [],
  sourceLocations: [],
})

const snapshot: ArchitectureSnapshot = {
  metadata: {
    workspaceName: 'Layout test', providerId: 'test', sourceKind: 'Sample',
    isLive: false, generatedAtUtc: '2026-08-17T00:00:00Z',
  },
  graph: {
    nodes: [node('c'), node('a'), node('b')],
    relations: [
      {
        id: 'a-b', sourceId: 'a', targetId: 'b', kind: 'DependsOn',
        weight: 1, evidenceCount: 1, confidence: 'Exact',
      },
      {
        id: 'b-c', sourceId: 'b', targetId: 'c', kind: 'DependsOn',
        weight: 1, evidenceCount: 1, confidence: 'Exact',
      },
    ],
  },
  git: {
    status: 'Ready',
    baseline: { kind: 'Upstream', reference: 'origin/main', resolvedSha: 'abc123' },
    worktree: null,
    files: [],
    nodes: [{
      nodeId: 'a', kind: 'Modified', additions: 2, deletions: 1,
      changedFiles: 1, paths: ['a.cs'],
    }],
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

describe('ELK layout', () => {
  it('shares explicit card dimensions with React Flow and keeps nodes apart', async () => {
    const layout = await new ElkLayoutEngine().layout(snapshot, 'Projects')
    const dimensions = architectureNodeDimensions(snapshot.graph.nodes[0])

    expect(dimensions).toEqual({ width: 236, height: 148 })
    expect(layout.nodes.every((candidate) => (
      candidate.width === dimensions.width
      && candidate.height === dimensions.height
      && candidate.style?.width === dimensions.width
      && candidate.style?.height === dimensions.height
      && candidate.handles?.some((handle) => (
        handle.type === 'target'
        && handle.position === 'left'
        && handle.x === 0
        && handle.y === dimensions.height / 2
      )) === true
      && candidate.handles?.some((handle) => (
        handle.type === 'source'
        && handle.position === 'right'
        && handle.x === dimensions.width
        && handle.y === dimensions.height / 2
      )) === true
    ))).toBe(true)
    expect(layout.nodes.find((candidate) => candidate.id === 'a')?.data.gitDelta)
      .toEqual(expect.objectContaining({ additions: 2, deletions: 1 }))
    expect(layout.edges.every((edge) => (
      edge.type === 'architectureRoute'
      && edge.data?.route.sections.every((section) => section.length >= 2) === true
    ))).toBe(true)

    for (let leftIndex = 0; leftIndex < layout.nodes.length; leftIndex += 1) {
      for (let rightIndex = leftIndex + 1; rightIndex < layout.nodes.length; rightIndex += 1) {
        const left = layout.nodes[leftIndex]
        const right = layout.nodes[rightIndex]
        const separated = (
          left.position.x + dimensions.width <= right.position.x
          || right.position.x + dimensions.width <= left.position.x
          || left.position.y + dimensions.height <= right.position.y
          || right.position.y + dimensions.height <= left.position.y
        )
        expect(separated).toBe(true)
      }
    }

    for (const edge of layout.edges) {
      const route = edge.data?.route
      if (route === undefined) {
        throw new Error(`Missing route data for '${edge.id}'.`)
      }

      const unrelatedNodes = layout.nodes.filter((candidate) => (
        candidate.id !== edge.source && candidate.id !== edge.target
      ))
      for (const section of route.sections) {
        for (let index = 1; index < section.length; index += 1) {
          const start = section[index - 1]
          const end = section[index]
          expect(start.x === end.x || start.y === end.y).toBe(true)
          for (const unrelatedNode of unrelatedNodes) {
            expect(segmentCrossesNodeInterior(start, end, unrelatedNode)).toBe(false)
          }
        }
      }
    }
  })

  it('widens only cards whose balanced two-row title exceeds the base width', () => {
    expect(architectureNodeDimensions(node('a'))).toEqual({ width: 236, height: 148 })
    expect(architectureNodeDimensions({
      ...node('package'),
      name: 'Microsoft.Extensions.Hosting.WindowsServices',
    })).toEqual({ width: 350, height: 148 })
  })

  it('keeps positions stable when provider ordering changes', async () => {
    const engine = new ElkLayoutEngine()
    const first = await engine.layout(snapshot, 'Projects')
    const reversed = await engine.layout({
      ...snapshot,
      graph: {
        nodes: [...snapshot.graph.nodes].reverse(),
        relations: [...snapshot.graph.relations].reverse(),
      },
    }, 'Projects')

    const positions = (layout: typeof first) => Object.fromEntries(
      layout.nodes.map((candidate) => [candidate.id, candidate.position]),
    )
    expect(positions(reversed)).toEqual(positions(first))
  })

  it('offers a tighter spacing mode through the same layout engine', async () => {
    const engine = new ElkLayoutEngine()
    const normal = await engine.layout(snapshot, 'Projects', { density: 'Normal' })
    const tight = await engine.layout(snapshot, 'Projects', { density: 'Tight' })
    const bounds = (layout: typeof normal) => ({
      width: Math.max(...layout.nodes.map((candidate) => (
        candidate.position.x + Number(candidate.style?.width ?? 0)
      ))) - Math.min(...layout.nodes.map((candidate) => candidate.position.x)),
      height: Math.max(...layout.nodes.map((candidate) => (
        candidate.position.y + Number(candidate.style?.height ?? 0)
      ))) - Math.min(...layout.nodes.map((candidate) => candidate.position.y)),
    })

    expect(bounds(tight).width).toBeLessThan(bounds(normal).width)
    expect(bounds(tight).height).toBeLessThanOrEqual(bounds(normal).height)
    expect(tight.nodes.map((candidate) => candidate.style))
      .toEqual(normal.nodes.map((candidate) => candidate.style))
  })

  it('sizes and decorates revealed children separately from surrounding context', async () => {
    const onToggleExpansion = () => undefined
    const nestedSnapshot: ArchitectureSnapshot = {
      ...snapshot,
      graph: {
        ...snapshot.graph,
        nodes: snapshot.graph.nodes.map((candidate) => candidate.id === 'b'
          ? { ...candidate, kind: 'Namespace', parentId: 'a' }
          : candidate),
      },
    }
    const layout = await new ElkLayoutEngine().layout(nestedSnapshot, 'Projects', {
      hierarchy: {
        expandableNodeIds: new Set(['a']),
        expandedNodeIds: new Set(['a']),
        nestedNodeIds: new Set(['b']),
        dimmedNodeIds: new Set(['c']),
      },
      onToggleExpansion,
    })

    expect(layout.nodes.find((candidate) => candidate.id === 'b')?.style)
      .toMatchObject({ width: 218, height: 136 })
    expect(layout.nodes.find((candidate) => candidate.id === 'a')?.data.hierarchy)
      .toMatchObject({ canExpand: true, isExpanded: true, isNested: false, isDimmed: false })
    expect(layout.nodes.find((candidate) => candidate.id === 'c')?.data.hierarchy.isDimmed)
      .toBe(true)
    expect(layout.nodes.find((candidate) => candidate.id === 'a')?.data.hierarchy.onToggleExpansion)
      .toBe(onToggleExpansion)
    expect(layout.edges.find((edge) => edge.id === 'b-c')?.className)
      .toContain('is-focus-dimmed')
  })
})

function segmentCrossesNodeInterior(
  start: { x: number; y: number },
  end: { x: number; y: number },
  node: Awaited<ReturnType<ElkLayoutEngine['layout']>>['nodes'][number],
): boolean {
  const dimensions = architectureNodeDimensions(node.data.architecture)
  const left = node.position.x
  const right = left + dimensions.width
  const top = node.position.y
  const bottom = top + dimensions.height

  if (start.x === end.x) {
    return start.x > left && start.x < right
      && Math.max(Math.min(start.y, end.y), top) < Math.min(Math.max(start.y, end.y), bottom)
  }

  return start.y > top && start.y < bottom
    && Math.max(Math.min(start.x, end.x), left) < Math.min(Math.max(start.x, end.x), right)
}
