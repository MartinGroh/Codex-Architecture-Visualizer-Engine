import type { ArchitectureEdgeMetadata, ArchitectureEdgeRoute } from './graphPresentation'

export function architectureEdgePath(route: ArchitectureEdgeRoute): string {
  return route.sections
    .map((section) => section
      .map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x} ${point.y}`)
      .join(' '))
    .join(' ')
}

export function architectureActivityEdgePaths(
  route: ArchitectureEdgeRoute,
  activityDirection: ArchitectureEdgeMetadata['activityDirection'],
): string[] {
  if (activityDirection === undefined) return []

  const forwardPath = architectureEdgePath(route)
  const reversePath = architectureEdgePath({
    // CONSTRAINT: Static arrows are consumer -> provider. Live motion is relative to
    // active work, so inputs arrive at the active consumer and outputs leave the
    // active provider; both roles therefore reverse the stored dependency route.
    sections: [...route.sections]
      .reverse()
      .map((section) => [...section].reverse()),
  })

  return activityDirection === 'both'
    ? [forwardPath, reversePath]
    : [reversePath]
}
