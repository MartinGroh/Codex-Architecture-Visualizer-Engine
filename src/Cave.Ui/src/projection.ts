import type {
  AgentNodeActivity,
  ArchitectureNode,
  ArchitectureNodeKind,
  ArchitectureRelation,
  ArchitectureSnapshot,
  EvidenceConfidence,
} from './types'
import { isTestProject } from './projectTypes'

export const projectionLevels = ['System', 'Projects', 'Namespaces', 'Classes'] as const

export type ProjectionLevel = (typeof projectionLevels)[number]
export type ProjectionMode = 'Auto' | ProjectionLevel

export interface ProjectionOptions {
  hideTests?: boolean
  hideExternalPackages?: boolean
  expandedNodeIds?: ReadonlySet<string>
  /** Exact canonical nodes to reveal through this projection without opening every sibling. */
  revealedNodeIds?: ReadonlySet<string>
}

export interface HierarchyExpansionState {
  expandedNodeIds: ReadonlySet<string>
  focusNodeId: string | null
}

export interface HierarchyPresentation {
  expandableNodeIds: ReadonlySet<string>
  expandedNodeIds: ReadonlySet<string>
  nestedNodeIds: ReadonlySet<string>
  dimmedNodeIds: ReadonlySet<string>
}

/** Returns the canonical state with every hierarchy branch collapsed. */
export function collapseHierarchyExpansion(): HierarchyExpansionState {
  return {
    expandedNodeIds: new Set(),
    focusNodeId: null,
  }
}

const visibleKinds: Record<ProjectionLevel, ReadonlySet<ArchitectureNodeKind>> = {
  System: new Set(['ArchitectureGroup']),
  Projects: new Set(['Project']),
  Namespaces: new Set(['Project', 'Namespace']),
  Classes: new Set(['Namespace', 'Class', 'Interface', 'AbstractClass']),
}

const confidenceOrder: Record<EvidenceConfidence, number> = {
  Exact: 0,
  Inferred: 1,
  Heuristic: 2,
  AgentDeclared: 3,
}

/**
 * Produces a presentation-only semantic projection without mutating the canonical snapshot.
 * Hidden descendants contribute their relations to the nearest visible ancestor.
 */
