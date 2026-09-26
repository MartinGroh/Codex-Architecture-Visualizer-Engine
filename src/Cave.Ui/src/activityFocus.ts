import type { ArchitectureSnapshot } from './types'

/**
 * Narrows an already-projected snapshot to the evidence-backed active-work scope.
 * The canonical snapshot stays unchanged; relations and overlays survive only when
 * both of their projected endpoints remain visible.
 */
export function focusActivitySnapshot(
  snapshot: ArchitectureSnapshot,
  focusNodeIds: ReadonlySet<string>,
): ArchitectureSnapshot {
  const availableNodeIds = new Set(snapshot.graph.nodes.map((node) => node.id))
  const visibleNodeIds = new Set(
    [...focusNodeIds].filter((nodeId) => availableNodeIds.has(nodeId)),
  )

  return {
    ...snapshot,
    graph: {
      nodes: snapshot.graph.nodes
        .filter((node) => visibleNodeIds.has(node.id))
        .map((node) => ({
          ...node,
          parentId: node.parentId !== null && visibleNodeIds.has(node.parentId)
            ? node.parentId
            : null,
        })),
      relations: snapshot.graph.relations.filter((relation) => (
        visibleNodeIds.has(relation.sourceId) && visibleNodeIds.has(relation.targetId)
      )),
    },
    git: {
      ...snapshot.git,
      nodes: snapshot.git.nodes.filter((delta) => visibleNodeIds.has(delta.nodeId)),
    },
    activity: {
      ...snapshot.activity,
      nodes: snapshot.activity.nodes.filter((activity) => visibleNodeIds.has(activity.nodeId)),
      recentEdits: snapshot.activity.recentEdits.map((edit) => ({
        ...edit,
        nodeIds: edit.nodeIds.filter((nodeId) => visibleNodeIds.has(nodeId)),
      })),
    },
  }
}
