import { MarkerType, type Edge, type Node } from '@xyflow/react'
import type {
  AgentActivity,
  AgentNodeActivity,
  ArchitectureNode,
  ArchitectureRelationKind,
  ArchitectureSnapshot,
  GitNodeDelta,
  RecentAgentEdit,
} from './types'
import { isExternalPackage } from './projection'

export interface ArchitectureNodeData extends Record<string, unknown> {
  architecture: ArchitectureNode
  gitDelta: GitNodeDelta | null
  activities: AgentNodeActivity[]
  agentDecorations: AgentNodeDecoration[]
  recentEdits: RecentAgentEdit[]
  hierarchy: {
    canExpand: boolean
    isExpanded: boolean
    isNested: boolean
    isDimmed: boolean
    onToggleExpansion?: (nodeId: string) => void
  }
}

export type AgentNodeDecorationState = 'Active' | 'Worked'

export interface AgentNodeDecoration {
  agent: AgentActivity
  state: AgentNodeDecorationState
  evidence: AgentNodeActivity['evidence']
}

export type ArchitectureFlowNode = Node<ArchitectureNodeData, 'architecture'>

export interface ArchitectureRoutePoint {
  x: number
  y: number
}

export interface ArchitectureEdgeRoute {
  sections: ArchitectureRoutePoint[][]
}

export interface ArchitectureEdgeMetadata extends Record<string, unknown> {
  confidence?: string
  evidenceCount?: number
  relationKind?: ArchitectureRelationKind
  relationLabel?: string
  activityDirection?: 'input' | 'output' | 'both'
}

export interface ArchitectureRoutedEdgeData extends ArchitectureEdgeMetadata {
  route: ArchitectureEdgeRoute
}

export type ArchitectureFlowEdge = Edge<ArchitectureRoutedEdgeData, 'architectureRoute'>

const relationLabels: Record<ArchitectureRelationKind, string> = {
  Contains: 'contains',
  DependsOn: 'depends on',
  Inherits: 'inherits',
  Implements: 'implements',
  ProjectReference: 'references',
  HttpApi: 'uses HTTP API from',
}

export function createAgentNodeDecorations(
  snapshot: ArchitectureSnapshot,
): Map<string, AgentNodeDecoration[]> {
  const agentsById = new Map(snapshot.activity.agents.map((agent) => [agent.agentId, agent]))
  const activitiesByAgent = new Map<string, AgentNodeActivity[]>()

  for (const activity of snapshot.activity.nodes) {
    const current = activitiesByAgent.get(activity.agentId) ?? []
    current.push(activity)
    activitiesByAgent.set(activity.agentId, current)
  }

  const decorationsByNode = new Map<string, AgentNodeDecoration[]>()
  for (const [agentId, activities] of activitiesByAgent) {
    const agent = agentsById.get(agentId)
    if (agent === undefined || agent.state === 'Planned' || activities.length === 0) {
      continue
    }

    // CONSTRAINT: Pathless lifecycle/tool events can be newer than the latest mapped node.
    // Keep the newest mapped location visible while the backend independently owns liveness.
    const targetTimestamp = activities.reduce(
      (latest, activity) => activity.updatedAtUtc > latest ? activity.updatedAtUtc : latest,
      activities[0].updatedAtUtc,
    )
    const state: AgentNodeDecorationState = agent.state === 'Active' ? 'Active' : 'Worked'
    // Active work needs one unambiguous pulse at the latest mapped location. Once the
    // agent becomes idle/completed, retain every mapped location as quiet work history
    // until the work-indicator epoch is reset by the user's configured policy.
    const currentActivities = state === 'Active'
      ? activities.filter((activity) => activity.updatedAtUtc === targetTimestamp)
      : activities
    const currentActivitiesByNode = new Map<string, AgentNodeActivity[]>()
    for (const activity of currentActivities) {
      const nodeActivities = currentActivitiesByNode.get(activity.nodeId) ?? []
      nodeActivities.push(activity)
      currentActivitiesByNode.set(activity.nodeId, nodeActivities)
    }

    for (const [nodeId, nodeActivities] of currentActivitiesByNode) {
      const current = decorationsByNode.get(nodeId) ?? []
      current.push({
        agent,
        state,
        evidence: nodeActivities.some((activity) => activity.evidence === 'Declared')
          ? 'Declared'
          : 'Observed',
      })
      decorationsByNode.set(nodeId, current)
    }
  }

  for (const decorations of decorationsByNode.values()) {
    decorations.sort((left, right) => (
      Number(right.state === 'Active') - Number(left.state === 'Active')
      || Number(left.agent.isSubagent) - Number(right.agent.isSubagent)
      || left.agent.agentId.localeCompare(right.agent.agentId)
    ))
  }

  return decorationsByNode
}

