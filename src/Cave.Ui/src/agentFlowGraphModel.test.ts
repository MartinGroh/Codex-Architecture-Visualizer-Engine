import { describe, expect, it } from 'vitest'
import { Position } from '@xyflow/react'
import { agentFlowTopology, flowOperation, layoutAgentFlow } from './agentFlowGraphModel'
import type { MachineActivityLane } from './machineActivityTimeline'
const lane = (id: string, parent: string | null = null): MachineActivityLane => ({ agent: {
  agentId: id, parentAgentId: parent, agentType: 'default', isSubagent: parent !== null,
  state: 'Active', phase: 'Editing', summary: null, hasObservedActivity: true, hasDeclaredScope: false,
  startedAtUtc: '2026-11-18T10:00:00Z', updatedAtUtc: '2026-11-18T10:01:00Z',
}, active: true, durationMs: 60000, durationPercent: 100, milestones: [], completionSummary: null })
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
    for (const node of result.nodes) {
      expect(Number.isFinite(node.position.x)).toBe(true)
      expect(Number.isFinite(node.position.y)).toBe(true)
    }
    for (const a of result.nodes) for (const b of result.nodes) if(a.id !== b.id) {
      expect(a.position.x + a.width! <= b.position.x || b.position.x + b.width! <= a.position.x
        || a.position.y + a.height! <= b.position.y || b.position.y + b.height! <= a.position.y).toBe(true)
    }
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
})
