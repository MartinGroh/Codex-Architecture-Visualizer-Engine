import { AgentAvatar } from './AgentAvatar'
import type { AgentActivity } from './types'

export interface AgentActivitySpotlightProps {
  agents: AgentActivity[]
  activeNodeCount: number
  recentEditCount: number
  unmappedCount: number
  isSelected: boolean
  prominent?: boolean
  onSelect: () => void
}

export function AgentActivitySpotlight({
  agents,
  activeNodeCount,
  recentEditCount,
  unmappedCount,
  isSelected,
  prominent = false,
  onSelect,
}: AgentActivitySpotlightProps) {
  const hasMainAgent = agents.some((agent) => !agent.isSubagent)
  const primaryAgent = agents[0]
  const primaryPhase = primaryAgent?.phase ?? 'Working'
  const activeLabel = agents.length === 0
    ? 'No active agents'
    : `${agents.length} active agent${agents.length === 1 ? '' : 's'}`
  const activeDetail = agents
    .map((agent) => `${agent.isSubagent ? agent.agentType.trim() || 'Subagent' : 'Main agent'} ${agent.phase ?? 'Working'}${agent.summary ? `: ${agent.summary}` : ''}`)
    .join('. ')
  const accessibleMetrics = `${activeNodeCount} active nodes, ${recentEditCount} recent edits, ${unmappedCount} unmapped paths`

  return (
    <button
      className={`agent-activity-spotlight ${prominent ? 'is-prominent' : ''} ${agents.length > 0 ? `is-active phase-${primaryPhase.toLowerCase()}` : ''} ${hasMainAgent ? 'has-main-agent' : 'has-subagent'} ${isSelected ? 'is-selected' : ''}`}
      type="button"
      aria-label={`Open live agent activity. ${activeLabel}.${activeDetail ? ` ${activeDetail}.` : ''} ${accessibleMetrics}.`}
      aria-pressed={isSelected}
      onClick={onSelect}
    >
      <span className="agent-activity-spotlight__icon" aria-hidden="true">
        <AgentAvatar
          phase={primaryAgent?.phase ?? null}
          active={agents.length > 0}
          isSubagent={primaryAgent?.isSubagent ?? false}
          size={prominent ? 48 : 36}
        />
        <i />
      </span>

      <span className="agent-activity-spotlight__identity">
        <span className="agent-activity-spotlight__eyebrow">Live agent activity</span>
        {agents.length === 0 ? (
          <strong>{activeLabel}</strong>
        ) : (
          <span className="agent-activity-spotlight__agents" aria-label="Active agent phases">
            {agents.slice(0, 3).map((agent, index) => (
              <span
                key={agent.agentId}
                className={`agent-activity-spotlight__agent ${index === 0 ? 'is-primary' : ''} ${agent.isSubagent ? 'is-subagent' : 'is-main'} phase-${(agent.phase ?? 'Working').toLowerCase()}`}
                title={agent.summary ?? undefined}
              >
                {index > 0 && (
                  <AgentAvatar
                    phase={agent.phase}
                    active
                    isSubagent={agent.isSubagent}
                    size={17}
                  />
                )}
                <span>{agent.isSubagent ? agent.agentType.trim() || 'Subagent' : 'Main agent'}</span>
                <b>{agent.phase ?? 'Working'}</b>
              </span>
            ))}
            {agents.length > 3 && <em>+{agents.length - 3}</em>}
            {agents.length > 1 && (
              <em className="agent-activity-spotlight__topbar-more">+{agents.length - 1}</em>
            )}
          </span>
        )}
        {(primaryAgent?.summary || prominent) && (
          <span
            className="agent-activity-spotlight__summary"
            title={primaryAgent?.summary ?? 'Waiting for the next mapped Codex action.'}
          >
            {primaryAgent?.summary ?? 'Waiting for the next mapped Codex action.'}
          </span>
        )}
      </span>

      <span className="agent-activity-spotlight__metrics" aria-hidden="true">
        <span><b>{agents.length}</b><small>agents</small></span>
        <span><b>{activeNodeCount}</b><small>nodes</small></span>
        <span><b>{recentEditCount}</b><small>edits</small></span>
        <span className={unmappedCount > 0 ? 'has-warning' : ''}><b>{unmappedCount}</b><small>unmapped</small></span>
      </span>
    </button>
  )
}