export function projectArchitecture(
  snapshot: ArchitectureSnapshot,
  level: ProjectionLevel,
  options: ProjectionOptions = {},
): ArchitectureSnapshot {
  const nodesById = new Map(snapshot.graph.nodes.map((node) => [node.id, node]))
  const excludedIds = options.hideTests ? testProjectDescendants(snapshot.graph.nodes) : new Set<string>()
  if (options.hideTests) {
    for (const packageId of testOnlyExternalPackageIds(snapshot, excludedIds)) {
      excludedIds.add(packageId)
    }
  }
  if (options.hideExternalPackages) {
    for (const packageId of externalPackageDescendants(snapshot.graph.nodes)) {
      excludedIds.add(packageId)
    }
  }
  const expandedNodeIds = options.expandedNodeIds ?? new Set<string>()
  const revealedNodeIds = selectiveRevealClosure(
    options.revealedNodeIds ?? new Set<string>(),
    nodesById,
    level,
  )
  const visible = snapshot.graph.nodes.filter((node) =>
    !excludedIds.has(node.id)
    && (visibleKinds[level].has(node.kind)
      || isExternalPackage(node)
      || revealedNodeIds.has(node.id)
      || isRevealedChild(node, nodesById, expandedNodeIds)))
  const visibleIds = new Set(visible.map((node) => node.id))
  const representativeCache = new Map<string, string | null>()

  const findVisibleAncestor = (nodeId: string): string | null => {
    if (excludedIds.has(nodeId)) return null

    if (representativeCache.has(nodeId)) {
      return representativeCache.get(nodeId) ?? null
    }

    let candidate: ArchitectureNode | undefined = nodesById.get(nodeId)
    const visited: string[] = []
    while (candidate !== undefined) {
      visited.push(candidate.id)
      if (visibleIds.has(candidate.id)) {
        for (const visitedId of visited) {
          representativeCache.set(visitedId, candidate.id)
        }
        return candidate.id
      }

      candidate = candidate.parentId === null
        ? undefined
        : nodesById.get(candidate.parentId)
    }

    for (const visitedId of visited) {
      representativeCache.set(visitedId, null)
    }
    return null
  }

  const projectedNodes = visible.map((node) => ({
    ...node,
    parentId: node.parentId !== null && visibleIds.has(node.parentId)
      ? node.parentId
      : null,
  }))

  const aggregated = new Map<string, ArchitectureRelation>()
  for (const relation of snapshot.graph.relations) {
    const sourceId = findVisibleAncestor(relation.sourceId)
    const targetId = findVisibleAncestor(relation.targetId)
    if (sourceId === null || targetId === null || sourceId === targetId) {
      continue
    }

    const key = `${relation.kind}:${sourceId}:${targetId}`
    const current = aggregated.get(key)
    if (current === undefined) {
      aggregated.set(key, {
        ...relation,
        id: `projection:${level}:${key}`,
        sourceId,
        targetId,
      })
      continue
    }

    aggregated.set(key, {
      ...current,
      weight: current.weight + relation.weight,
      evidenceCount: current.evidenceCount + relation.evidenceCount,
      confidence: leastCertain(current.confidence, relation.confidence),
    })
  }

  const projectedActivity = new Map<string, AgentNodeActivity>()
  for (const activity of snapshot.activity.nodes) {
    const nodeId = findVisibleAncestor(activity.nodeId)
    if (nodeId === null) {
      continue
    }

    const key = `${activity.agentId}:${nodeId}:${activity.evidence}`
    const current = projectedActivity.get(key)
    projectedActivity.set(key, current === undefined
      ? {
          ...activity,
          nodeId,
          isDirect: activity.nodeId === nodeId && activity.isDirect,
        }
      : {
          ...current,
          isDirect: current.isDirect || (activity.nodeId === nodeId && activity.isDirect),
          updatedAtUtc: current.updatedAtUtc >= activity.updatedAtUtc
            ? current.updatedAtUtc
            : activity.updatedAtUtc,
          paths: [...new Set([...current.paths, ...activity.paths])],
        })
  }

  const projectedEdits = snapshot.activity.recentEdits.map((edit) => ({
    ...edit,
    nodeIds: [...new Set(edit.nodeIds
      .map(findVisibleAncestor)
      .filter((nodeId): nodeId is string => nodeId !== null))],
  }))

  return {
    metadata: snapshot.metadata,
    graph: {
      nodes: projectedNodes,
      relations: [...aggregated.values()],
    },
    git: {
      ...snapshot.git,
      nodes: snapshot.git.nodes.filter((delta) => visibleIds.has(delta.nodeId)),
    },
    activity: {
      ...snapshot.activity,
      nodes: [...projectedActivity.values()],
      recentEdits: projectedEdits,
    },
    conversation: snapshot.conversation,
  }
}

/**
 * Keeps expansion to one project/namespace branch so focused drill-down remains legible.
 */
export function toggleHierarchyExpansion(
  snapshot: ArchitectureSnapshot,
  current: HierarchyExpansionState,
  nodeId: string,
): HierarchyExpansionState {
  const nodesById = new Map(snapshot.graph.nodes.map((node) => [node.id, node]))
  const node = nodesById.get(nodeId)
  if (node === undefined || (node.kind !== 'Project' && node.kind !== 'Namespace')) {
    return current
  }

  if (current.expandedNodeIds.has(nodeId)) {
    if (node.kind === 'Namespace') {
      const projectId = findAncestorOfKind(node, 'Project', nodesById)?.id ?? null
      return projectId !== null && current.expandedNodeIds.has(projectId)
        ? { expandedNodeIds: new Set([projectId]), focusNodeId: projectId }
        : { expandedNodeIds: new Set(), focusNodeId: null }
    }

    return { expandedNodeIds: new Set(), focusNodeId: null }
  }

  if (node.kind === 'Project') {
    return { expandedNodeIds: new Set([node.id]), focusNodeId: node.id }
  }

  const projectId = findAncestorOfKind(node, 'Project', nodesById)?.id
  return {
    expandedNodeIds: new Set(projectId === undefined ? [node.id] : [projectId, node.id]),
    focusNodeId: node.id,
  }
}

