import { BaseEdge, type EdgeProps } from '@xyflow/react'
import { architectureActivityEdgePaths, architectureEdgePath } from './edgeRouting'
import type { ArchitectureFlowEdge } from './graphPresentation'

export function ArchitectureRouteEdge({
  id,
  data,
  markerStart,
  markerEnd,
  style,
  interactionWidth,
}: EdgeProps<ArchitectureFlowEdge>) {
  if (data === undefined) {
    throw new Error(`Architecture edge '${id}' has no ELK route data.`)
  }

  const path = architectureEdgePath(data.route)
  const activityPaths = architectureActivityEdgePaths(data.route, data.activityDirection)

  return (
    <>
      <BaseEdge
        id={id}
        path={path}
        markerStart={markerStart}
        markerEnd={markerEnd}
        style={style}
        interactionWidth={interactionWidth}
      />
      {activityPaths.map((activityPath, index) => (
        <path
          key={`${id}:activity:${index}`}
          className={`edge-activity-flow edge-activity-flow--${index + 1}`}
          d={activityPath}
          aria-hidden="true"
        />
      ))}
      {activityPaths.flatMap((activityPath, pathIndex) => [0, 1].map((phaseIndex) => (
        <circle
          key={`${id}:signal:${pathIndex}:${phaseIndex}`}
          className={`edge-activity-signal edge-activity-signal--${phaseIndex + 1}`}
          r="4.4"
          aria-hidden="true"
        >
          <animateMotion
            path={activityPath}
            dur="1.8s"
            begin={`${phaseIndex * -0.9}s`}
            calcMode="linear"
            repeatCount="indefinite"
          />
        </circle>
      )))}
    </>
  )
}
