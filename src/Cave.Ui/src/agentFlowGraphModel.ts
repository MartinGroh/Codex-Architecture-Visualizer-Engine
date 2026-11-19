import ELK from 'elkjs/lib/elk.bundled.js'
import { MarkerType, Position, type BuiltInEdge, type Node, type SmoothStepPathOptions, type XYPosition } from '@xyflow/react'
import type { AgentFlowBubble } from './agentFlowBubbles'
import type { MachineActivityLane } from './machineActivityTimeline'
import type { AgentActivityPhase } from './types'

export type FlowOperation = AgentActivityPhase | 'Completed' | 'Idle' | 'Planned'
export type AgentFlowLayoutMode = 'flow' | 'mindmap' | 'tree'
interface ParentLink {
  id: string
  parent: string
}
export type AgentFlowBubbleGeometry = Pick<AgentFlowBubble, 'id' | 'parent' | 'kind' | 'size'>
export interface AgentFlowNodeData extends Record<string, unknown> {
  name: string
  lane?: MachineActivityLane
  bubble?: AgentFlowBubble
  artifactUrl?: (id: string) => string
  alternateSourcePosition?: Position
  workBranch?: { count: number; expanded: boolean; toggle: (id: string) => void }
}
export type AgentFlowNode = Node<AgentFlowNodeData, 'hub' | 'agent' | 'bubble'>
type AgentFlowEdge = Extract<BuiltInEdge, { type?: 'smoothstep' | 'default' }>
export interface AgentFlowLayout { nodes: AgentFlowNode[]; edges: AgentFlowEdge[] }

interface NodeGeometry {
  id: string
  parent: string | null
  type: NonNullable<AgentFlowNode['type']>
  width: number
  height: number
}
interface Placement {
  position: XYPosition
  sourcePosition: Position
  targetPosition: Position
  alternateSourcePosition?: Position
}
const elk = new ELK()
const hubId = 'project-hub'
const cardSizes = {
  flow: { hub: { width: 224, height: 108 }, agent: { width: 278, height: 156 } },
  compact: { hub: { width: 174, height: 76 }, agent: { width: 216, height: 96 } },
}
const textBubble = { width: 228, height: 72 }
const readingSizes = { expanded: { width: 268, height: 94 }, report: { width: 304, height: 142 } }
const bubbleSizes = {
  task: textBubble, activity: textBubble, text: textBubble,
  image: { width: 220, height: 146 }, diagram: { width: 220, height: 112 },
}
const directions = { RIGHT: Position.Right, LEFT: Position.Left, DOWN: Position.Bottom }
const opposite = {
  [Position.Left]: Position.Right,
  [Position.Right]: Position.Left,
  [Position.Top]: Position.Bottom,
  [Position.Bottom]: Position.Top,
}
const alternateHandle = 'alternate'
const arrow = { type: MarkerType.ArrowClosed, width: 14, height: 14, color: 'var(--text-muted)' }
const mindMapGap = 18
const treeLayerGap = 42
const treeWorkGap = 12
const treeHalfTrunk = 21
const treeRouteOffset = 12

/** Presentation-only topology. A project link means membership, not delegation. */
export function agentFlowTopology(lanes: MachineActivityLane[]): ParentLink[] {
  const ids = new Set(lanes.map(({ agent }) => agent.agentId))
  const topology = lanes.map(({ agent }) => ({
    id: agent.agentId,
    parent: agent.parentAgentId && ids.has(agent.parentAgentId) ? agent.parentAgentId : hubId,
  })).sort((a, b) => a.id.localeCompare(b.id))
  const byId = new Map(topology.map((agent) => [agent.id, agent]))
  const settled = new Set<string>()
  for (const agent of topology) {
    const path = new Set<string>()
    let current = byId.get(agent.id)
    while (current && !settled.has(current.id) && !path.has(current.id)) {
      path.add(current.id)
      current = byId.get(current.parent)
    }
    // Quarantine every edge inside a contradictory cycle. Valid children of those agents
    // still keep their exact observed parent, and the agents remain visible as members.
    let inCycle = false
    for (const id of path) {
      if (id === current?.id) inCycle = true
      if (inCycle) byId.get(id)!.parent = hubId
      settled.add(id)
    }
  }
  return topology
}

async function placeLayered(nodes: readonly NodeGeometry[], direction: keyof typeof directions, nodeGap = 26): Promise<Map<string, Placement>> {
  const vertical = direction === 'DOWN'
  const result = await elk.layout({
    id: 'flow', layoutOptions: {
      'elk.algorithm': 'layered', 'elk.direction': direction, 'elk.spacing.nodeNode': String(nodeGap),
      'elk.layered.spacing.nodeNodeBetweenLayers': String(vertical ? treeLayerGap : 68),
      ...(vertical ? { 'elk.layered.layering.strategy': 'LONGEST_PATH_SOURCE' } : {}),
      'elk.layered.nodePlacement.strategy': 'BRANDES_KOEPF',
      'elk.layered.nodePlacement.bk.fixedAlignment': 'BALANCED',
    },
    children: nodes.map(({ id, width, height }) => ({ id, width, height })),
    edges: nodes.flatMap(({ id, parent }) => parent === null ? [] : [{ id: `link:${id}`, sources: [parent], targets: [id] }]),
  })
  const sourcePosition = directions[direction]
  const targetPosition = opposite[sourcePosition]
  return new Map((result.children ?? []).map(({ id, x, y }) => {
    if (x === undefined || y === undefined) throw new Error('Agent Flow layout returned an unpositioned node')
    return [id, { position: { x, y }, sourcePosition, targetPosition }]
  }))
}