/**
 * Derives card-level hierarchy decoration from canonical and projected graph state.
 */
export function createHierarchyPresentation(
  snapshot: ArchitectureSnapshot,
  projectedSnapshot: ArchitectureSnapshot,
  level: ProjectionLevel,
  expansion: HierarchyExpansionState,
  focusNodeIds: ReadonlySet<string> = expansion.focusNodeId === null
    ? new Set()
    : new Set([expansion.focusNodeId]),
): HierarchyPresentation {
  const canonicalNodesById = new Map(snapshot.graph.nodes.map((node) => [node.id, node]))
  const projectedIds = new Set(projectedSnapshot.graph.nodes.map((node) => node.id))
  const expandableNodeIds = new Set<string>()
  for (const node of projectedSnapshot.graph.nodes) {
    if (snapshot.graph.nodes.some((candidate) => isExpandableChild(candidate, node))) {
      expandableNodeIds.add(node.id)
    }
  }

  const nestedNodeIds = new Set(projectedSnapshot.graph.nodes
    .filter((node) => !visibleKinds[level].has(node.kind) && !isExternalPackage(node))
    .map((node) => node.id))
  const dimmedNodeIds = new Set<string>()
  const visibleFocusNodeIds = [...focusNodeIds].filter((nodeId) => projectedIds.has(nodeId))
  if (visibleFocusNodeIds.length > 0) {
    const affectedNodeIds = new Set(visibleFocusNodeIds)
    for (const node of projectedSnapshot.graph.nodes) {
      if (visibleFocusNodeIds.some((focusNodeId) => hasAncestor(node, focusNodeId, canonicalNodesById))) {
        affectedNodeIds.add(node.id)
      }
    }

    for (const focusNodeId of visibleFocusNodeIds) {
      let ancestor = canonicalNodesById.get(focusNodeId)
      while (ancestor?.parentId !== null && ancestor?.parentId !== undefined) {
        affectedNodeIds.add(ancestor.parentId)
        ancestor = canonicalNodesById.get(ancestor.parentId)
      }
    }

    for (const nodeId of projectedIds) {
      if (!affectedNodeIds.has(nodeId)) {
        dimmedNodeIds.add(nodeId)
      }
    }
  }

  return {
    expandableNodeIds,
    expandedNodeIds: expansion.expandedNodeIds,
    nestedNodeIds,
    dimmedNodeIds,
  }
}

function selectiveRevealClosure(
  requestedNodeIds: ReadonlySet<string>,
  nodesById: ReadonlyMap<string, ArchitectureNode>,
  level: ProjectionLevel,
): Set<string> {
  const revealedNodeIds = new Set<string>()
  for (const nodeId of requestedNodeIds) {
    let candidate = nodesById.get(nodeId)
    while (candidate !== undefined
      && !visibleKinds[level].has(candidate.kind)
      && !isExternalPackage(candidate)) {
      revealedNodeIds.add(candidate.id)
      candidate = candidate.parentId === null ? undefined : nodesById.get(candidate.parentId)
    }
  }

  return revealedNodeIds
}

function isRevealedChild(
  node: ArchitectureNode,
  nodesById: ReadonlyMap<string, ArchitectureNode>,
  expandedNodeIds: ReadonlySet<string>,
): boolean {
  if (node.parentId === null || !expandedNodeIds.has(node.parentId)) return false
  const parent = nodesById.get(node.parentId)
  return parent !== undefined && isExpandableChild(node, parent)
}

function isExpandableChild(child: ArchitectureNode, parent: ArchitectureNode): boolean {
  if (child.parentId !== parent.id) return false
  if (parent.kind === 'Project') return child.kind === 'Namespace'
  return parent.kind === 'Namespace'
    && (child.kind === 'Class' || child.kind === 'Interface' || child.kind === 'AbstractClass')
}

