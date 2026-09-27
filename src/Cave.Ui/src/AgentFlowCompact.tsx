import { ChevronRight } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { agentDisplayName, agentFocusLabel } from './agentIdentity'
import { visibleLaneMilestones, type MachineActivityLane } from './machineActivityTimeline'

/** Uses the same observed lanes as the full flow, with touch and keyboard disclosure. */
export function AgentFlowCompact({ lanes, active }: { lanes: MachineActivityLane[]; active: boolean }) {
  return (
    <div className="agent-flow-compact" aria-label="Agent goals and timeline details">
      {lanes.map((lane) => {
        const { agent } = lane
        const name = agentDisplayName(agent)
        return (
          <details className="agent-flow-compact__agent" key={agent.agentId}>
            <summary>
              <AgentAvatar phase={agent.phase} active={active} isSubagent={agent.isSubagent} size={32} />
              <span className="agent-flow-compact__caption">
                <strong title={`Display alias for ${agent.agentId}`}>{name}</strong>
                <small>{active ? agent.phase ?? 'Working' : 'Done'} · {agentFocusLabel(agent)}</small>
                <span>{agent.summary ?? 'No focus has been declared yet.'}</span>
              </span>
              <ChevronRight className="agent-flow-compact__chevron" size={18} aria-hidden="true" />
            </summary>
            <div className="agent-flow-compact__detail">
              <p><strong>{agentFocusLabel(agent)}</strong><br />{agent.summary ?? 'No focus has been declared yet.'}</p>
              <ol aria-label={`${name} observed work timeline`}>
                {visibleLaneMilestones(lane, active).map((milestone) => (
                  <li key={milestone.id}>
                    <strong>{milestone.label}</strong>
                    <span>{milestone.detail}</span>
                    <time dateTime={milestone.observedAtUtc}>{new Date(milestone.observedAtUtc).toLocaleTimeString()}</time>
                  </li>
                ))}
              </ol>
              {!active && <p><strong>Completed summary</strong><br />{lane.completionSummary?.text ?? agent.summary ?? 'No final summary was shared.'}{lane.completionSummary?.isTruncated ? ' …' : ''}</p>}
            </div>
          </details>
        )
      })}
    </div>
  )
}
