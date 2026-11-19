import ELK from 'elkjs/lib/elk.bundled.js'
import { Position, type Edge, type Node } from '@xyflow/react'
import type { MachineActivityLane } from './machineActivityTimeline'
import type { AgentActivityPhase } from './types'

export type FlowOperation = AgentActivityPhase | 'Completed' | 'Idle' | 'Planned'
export interface AgentFlowNodeData extends Record<string, unknown> {
  name: string
  lane?: MachineActivityLane
}
export type AgentFlowNode = Node<AgentFlowNodeData, 'hub' | 'agent'>
export interface AgentFlowLayout { nodes: AgentFlowNode[]; edges: Edge[] }
const elk = new ELK()
const hubSize = { width: 220, height: 140 }
const agentSize = { width: 240, height: 150 }

/** Presentation-only topology. A project link means membership, not delegation. */
export function agentFlowTopology(lanes: MachineActivityLane[]) {
  const ids = new Set(lanes.map((lane) => lane.agent.agentId))
  return lanes.map(({ agent }) => ({ id: agent.agentId,
    parent: agent.parentAgentId && agent.parentAgentId !== agent.agentId && ids.has(agent.parentAgentId)
      ? agent.parentAgentId : 'project-hub',
  })).sort((a, b) => a.id.localeCompare(b.id))
}

/** ELK owns radial positioning; state changes do not alter the topology. */
export async function layoutAgentFlow(topology: ReturnType<typeof agentFlowTopology>): Promise<AgentFlowLayout> {
  // Layout is a project-centered star; rendered relationships retain their actual evidence.
  const links = topology.map(({ id }) => ({ id: `link:${id}`, sources: ['project-hub'], targets: [id] }))
  const result = await elk.layout({ id: 'flow', layoutOptions: {
    'elk.algorithm': 'radial', 'elk.spacing.nodeNode': '40',
    'elk.radial.radius': '320', 'elk.radial.centerOnRoot': 'true',
  }, children: [{ id: 'project-hub', ...hubSize },
    ...topology.map(({ id }) => ({ id, ...agentSize }))], edges: links })
  return {
    nodes: (result.children ?? []).map((node) => {
      const isHub = node.id === 'project-hub'
      const { width, height } = isHub ? hubSize : agentSize
      return {
        id: node.id,
        type: isHub ? 'hub' : 'agent',
        position: { x: node.x ?? 0, y: node.y ?? 0 },
        data: { name: '' },
        width,
        height,
        // CONSTRAINT: Live data replaces the controlled nodes without resizing them. Supply
        // handle bounds with each node so React Flow keeps edges between measurements.
        handles: [
          { type: 'target', position: Position.Left, x: 0, y: height / 2 },
          { type: 'source', position: Position.Right, x: width - 1, y: height / 2 },
        ],
      }
    }),
    edges: topology.map(({ id, parent }) => ({ id: `link:${id}`, source: parent, target: id,
      type: 'straight', className: parent === 'project-hub' ? 'flow2-membership' : 'flow2-delegation',
      ariaLabel: parent === 'project-hub' ? 'Agent belongs to project' : 'Observed parent to subagent',
    })),
  }
}

export function flowOperation(lane: MachineActivityLane): FlowOperation {
  if (lane.active) return lane.agent.phase ?? 'Working'
  return lane.agent.state === 'Completed' ? 'Completed' : lane.agent.state === 'Planned' ? 'Planned' : 'Idle'
}
