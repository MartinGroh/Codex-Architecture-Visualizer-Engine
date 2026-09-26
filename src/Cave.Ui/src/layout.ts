import ELK from 'elkjs/lib/elk.bundled.js'
import type { ElkExtendedEdge } from 'elkjs/lib/elk-api'
import { Position } from '@xyflow/react'
import {
  createAgentNodeDecorations,
  createFlowEdges,
  type ArchitectureEdgeRoute,
  type ArchitectureFlowEdge,
  type ArchitectureFlowNode,
} from './graphPresentation'
import type { HierarchyPresentation, ProjectionLevel } from './projection'
import type { ArchitectureNode, ArchitectureSnapshot } from './types'
import { longestBalancedNodeLabelRow } from './nodeLabel'

export interface GraphLayout {
  nodes: ArchitectureFlowNode[]
  edges: ArchitectureFlowEdge[]
}

export interface LayoutEngine {
  layout(
    snapshot: ArchitectureSnapshot,
    level: ProjectionLevel,
    options?: LayoutOptions,
  ): Promise<GraphLayout>
}

export interface LayoutOptions {
  hierarchy?: HierarchyPresentation
  onToggleExpansion?: (nodeId: string) => void
  density?: LayoutDensity
}

export type LayoutDensity = 'Normal' | 'Tight'

const elk = new ELK()

interface NodeDimensions {
  width: number
  height: number
}

interface LayoutSpacing {
  node: number
  layer: number
  edge: number
  component: number
}

const dimensionsByKind: Record<ArchitectureNode['kind'], NodeDimensions> = {
  ArchitectureGroup: { width: 276, height: 148 },
  Project: { width: 236, height: 148 },
  Namespace: { width: 244, height: 148 },
  Class: { width: 252, height: 148 },
  Interface: { width: 252, height: 148 },
  AbstractClass: { width: 252, height: 148 },
}

const nestedDimensionsByKind: Record<ArchitectureNode['kind'], NodeDimensions> = {
  ArchitectureGroup: { width: 276, height: 148 },
  Project: { width: 236, height: 148 },
  Namespace: { width: 218, height: 136 },
  Class: { width: 226, height: 136 },
  Interface: { width: 226, height: 136 },
  AbstractClass: { width: 226, height: 136 },
}

const cardHorizontalChrome = 28
const distantTitleCharacterWidth = 14

const spacingByLevel: Record<ProjectionLevel, LayoutSpacing> = {
  System: { node: 64, layer: 112, edge: 18, component: 72 },
  Projects: { node: 52, layer: 100, edge: 16, component: 68 },
  Namespaces: { node: 44, layer: 90, edge: 14, component: 58 },
  Classes: { node: 34, layer: 78, edge: 12, component: 46 },
}

const tightSpacingScale: LayoutSpacing = {
  node: 0.68,
  layer: 0.7,
  edge: 0.72,
  component: 0.68,
}

export function architectureNodeDimensions(
  node: ArchitectureNode,
  isNested = false,
): NodeDimensions {
  const base = isNested ? nestedDimensionsByKind[node.kind] : dimensionsByKind[node.kind]
  const labelWidth = cardHorizontalChrome
    + (longestBalancedNodeLabelRow(node.name) * distantTitleCharacterWidth)

  return {
    width: Math.max(base.width, labelWidth),
    height: base.height,
  }
}

