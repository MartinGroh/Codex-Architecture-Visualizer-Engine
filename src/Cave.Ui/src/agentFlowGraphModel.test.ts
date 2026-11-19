import { describe, expect, it } from 'vitest'
import { getSmoothStepPath, Position } from '@xyflow/react'
import {
  agentFlowTopology, flowOperation, layoutAgentFlow, type AgentFlowLayout, type AgentFlowBubbleGeometry,
} from './agentFlowGraphModel'
import type { MachineActivityLane } from './machineActivityTimeline'
const lane = (id: string, parent: string | null = null): MachineActivityLane => ({ agent: {
  agentId: id, parentAgentId: parent, agentType: 'default', isSubagent: parent !== null,
  state: 'Active', phase: 'Editing', summary: null, hasObservedActivity: true, hasDeclaredScope: false,
  startedAtUtc: '2026-11-18T10:00:00Z', updatedAtUtc: '2026-11-18T10:01:00Z',
}, active: true, durationMs: 60000, durationPercent: 100, milestones: [], completionSummary: null })

function expectClearNodes({ nodes }: AgentFlowLayout) {
  for (const node of nodes) {
    expect(Number.isFinite(node.position.x)).toBe(true)
    expect(Number.isFinite(node.position.y)).toBe(true)
    expect(node.width).toBeGreaterThan(0)
    expect(node.height).toBeGreaterThan(0)
  }
  for (const a of nodes) for (const b of nodes) if (a.id !== b.id) {
    expect(a.position.x + a.width! <= b.position.x || b.position.x + b.width! <= a.position.x
      || a.position.y + a.height! <= b.position.y || b.position.y + b.height! <= a.position.y,
    `${a.id} and ${b.id}`).toBe(true)
  }
}