function indexChildren(nodes: readonly NodeGeometry[]) {
  const children = new Map<string, NodeGeometry[]>()
  for (const node of nodes) {
    if (node.parent === null) continue
    const siblings = children.get(node.parent)
    if (siblings) siblings.push(node)
    else children.set(node.parent, [node])
  }
  return children
}

async function placeMindMap(hub: NodeGeometry, nodes: readonly NodeGeometry[]): Promise<Map<string, Placement>> {
  const children = indexChildren(nodes)
  const roots = children.get(hub.id) ?? []
  const center = roots.length === 1 ? roots[0] : hub
  const sides = [
    { direction: 'RIGHT' as const, nodes: [] as NodeGeometry[], weight: 0 },
    { direction: 'LEFT' as const, nodes: [] as NodeGeometry[], weight: 0 },
  ]
  for (const root of children.get(center.id) ?? []) {
    // Each observed subtree stays intact. Reserve both sides for agent branches before
    // placing the center's own work, and keep side assignment independent of live copy.
    const branch = [root]
    let weight = 0
    for (let i = 0; i < branch.length; i++) {
      const descendants = children.get(branch[i].id) ?? []
      for (const child of descendants) branch.push(child)
      if (!descendants.some((child) => child.type === 'agent')) weight += branch[i].height + mindMapGap
    }
    const side = sides[0].weight <= sides[1].weight ? sides[0] : sides[1]
    for (const node of branch) side.nodes.push(node)
    side.weight += weight
  }

  // ELK places each side independently, with a shared center where the two layouts meet.
  const position = { x: -center.width / 2, y: -center.height / 2 }
  const placements = new Map<string, Placement>([[center.id, {
    position, sourcePosition: Position.Right, targetPosition: Position.Top,
    alternateSourcePosition: sides[1].nodes.length ? Position.Left : undefined,
  }]])
  if (center.id !== hub.id) {
    placements.set(hub.id, {
      position: { x: -hub.width / 2, y: position.y - hub.height - 40 },
      sourcePosition: Position.Bottom, targetPosition: Position.Top,
    })
  }
  await Promise.all(sides.filter((side) => side.nodes.length).map(async ({ nodes, direction }) => {
    const side = await placeLayered([{ ...center, parent: null }, ...nodes], direction, mindMapGap)
    const origin = side.get(center.id)!.position
    for (const [id, placement] of side) {
      if (id === center.id) continue
      placements.set(id, {
        ...placement,
        position: { x: placement.position.x - origin.x + position.x, y: placement.position.y - origin.y + position.y },
      })
    }
  }))
  return placements
}

function packTreeGroup(owner: NodeGeometry, work: readonly NodeGeometry[]) {
  // Each inward-facing port leads to a clear center trunk, so connectors share the
  // vertical run and fan out at their own row, without crossing neighboring cards.
  const halfWidth = Math.max(owner.width / 2, ...work.map(({ width }) => width + treeHalfTrunk))
  const columnById = new Map<string, number>()
  const heights = [0, 0]
  for (const leaf of [...work].sort((a, b) => b.height - a.height)) {
    const column = heights[0] <= heights[1] ? 0 : 1
    columnById.set(leaf.id, column)
    heights[column] += leaf.height + treeWorkGap
  }
  const columns = [owner.height + 16, owner.height + 16]
  const leaves = work.map(({ id, width, height }) => {
    const column = columnById.get(id)!
    const left = column === 0
    const position = { x: left ? -treeHalfTrunk - width : treeHalfTrunk, y: columns[column] }
    columns[column] += height + treeWorkGap
    return { id, position, targetPosition: left ? Position.Right : Position.Left }
  })
  const height = work.length ? Math.max(...columns) - treeWorkGap : owner.height
  return { owner, extent: { ...owner, width: halfWidth * 2, height }, leaves }
}

