import { ChevronDown, ChevronUp } from 'lucide-react'
import { AgentActivitySpotlight } from './AgentActivitySpotlight'
import type { AgentActivity } from './types'

export type AgentActivityPlacementMode = 'canvas' | 'topbar'

interface AgentActivityPlacementProps {
  placement: AgentActivityPlacementMode
  agents: AgentActivity[]
  activeNodeCount: number
  recentEditCount: number
  unmappedCount: number
  isSelected: boolean
  prominent?: boolean
  onSelect: () => void
  onTogglePlacement: () => void
}

export function AgentActivityPlacement({
  placement,
  agents,
  activeNodeCount,
  recentEditCount,
  unmappedCount,
  isSelected,
  prominent = false,
  onSelect,
  onTogglePlacement,
}: AgentActivityPlacementProps) {
  const moveToTopbar = placement === 'canvas'
  const placementLabel = moveToTopbar
    ? 'Move live agent activity to the top bar'
    : 'Move live agent activity to the canvas'

  return (
    <div className={`agent-activity-placement agent-activity-placement--${placement}`}>
      <AgentActivitySpotlight
        agents={agents}
        activeNodeCount={activeNodeCount}
        recentEditCount={recentEditCount}
        unmappedCount={unmappedCount}
        isSelected={isSelected}
        prominent={prominent}
        onSelect={onSelect}
      />
      <button
        className="agent-activity-placement__toggle"
        type="button"
        aria-label={placementLabel}
        title={placementLabel}
        onClick={onTogglePlacement}
      >
        {moveToTopbar ? <ChevronUp size={12} /> : <ChevronDown size={12} />}
      </button>
    </div>
  )
}