describe('agent flow graph', () => {
  it('uses only observed parents, not guessed delegation', () => {
    expect(agentFlowTopology([lane('main'), lane('child', 'main'), lane('orphan', 'missing'), lane('self', 'self')])).toEqual([
      { id: 'child', parent: 'main' }, { id: 'main', parent: 'project-hub' },
      { id: 'orphan', parent: 'project-hub' }, { id: 'self', parent: 'project-hub' },
    ])
  })
  it('gives completed work a terminal treatment', () => {
    expect(flowOperation({ ...lane('done'), agent: { ...lane('done').agent, state: 'Completed' }, active: false })).toBe('Completed')
    expect(flowOperation({ ...lane('stale'), active: false })).toBe('Idle')
    expect(flowOperation(lane('working'))).toBe('Editing')
  })
  it('retains members in a cyclic ancestry and keeps legitimate descendant links', async () => {
    const lanes = [lane('a-leaf', 'b-cycle'), lane('b-cycle', 'c-cycle'), lane('c-cycle', 'b-cycle')]
    const topology = agentFlowTopology(lanes)
    expect(topology).toEqual([
      { id: 'a-leaf', parent: 'b-cycle' }, { id: 'b-cycle', parent: 'project-hub' },
      { id: 'c-cycle', parent: 'project-hub' },
    ])
    const result = await layoutAgentFlow(topology, 'mindmap')
    expectClearNodes(result)
    expect(result.edges.map(({ source, target, className }) => [source, target, className])).toEqual([
      ['b-cycle', 'a-leaf', 'flow2-delegation'],
      ['project-hub', 'b-cycle', 'flow2-membership'],
      ['project-hub', 'c-cycle', 'flow2-membership'],
    ])
  })
  it('lays out each parent before its children without overlapping nodes', async () => {
    const result = await layoutAgentFlow(agentFlowTopology([lane('main'), lane('child', 'main'), lane('second', 'main'), lane('nested', 'child'), lane('other')]))
    expect(result.nodes).toHaveLength(6)
    expect(result.edges).toHaveLength(5)
    const byId = new Map(result.nodes.map((node) => [node.id, node]))
    for (const edge of result.edges) {
      const source = byId.get(edge.source)!
      const target = byId.get(edge.target)!
      expect(source.position.x + source.width!).toBeLessThan(target.position.x)
    }
    expectClearNodes(result)
  })
  it('provides fixed handles for both relationship types before browser measurement', async () => {
    const { nodes, edges } = await layoutAgentFlow(agentFlowTopology([lane('main'), lane('child', 'main')]))
    expect(edges).toMatchObject([
      { source: 'main', target: 'child', className: 'flow2-delegation' },
      { source: 'project-hub', target: 'main', className: 'flow2-membership' },
    ])
    for (const node of nodes) {
      expect(node.handles).toEqual([
        { type: 'target', position: Position.Left, x: 0, y: node.height! / 2 },
        { type: 'source', position: Position.Right, x: node.width! - 1, y: node.height! / 2 },
      ])
    }
  })

  const lanes = [lane('main'), lane('a', 'main'), lane('b', 'main'), lane('c', 'main'), lane('nested', 'b')]
  const work: AgentFlowBubbleGeometry[] = [
    { id: 'artifact-image', parent: 'a', kind: 'image' }, { id: 'artifact-text', parent: 'b', kind: 'text' },
    { id: 'artifact-diagram', parent: 'nested', kind: 'diagram' }, { id: 'work-main', parent: 'main', kind: 'task' },
  ]

  it('fits tree work beneath its owner and routes delegation below sibling work', async () => {
    const treeWork: AgentFlowBubbleGeometry[] = [
      ...work,
      { id: 'main-note', parent: 'main', kind: 'activity' },
      { id: 'main-image', parent: 'main', kind: 'image' },
      { id: 'branch-task', parent: 'a', kind: 'task' },
      { id: 'branch-note', parent: 'a', kind: 'activity' },
    ]
    const result = await layoutAgentFlow(agentFlowTopology(lanes), 'tree', treeWork)
    expectClearNodes(result)
    const byId = new Map(result.nodes.map((node) => [node.id, node]))
    for (const edge of result.edges) {
      const { source, target } = edge
      const parent = byId.get(source)!
      const child = byId.get(target)!
      expect(parent.position.y + parent.height!).toBeLessThan(child.position.y)
      expect(parent.sourcePosition).toBe(Position.Bottom)
      const sourceX = parent.position.x + parent.width! / 2
      const sourceY = parent.position.y + parent.height!
      const work = edge.className === 'flow2-work'
      let targetX = child.position.x + child.width! / 2
      let targetY = child.position.y
      if (work) {
        const left = child.position.x < sourceX
        expect(child.targetPosition).toBe(left ? Position.Right : Position.Left)
        targetX = left ? child.position.x + child.width! : child.position.x
        targetY += child.height! / 2
      } else {
        expect(child.targetPosition).toBe(Position.Top)
        expect(child.handles?.find(({ type }) => type === 'target')).toMatchObject({ x: child.width! / 2, y: 0 })
      }
      if (edge.type !== 'smoothstep') throw new Error('Expected a tree route')
      const [path] = getSmoothStepPath({ ...edge.pathOptions, sourceX, sourceY, targetX, targetY, borderRadius: 0,
        sourcePosition: parent.sourcePosition, targetPosition: child.targetPosition })
      const coordinates = (path.match(/-?\d+(?:\.\d+)?(?:e[+-]?\d+)?/gi) ?? []).map(Number)
      expect(coordinates.length).toBeGreaterThan(2)
      // Even allowing eight pixels for rounded turns, every segment clears other work.
      const obstacles = result.nodes.filter(({ id }) => id !== source && id !== target)
      for (let index = 2; index < coordinates.length; index += 2) {
        const [x1, y1, x2, y2] = coordinates.slice(index - 2, index + 2)
        for (const { id, position, width, height } of obstacles) {
          const crossesX = Math.max(x1, x2) > position.x - 8 && Math.min(x1, x2) < position.x + width! + 8
          const crossesY = Math.max(y1, y2) > position.y - 8 && Math.min(y1, y2) < position.y + height! + 8
          expect(crossesX && crossesY, `${source} → ${target} crosses ${id}`).toBe(false)
        }
      }
    }
    expect(byId.get('artifact-image')?.type).toBe('bubble')
    expect(byId.get('artifact-image')?.height).toBeGreaterThan(byId.get('artifact-text')!.height!)
    expect(byId.get('artifact-text')?.height).toBe(72)
    expect(byId.get('a')?.position.y).toBe(byId.get('c')?.position.y)
    expect(new Set(treeWork.filter(({ parent }) => parent === 'main').map(({ id }) => byId.get(id)?.targetPosition)).size).toBe(2)
    const ownership = result.edges.find(({ target }) => target === 'artifact-diagram')
    expect(ownership).toMatchObject({ source: 'nested', className: 'flow2-work', markerEnd: expect.anything() })
  })

  it('keeps a wide set of outputs compact without expanding the agent generation', async () => {
    const ownerIds = ['main', 'a', 'b', 'c']
    const topology = agentFlowTopology(ownerIds.map((id) => lane(id, id === 'main' ? null : 'main')))
    const workCounts = [5, 5, 4, 3]
    const mediaByOwner: Partial<Record<string, AgentFlowBubbleGeometry['kind']>> = { a: 'diagram', b: 'image' }
    const work = ownerIds.flatMap((parent, index) => Array.from({ length: workCounts[index] }, (_, i): AgentFlowBubbleGeometry =>
      ({ id: `${parent}:work-${i}`, parent, kind: i === 0 ? mediaByOwner[parent] ?? 'text' : 'text' })))
    const result = await layoutAgentFlow(topology, 'tree', work)
    expectClearNodes(result)
    expect(result.nodes).toHaveLength(22)
    const left = Math.min(...result.nodes.map(({ position }) => position.x))
    const right = Math.max(...result.nodes.map(({ position, width }) => position.x + width!))
    const top = Math.min(...result.nodes.map(({ position }) => position.y))
    const bottom = Math.max(...result.nodes.map(({ position, height }) => position.y + height!))
    expect(right - left).toBeLessThan(1600)
    expect(bottom - top).toBeLessThan(900)
  })

  it('flanks a sole main agent with outward subtrees and puts the project on its own trunk', async () => {
    const result = await layoutAgentFlow(agentFlowTopology(lanes), 'mindmap', work)
    expectClearNodes(result)
    const byId = new Map(result.nodes.map((node) => [node.id, node]))
    const hub = byId.get('project-hub')!
    const main = byId.get('main')!
    expect(hub.position.x + hub.width! / 2).toBe(main.position.x + main.width! / 2)
    expect(hub.position.y + hub.height!).toBeLessThan(main.position.y)
    expect(hub.sourcePosition).toBe(Position.Bottom)
    expect(main.targetPosition).toBe(Position.Top)
    expect(main.data.alternateSourcePosition).toBe(Position.Left)
    const subagents = result.edges.filter(({ className, source }) => className === 'flow2-delegation' && source === main.id)
    expect(subagents.some(({ sourceHandle }) => sourceHandle === 'alternate')).toBe(true)
    expect(subagents.some(({ sourceHandle }) => sourceHandle === undefined)).toBe(true)
    for (const edge of result.edges.filter(({ source }) => source !== hub.id)) {
      const source = byId.get(edge.source)!
      const target = byId.get(edge.target)!
      const sourceHandle = source.handles?.find(({ type, id }) => type === 'source' && id === edge.sourceHandle)
      expect(edge.type).toBe('default')
      if (target.position.x < source.position.x) {
        expect(target.position.x + target.width!).toBeLessThan(source.position.x)
        expect(sourceHandle?.position).toBe(Position.Left)
        expect(target.targetPosition).toBe(Position.Right)
      } else {
        expect(source.position.x + source.width!).toBeLessThan(target.position.x)
        expect(sourceHandle?.position).toBe(Position.Right)
        expect(target.targetPosition).toBe(Position.Left)
      }
      expect(edge.markerEnd).toBeDefined()
    }
  })

  it.each(['mindmap', 'tree'] as const)('preserves %s geometry across feed ordering and changing live text', async (mode) => {
    const topology = agentFlowTopology(lanes)
    const updatedLanes: MachineActivityLane[] = [...lanes].reverse().map((item) => ({
      ...item, agent: { ...item.agent, phase: 'Thinking', summary: 'New live summary' },
    }))
    expect(agentFlowTopology(updatedLanes)).toEqual(topology)
    const original = await layoutAgentFlow(topology, mode, work)
    expect(await layoutAgentFlow(agentFlowTopology(updatedLanes), mode, [...work].reverse())).toEqual(original)
  })

  it('keeps only work for a visible owner and leaves the empty project usable', async () => {
    const result = await layoutAgentFlow(agentFlowTopology([lane('owner')]), 'flow', [
      { id: 'fake', parent: 'unavailable', kind: 'text' }, { id: 'owner', parent: 'owner', kind: 'diagram' },
      { id: 'work', parent: 'owner', kind: 'activity' },
    ])
    expect(result.nodes.map(({ id }) => id)).toEqual(['project-hub', 'owner', 'work'])
    expect(result.edges.find(({ target }) => target === 'work')).toMatchObject({ source: 'owner', className: 'flow2-work' })
    const empty = await layoutAgentFlow(agentFlowTopology([]), 'mindmap')
    expect(empty.nodes.map(({ id }) => id)).toEqual(['project-hub'])
    expect(empty.edges).toEqual([])
  })
})