export class ElkLayoutEngine implements LayoutEngine {
  public async layout(
    snapshot: ArchitectureSnapshot,
    level: ProjectionLevel,
    options: LayoutOptions = {},
  ): Promise<GraphLayout> {
    const edges = createFlowEdges(snapshot)
    const hierarchy = options.hierarchy
    const gitDeltas = new Map(snapshot.git.nodes.map((delta) => [delta.nodeId, delta]))
    const activitiesByNode = new Map<string, typeof snapshot.activity.nodes>()
    for (const activity of snapshot.activity.nodes) {
      const current = activitiesByNode.get(activity.nodeId) ?? []
      current.push(activity)
      activitiesByNode.set(activity.nodeId, current)
    }
    const editsByNode = new Map<string, typeof snapshot.activity.recentEdits>()
    for (const edit of snapshot.activity.recentEdits) {
      for (const nodeId of edit.nodeIds) {
        const current = editsByNode.get(nodeId) ?? []
        current.push(edit)
        editsByNode.set(nodeId, current)
      }
    }
    const agentDecorationsByNode = createAgentNodeDecorations(snapshot)
    const density = options.density ?? 'Normal'
    const spacing = layoutSpacing(level, density)
    const edgeNodeSpacing = density === 'Tight'
      ? Math.max(18, spacing.edge * 2)
      : Math.max(24, spacing.edge * 2)
    const edgeNodeLayerSpacing = density === 'Tight'
      ? Math.max(22, spacing.edge * 2)
      : Math.max(28, spacing.edge * 2)
    const result = await elk.layout({
      id: 'cave-root',
      layoutOptions: {
        'elk.algorithm': 'layered',
        'elk.direction': 'RIGHT',
        'elk.edgeRouting': 'ORTHOGONAL',
        'elk.padding': '[top=24,left=24,bottom=24,right=24]',
        'elk.spacing.nodeNode': spacing.node.toString(),
        'elk.spacing.edgeEdge': spacing.edge.toString(),
        'elk.spacing.edgeNode': edgeNodeSpacing.toString(),
        'elk.spacing.componentComponent': spacing.component.toString(),
        'elk.layered.spacing.nodeNodeBetweenLayers': spacing.layer.toString(),
        'elk.layered.spacing.edgeEdgeBetweenLayers': spacing.edge.toString(),
        'elk.layered.spacing.edgeNodeBetweenLayers': edgeNodeLayerSpacing.toString(),
        'elk.layered.mergeEdges': 'false',
        'elk.layered.unnecessaryBendpoints': 'true',
        'elk.layered.nodePlacement.strategy': 'BRANDES_KOEPF',
        'elk.layered.nodePlacement.bk.fixedAlignment': 'BALANCED',
        'elk.layered.crossingMinimization.strategy': 'LAYER_SWEEP',
        'elk.layered.considerModelOrder.strategy': 'NODES_AND_EDGES',
        'elk.layered.cycleBreaking.strategy': 'GREEDY_MODEL_ORDER',
      },
      children: [...snapshot.graph.nodes]
        .sort((left, right) => left.id.localeCompare(right.id))
        .map((node) => ({
          id: node.id,
          ...architectureNodeDimensions(node, hierarchy?.nestedNodeIds.has(node.id)),
        })),
      edges: [...edges]
        .sort((left, right) => left.id.localeCompare(right.id))
        .map((edge) => ({
          id: edge.id,
          sources: [edge.source],
          targets: [edge.target],
        })),
    })

    const positions = new Map(
      (result.children ?? []).map((node) => [
        node.id,
        { x: node.x ?? 0, y: node.y ?? 0 },
      ]),
    )
    const routes = new Map(
      (result.edges ?? []).map((edge) => [edge.id, toArchitectureEdgeRoute(edge)]),
    )

    return {
      nodes: snapshot.graph.nodes.map((node) => {
        const dimensions = architectureNodeDimensions(
          node,
          hierarchy?.nestedNodeIds.has(node.id),
        )
        return {
          id: node.id,
          type: 'architecture',
          position: positions.get(node.id) ?? { x: 0, y: 0 },
          sourcePosition: Position.Right,
          targetPosition: Position.Left,
          ariaLabel: `${node.kind}: ${node.name}`,
          // CONSTRAINT: React Flow uses top-level dimensions to decide whether a node is initialized.
          // CSS dimensions alone render the card at the right size but leave its wrapper visibility hidden.
          width: dimensions.width,
          height: dimensions.height,
          style: dimensions,
          // React Flow cannot position an edge until its endpoint handles have been measured.
          // The layout already owns exact dimensions, so publish matching handle geometry and
          // avoid making first-paint edge rendering depend on an asynchronous DOM measurement.
          handles: [
            {
              type: 'target',
              position: Position.Left,
              x: 0,
              y: dimensions.height / 2,
              width: 1,
              height: 1,
            },
            {
              type: 'source',
              position: Position.Right,
              x: dimensions.width,
              y: dimensions.height / 2,
              width: 1,
              height: 1,
            },
          ],
          data: {
            architecture: node,
            gitDelta: gitDeltas.get(node.id) ?? null,
            activities: activitiesByNode.get(node.id) ?? [],
            agentDecorations: agentDecorationsByNode.get(node.id) ?? [],
            recentEdits: editsByNode.get(node.id) ?? [],
            hierarchy: {
              canExpand: hierarchy?.expandableNodeIds.has(node.id) ?? false,
              isExpanded: hierarchy?.expandedNodeIds.has(node.id) ?? false,
              isNested: hierarchy?.nestedNodeIds.has(node.id) ?? false,
              isDimmed: hierarchy?.dimmedNodeIds.has(node.id) ?? false,
              onToggleExpansion: options.onToggleExpansion,
            },
          },
        }
      }),
      edges: edges.map((edge) => {
        const route = routes.get(edge.id)
        if (route === undefined) {
          throw new Error(`ELK did not return a route for architecture edge '${edge.id}'.`)
        }

        return {
          ...edge,
          className: `${edge.className ?? ''}${
            hierarchy?.dimmedNodeIds.has(edge.source) === true
              || hierarchy?.dimmedNodeIds.has(edge.target) === true
              ? ' is-focus-dimmed'
              : ''
          }`,
          type: 'architectureRoute',
          data: {
            ...edge.data,
            route,
          },
        }
      }),
    }
  }
}

function layoutSpacing(level: ProjectionLevel, density: LayoutDensity): LayoutSpacing {
  const spacing = spacingByLevel[level]
  if (density === 'Normal') {
    return spacing
  }

  return {
    node: Math.round(spacing.node * tightSpacingScale.node),
    layer: Math.round(spacing.layer * tightSpacingScale.layer),
    edge: Math.round(spacing.edge * tightSpacingScale.edge),
    component: Math.round(spacing.component * tightSpacingScale.component),
  }
}

function toArchitectureEdgeRoute(edge: ElkExtendedEdge): ArchitectureEdgeRoute {
  if (edge.sections === undefined || edge.sections.length === 0) {
    throw new Error(`ELK returned no routed sections for architecture edge '${edge.id}'.`)
  }

  return {
    sections: edge.sections.map((section) => [
      section.startPoint,
      ...(section.bendPoints ?? []),
      section.endPoint,
    ]),
  }
}
