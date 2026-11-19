import { Check, ChevronRight } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { AgentModelLabel } from './AgentModel'
import { AgentCompletionSummary } from './AgentWorkContext'
import { AgentStatisticsPanel } from './AgentStatisticsPanel'
import { agentDisplayName, agentFocusLabel } from './agentIdentity'
import { LongText } from './LongText'
import { getLongTextExcerpt } from './longTextPreview'
import { agentHistoryMilestones, type MachineActivityLane } from './machineActivityTimeline'

/** Uses the same observed lanes as the full flow, with touch and keyboard disclosure. */
export function AgentFlowCompact({ lanes, openAgentId, onDismiss, workspaceId }: { lanes: MachineActivityLane[]; openAgentId?: string | null; onDismiss: (agentId: string, updatedAtUtc: string) => void; workspaceId?: string }) {
  return (
    <div className="agent-flow-compact" aria-label="Agent goals and timeline details">
      {lanes.map((lane) => {
        const { agent } = lane
        const active = lane.active
        const name = agentDisplayName(agent)
        const focusLabel = agentFocusLabel(agent)
        const focus = agent.summary ?? 'No focus has been declared yet.'
        return (
          <details className="agent-flow-compact__agent" key={agent.agentId} open={openAgentId === agent.agentId ? true : undefined}>
            <summary>
              <AgentAvatar phase={agent.phase} active={active} isSubagent={agent.isSubagent} configuration={agent.configuration ?? null} size={32} />
              <span className="agent-flow-compact__caption">
                <strong title={`Display alias for ${agent.agentId}`}>{name}</strong>
                <AgentModelLabel configuration={agent.configuration} />
                <small>{active ? agent.phase ?? 'Working' : 'Done'} · {focusLabel}</small>
                <span className="agent-flow-compact__excerpt">{getLongTextExcerpt(focus, 'plain')}</span>
              </span>
              <ChevronRight className="agent-flow-compact__chevron" size={18} aria-hidden="true" />
            </summary>
            <div className="agent-flow-compact__detail">
              <strong>{focusLabel}</strong>
              <LongText text={focus} title={focusLabel.toLowerCase()} compact />
              {workspaceId && agent.statisticsTaskId && !agent.isSubagent && <AgentStatisticsPanel title="Task statistics" workspaceId={workspaceId} taskId={agent.statisticsTaskId} />}
              <ol aria-label={`${name} observed work timeline`}>
                {agentHistoryMilestones(lane).map((milestone) => (
                  <li key={milestone.id}>
                    <strong>{milestone.label}</strong>
                    {!(milestone.kind === 'Phase' && milestone.detail === focus) && <LongText text={milestone.detail} title="work detail" compact />}
                    {milestone.filePath && <code className="agent-flow-compact__file">{milestone.filePath}</code>}
                    <time dateTime={milestone.observedAtUtc}>{new Date(milestone.observedAtUtc).toLocaleTimeString()}</time>
                  </li>
                ))}
              </ol>
              {!active && <><strong>Completed summary</strong>
                <AgentCompletionSummary lane={lane} />
                <button type="button" className="machine-project-flow__dismiss" onClick={() => onDismiss(agent.agentId, agent.updatedAtUtc)}><Check size={14} /> Done</button></>}
            </div>
          </details>
        )
      })}
    </div>
  )
}