function findAncestorOfKind(
  node: ArchitectureNode,
  kind: ArchitectureNodeKind,
  nodesById: ReadonlyMap<string, ArchitectureNode>,
): ArchitectureNode | undefined {
  let candidate: ArchitectureNode | undefined = node
  while (candidate?.parentId !== null && candidate?.parentId !== undefined) {
    candidate = nodesById.get(candidate.parentId)
    if (candidate?.kind === kind) return candidate
  }

  return undefined
}

function hasAncestor(
  node: ArchitectureNode,
  ancestorId: string,
  nodesById: ReadonlyMap<string, ArchitectureNode>,
): boolean {
  let candidate: ArchitectureNode | undefined = node
  while (candidate?.parentId !== null && candidate?.parentId !== undefined) {
    if (candidate.parentId === ancestorId) return true
    candidate = nodesById.get(candidate.parentId)
  }

  return false
}

function testProjectDescendants(nodes: ArchitectureNode[]): Set<string> {
  return matchingNodeDescendants(nodes, isTestProject)
}

function externalPackageDescendants(nodes: ArchitectureNode[]): Set<string> {
  return matchingNodeDescendants(nodes, isExternalPackage)
}

function matchingNodeDescendants(
  nodes: ArchitectureNode[],
  matchesRoot: (node: ArchitectureNode) => boolean,
): Set<string> {
  const childrenByParent = new Map<string, string[]>()
  for (const node of nodes) {
    if (node.parentId === null) continue
    const children = childrenByParent.get(node.parentId) ?? []
    children.push(node.id)
    childrenByParent.set(node.parentId, children)
  }

  const excluded = new Set<string>()
  const pending = nodes.filter(matchesRoot).map((node) => node.id)
  while (pending.length > 0) {
    const nodeId = pending.pop()!
    if (excluded.has(nodeId)) continue
    excluded.add(nodeId)
    pending.push(...(childrenByParent.get(nodeId) ?? []))
  }

  return excluded
}

function testOnlyExternalPackageIds(
  snapshot: ArchitectureSnapshot,
  excludedTestIds: ReadonlySet<string>,
): Set<string> {
  const externalPackageIds = new Set(snapshot.graph.nodes.filter(isExternalPackage).map((node) => node.id))
  const consumersByPackage = new Map<string, string[]>()
  for (const relation of snapshot.graph.relations) {
    if (relation.kind !== 'ProjectReference' || !externalPackageIds.has(relation.targetId)) continue
    const consumers = consumersByPackage.get(relation.targetId) ?? []
    consumers.push(relation.sourceId)
    consumersByPackage.set(relation.targetId, consumers)
  }

  return new Set([...externalPackageIds].filter((packageId) => {
    const consumers = consumersByPackage.get(packageId) ?? []
    return consumers.length > 0 && consumers.every((consumerId) => excludedTestIds.has(consumerId))
  }))
}

export function isExternalPackage(node: ArchitectureNode): boolean {
  return node.kind === 'Project'
    && node.tags.includes('source:external')
    && node.tags.includes('read-only')
    && node.tags.some((tag) => tag.startsWith('package:'))
}

/** Returns the semantic level for a newly enabled automatic projection. */
export function projectionLevelForZoom(zoom: number): ProjectionLevel {
  if (zoom < 0.18) return 'System'
  if (zoom < 0.62) return 'Projects'
  if (zoom < 1.05) return 'Namespaces'
  return 'Classes'
}

/**
 * Applies hysteresis to automatic semantic zoom so a small wheel movement near a
 * threshold does not repeatedly relayout the graph.
 */
export function nextAutoProjectionLevel(
  zoom: number,
  current: ProjectionLevel,
): ProjectionLevel {
  switch (current) {
    case 'System':
      return zoom > 0.24 ? 'Projects' : current
    case 'Projects':
      if (zoom < 0.14) return 'System'
      if (zoom > 0.72) return 'Namespaces'
      return current
    case 'Namespaces':
      if (zoom < 0.52) return 'Projects'
      if (zoom > 1.18) return 'Classes'
      return current
    case 'Classes':
      return zoom < 0.92 ? 'Namespaces' : current
  }
}

function leastCertain(
  left: EvidenceConfidence,
  right: EvidenceConfidence,
): EvidenceConfidence {
  return confidenceOrder[left] >= confidenceOrder[right] ? left : right
}
