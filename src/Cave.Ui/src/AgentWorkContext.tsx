import { agentDisplayName, agentFocusLabel } from './agentIdentity'
import { useState } from 'react'
import { sharedGoal } from './goalPresentation'
import { AgentModelLabel } from './AgentModel'
import { CodexText } from './CodexText'
import type { MachineActivityLane } from './machineActivityTimeline'
import { AgentStatisticsPanel } from './AgentStatisticsPanel'
import { LongText } from './LongText'
import { getLongTextExcerpt } from './longTextPreview'
import type { StatisticsReader } from './agentStatistics'
import type { AgentActivity, CodexGoalSnapshot, ConversationOverlay } from './types'

const workContextPreference = 'cave.current-work-expanded'

function readWorkContextPreference() {
  try {
    const preference = window.localStorage.getItem(workContextPreference)
    if (preference !== null) return preference === 'true'
  } catch { /* Storage is optional. */ }
  return false
}

export function AgentCompletionSummary({ lane }: { lane: MachineActivityLane }) {
  const { agent, completionSummary } = lane
  const byline = agentDisplayName(agent)
  if (completionSummary) return <LongText key={completionSummary.occurredAtUtc} title="completed summary" byline={byline}
    text={completionSummary.text} truncated={completionSummary.isTruncated} format="codex" compact>
    <CodexText text={completionSummary.text} />
  </LongText>
  return <LongText title="completed summary" byline={byline}
    text={agent.lastObservedActivity ?? agent.summary ?? 'No work detail was observed before completion.'} compact />
}

export function MainGoal({ goal, sharingEnabled }: { goal: CodexGoalSnapshot | null; sharingEnabled: boolean }) {
  return (
    <section className="agent-main-goal" aria-label="Current main goal">
      <strong>Main goal {goal?.goal && <small>· {goal.goal.status.replace(/([a-z])([A-Z])/g, '$1 $2')}</small>}</strong>
      {goal?.goal ? <div className="agent-main-goal__objective">
        <LongText text={goal.goal.objective} title="main goal" compact />
      </div> : <p>{!sharingEnabled
        ? 'Goal text is private. Enable conversation sharing to show it.'
        : goal?.status === 'Ready' ? 'No main goal is set for this task.'
          : goal?.error ?? 'The main goal is unavailable for this task.'}</p>}
      {goal?.goal && <small>{goal.goal.tokensUsed.toLocaleString()} tokens used{goal.goal.tokenBudget !== null ? ` of ${goal.goal.tokenBudget.toLocaleString()}` : ''} · {Math.floor(goal.goal.timeUsedSeconds / 60)}m elapsed</small>}
    </section>
  )
}

export function AgentWorkContext({ conversation, agents, workspaceId, statisticsReader }: { conversation: ConversationOverlay; agents: AgentActivity[];
  workspaceId?: string | null; statisticsReader?: StatisticsReader }) {
  const goal = sharedGoal(conversation)
  const preview = goal?.goal ? getLongTextExcerpt(goal.goal.objective, 'plain') : `${agents.length} active agent${agents.length === 1 ? '' : 's'}`
  const [expanded, setExpanded] = useState(readWorkContextPreference)
  return (
    <details className="architecture-work-context" open={expanded} onToggle={(event) => {
      const open = event.currentTarget.open
      if (open === expanded) return
      setExpanded(open)
      try { window.localStorage.setItem(workContextPreference, String(open)) } catch { /* Storage is optional. */ }
    }}>
      <summary>Current work <span>{preview}</span></summary>
      <div className="architecture-work-context__content">
        <MainGoal goal={goal} sharingEnabled={conversation.sharingEnabled} />
        {agents.length === 0 ? <p>No agents are working right now.</p> : <ul>
          {agents.map((agent) => <li key={agent.agentId}>
            <strong title={`Display alias for ${agent.agentId}`}>{agentDisplayName(agent)} <small>· {agent.phase ?? 'Working'}</small></strong>
            <AgentModelLabel configuration={agent.configuration} />
            <small>{agentFocusLabel(agent)}</small>
            <div className="architecture-work-context__focus">
              {agent.summary ? <LongText text={agent.summary} title={agentFocusLabel(agent).toLowerCase()} compact /> : 'No focus has been declared yet.'}
            </div>
          </li>)}
        </ul>}
      </div>
        {expanded && (workspaceId || statisticsReader) && <div className="architecture-work-context__statistics">
          <AgentStatisticsPanel title="Project lifetime statistics" workspaceId={workspaceId} reader={statisticsReader} />
          {[...new Set(agents.filter(agent => !agent.parentAgentId || !agents.some(parent =>
            parent.agentId === agent.parentAgentId && !!parent.statisticsTaskId)).map(agent => agent.statisticsTaskId).filter((id): id is string => !!id))].map((taskId, index) =>
            <AgentStatisticsPanel key={taskId} title={`Task ${index + 1} statistics · ${agents.filter(agent => agent.statisticsTaskId === taskId).map(agentDisplayName).join(', ')}`} workspaceId={workspaceId} taskId={taskId} reader={statisticsReader} />)}
        </div>}
    </details>
  )
}