export function createFlowEdges(snapshot: ArchitectureSnapshot): Edge<ArchitectureEdgeMetadata>[] {
  const nodesById = new Map(snapshot.graph.nodes.map((node) => [node.id, node]))
  const activeNodeIds = new Set([...createAgentNodeDecorations(snapshot)]
    .filter(([, decorations]) => decorations.some((decoration) => decoration.state === 'Active'))
    .map(([nodeId]) => nodeId))
  const projectReferencePairs = new Set(
    snapshot.graph.relations
      .filter((relation) => relation.kind === 'ProjectReference')
      .map((relation) => `${relation.sourceId}:${relation.targetId}`),
  )
  const containmentEdges = snapshot.graph.nodes
    .filter((node) => node.parentId !== null)
    .map<Edge<ArchitectureEdgeMetadata>>((node) => {
      const parentName = nodesById.get(node.parentId!)?.name ?? node.parentId!
      return {
        id: `contains:${node.parentId}:${node.id}`,
        source: node.parentId!,
        target: node.id,
        ariaLabel: `${parentName} contains ${node.name}`,
        className: 'containment-edge',
        interactionWidth: 24,
        markerEnd: {
          type: MarkerType.ArrowClosed,
          width: 15,
          height: 15,
          color: 'var(--hierarchy-edge)',
        },
        selectable: false,
      }
    })

  const semanticEdges = snapshot.graph.relations
    .filter((relation) => !(
      relation.kind === 'DependsOn'
      && projectReferencePairs.has(`${relation.sourceId}:${relation.targetId}`)
    ))
    .map<Edge<ArchitectureEdgeMetadata>>((relation) => {
      const relationLabel = relationLabels[relation.kind]
      const sourceName = nodesById.get(relation.sourceId)?.name ?? relation.sourceId
      const targetName = nodesById.get(relation.targetId)?.name ?? relation.targetId
      const activeSource = activeNodeIds.has(relation.sourceId)
      const activeTarget = activeNodeIds.has(relation.targetId)
      const isExternalPackageEdge = [nodesById.get(relation.sourceId), nodesById.get(relation.targetId)]
        .some((node) => node !== undefined && isExternalPackage(node))
      const supportsActivityFlow = relation.kind === 'DependsOn'
        || relation.kind === 'ProjectReference'
        || relation.kind === 'HttpApi'
      const activityDirection = supportsActivityFlow
        ? activeSource && activeTarget
          ? 'both'
          : activeSource
            ? 'input'
            : activeTarget
              ? 'output'
              : undefined
        : undefined

      return {
        id: relation.id,
        source: relation.sourceId,
        target: relation.targetId,
        ariaLabel: `${sourceName} ${relationLabel} ${targetName}`,
        className: `semantic-edge relation-${relation.kind.toLowerCase()}${isExternalPackageEdge ? ' external-package-edge' : ''}${activityDirection === undefined ? '' : ` activity-edge activity-edge--${activityDirection}`}`,
        interactionWidth: 24,
        markerEnd: {
          type: relation.kind === 'Implements' || relation.kind === 'HttpApi'
            ? MarkerType.Arrow
            : MarkerType.ArrowClosed,
          width: 14,
          height: 14,
        },
        data: {
          confidence: relation.confidence,
          evidenceCount: relation.evidenceCount,
          relationKind: relation.kind,
          relationLabel,
          activityDirection,
        },
      }
    })

  return [...containmentEdges, ...semanticEdges]
}

/**
 * Emphasizes routes incident to the selected card without changing graph layout or edge semantics.
 */
export function highlightConnectedEdges<TEdge extends Edge>(
  edges: TEdge[],
  selectedNodeId: string | null,
): TEdge[] {
  if (selectedNodeId === null) return edges

  return edges.map((edge) => edge.source === selectedNodeId || edge.target === selectedNodeId
    ? {
        ...edge,
        className: `${edge.className ?? ''} is-selection-related`.trim(),
        markerEnd: typeof edge.markerEnd === 'object' && edge.markerEnd !== null
          ? { ...edge.markerEnd, color: 'var(--violet)' }
          : edge.markerEnd,
      } as TEdge
    : edge)
}

export function formatNodeKind(kind: ArchitectureNode['kind']): string {
  switch (kind) {
    case 'ArchitectureGroup':
      return 'Architecture area'
    case 'AbstractClass':
      return 'Abstract class'
    default:
      return kind
  }
}