async function placeTree(hub: NodeGeometry, nodes: readonly NodeGeometry[]): Promise<Map<string, Placement>> {
  const children = indexChildren(nodes)
  const groups: ReturnType<typeof packTreeGroup>[] = []
  let level = [hub]
  while (level.length) {
    const next: NodeGeometry[] = []
    const layer = level.map((owner) => {
      const work: NodeGeometry[] = []
      for (const child of children.get(owner.id) ?? []) {
        if (child.type === 'bubble') work.push(child)
        else next.push(child)
      }
      return packTreeGroup(owner, work)
    })
    // Align the owner cards across each generation and reserve one routing channel
    // below the tallest work column before the next generation starts.
    const height = Math.max(...layer.map(({ extent }) => extent.height))
    for (const group of layer) groups.push({ ...group, extent: { ...group.extent, height } })
    level = next
  }
  const groupPlacements = await placeLayered(groups.map(({ extent }) => extent), 'DOWN')
  const placements = new Map<string, Placement>()
  for (const { owner, extent, leaves } of groups) {
    const group = groupPlacements.get(owner.id)!
    const centerX = group.position.x + extent.width / 2
    placements.set(owner.id, { ...group, position: { x: centerX - owner.width / 2, y: group.position.y } })
    for (const { id, position, targetPosition } of leaves) {
      placements.set(id, { sourcePosition: Position.Bottom, targetPosition,
        position: { x: centerX + position.x, y: group.position.y + position.y } })
    }
  }
  return placements
}

function geometryHandle(type: 'source' | 'target', position: Position, width: number, height: number, id?: string) {
  const x = position === Position.Left ? 0 : position === Position.Right ? width - 1 : width / 2
  const y = position === Position.Top ? 0 : position === Position.Bottom ? height - 1 : height / 2
  return { type, position, x, y, ...(id ? { id } : {}) }
}

/** Only topology, mode and bounded bubble dimensions affect layout. Updated copy within a size keeps its position. */
export async function layoutAgentFlow(
  topology: readonly ParentLink[],
  mode: AgentFlowLayoutMode = 'flow',
  bubbles: readonly AgentFlowBubbleGeometry[] = [],
): Promise<AgentFlowLayout> {
  const sizes = mode === 'flow' ? cardSizes.flow : cardSizes.compact
  const hub: NodeGeometry = { id: hubId, parent: null, type: 'hub', ...sizes.hub }
  const nodes: NodeGeometry[] = [hub, ...topology.map((agent): NodeGeometry => ({ ...agent, type: 'agent', ...sizes.agent }))]
  const agentIds = new Set(topology.map(({ id }) => id))
  const ids = new Set([...agentIds, hub.id])
  for (const { id, parent, kind, size } of [...bubbles].sort((a, b) => a.id.localeCompare(b.id))) {
    if (ids.has(id) || !agentIds.has(parent)) continue
    nodes.push({ id, parent, type: 'bubble', ...(size ? readingSizes[size] : bubbleSizes[kind]) })
    ids.add(id)
  }
  const placements = mode === 'mindmap'
    ? await placeMindMap(hub, nodes)
    : mode === 'tree' ? await placeTree(hub, nodes) : await placeLayered(nodes, 'RIGHT')
  const byId = new Map(nodes.map((node) => [node.id, node]))
  return {
    nodes: nodes.map(({ id, type, width, height }) => {
      const placement = placements.get(id)
      if (!placement) throw new Error('Agent Flow layout omitted a node')
      const { position, sourcePosition, targetPosition, alternateSourcePosition } = placement
      return {
        id, type, position, width, height, sourcePosition, targetPosition,
        data: { name: '', alternateSourcePosition },
        // CONSTRAINT: Live data replaces the controlled nodes without resizing them. Supply
        // handle bounds with each node so React Flow keeps edges between measurements.
        handles: [
          geometryHandle('target', targetPosition, width, height),
          geometryHandle('source', sourcePosition, width, height),
          ...(alternateSourcePosition ? [geometryHandle('source', alternateSourcePosition, width, height, alternateHandle)] : []),
        ],
      }
    }),
    edges: nodes.flatMap(({ id, parent, type }): AgentFlowEdge[] => {
      if (parent === null) return []
      const source = placements.get(parent)!
      const target = placements.get(id)!
      const membership = parent === hub.id
      const ownership = type === 'bubble'
      let pathOptions: SmoothStepPathOptions | undefined
      if (mode === 'tree') {
        pathOptions = { offset: treeRouteOffset, borderRadius: 7 }
        if (!ownership) {
          const distance = target.position.y - source.position.y - byId.get(parent)!.height
          // Smoothstep routes opposite ports at this fraction of the gap between
          // the offset points. Align all sibling bends in the gap above their cards.
          pathOptions.stepPosition = (distance - treeLayerGap / 2 - treeRouteOffset) / (distance - 2 * treeRouteOffset)
        }
      }
      const edge = {
        id: `link:${id}`, source: parent, target: id,
        sourceHandle: source.alternateSourcePosition === opposite[target.targetPosition] ? alternateHandle : undefined,
        className: membership ? 'flow2-membership' : ownership ? 'flow2-work' : 'flow2-delegation',
        markerEnd: membership ? undefined : arrow,
        ariaLabel: membership ? 'Agent belongs to project' : ownership ? 'Agent work or output' : 'Observed parent to subagent',
      }
      return [mode === 'mindmap' ? { ...edge, type: 'default' } : { ...edge, type: 'smoothstep', pathOptions }]
    }),
  }
}

export function flowOperation(lane: MachineActivityLane): FlowOperation {
  if (lane.active) return lane.agent.phase ?? 'Working'
  return lane.agent.state === 'Completed' ? 'Completed' : lane.agent.state === 'Planned' ? 'Planned' : 'Idle'
}
