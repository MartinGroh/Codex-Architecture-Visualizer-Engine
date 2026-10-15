import { agentDisplayName, agentFocusLabel } from './agentIdentity'
import { sharedGoal } from './goalPresentation'
import { AgentModelLabel } from './AgentModel'
import { AgentStatisticsPanel } from './AgentStatisticsPanel'
import type { StatisticsReader } from './agentStatistics'
import type { AgentActivity, CodexGoalSnapshot, ConversationOverlay } from './types'

export function MainGoal({ goal, sharingEnabled }: { goal: CodexGoalSnapshot | null; sharingEnabled: boolean }) {
  return (
    <section className="agent-main-goal" aria-label="Current main goal">
      <strong>Main goal {goal?.goal && <small>· {goal.goal.status.replace(/([a-z])([A-Z])/g, '$1 $2')}</small>}</strong>
      <p>{goal?.goal?.objective ?? (!sharingEnabled
        ? 'Goal text is private. Enable conversation sharing to show it.'
        : goal?.status === 'Ready' ? 'No main goal is set for this task.'
          : goal?.error ?? 'The main goal is unavailable for this task.')}</p>
      {goal?.goal && <small>{goal.goal.tokensUsed.toLocaleString()} tokens used{goal.goal.tokenBudget !== null ? ` of ${goal.goal.tokenBudget.toLocaleString()}` : ''} · {Math.floor(goal.goal.timeUsedSeconds / 60)}m elapsed</small>}
    </section>
  )
}

export function AgentWorkContext({ conversation, agents, workspaceId, statisticsReader }: { conversation: ConversationOverlay; agents: AgentActivity[];
  workspaceId?: string | null; statisticsReader?: StatisticsReader }) {
  const goal = sharedGoal(conversation)
  return (
    <details className="architecture-work-context" open>
      <summary>Current work <span>{goal?.goal?.objective ?? `${agents.length} active agent${agents.length === 1 ? '' : 's'}`}</span></summary>
      <div className="architecture-work-context__content">
        <MainGoal goal={goal} sharingEnabled={conversation.sharingEnabled} />
        {agents.length === 0 ? <p>No agents are working right now.</p> : <ul>
          {agents.map((agent) => <li key={agent.agentId}>
            <strong title={`Display alias for ${agent.agentId}`}>{agentDisplayName(agent)} <small>· {agent.phase ?? 'Working'}</small></strong>
            <AgentModelLabel configuration={agent.configuration} />
            <span>{agentFocusLabel(agent)}: {agent.summary ?? 'No focus has been declared yet.'}</span>
          </li>)}
        </ul>}
      </div>
        {(workspaceId || statisticsReader) && <div className="architecture-work-context__statistics">
          <AgentStatisticsPanel title="Project lifetime statistics" workspaceId={workspaceId} reader={statisticsReader} />
          {[...new Set(agents.map(agent => agent.statisticsTaskId).filter((id): id is string => !!id))].map(taskId =>
            <AgentStatisticsPanel key={taskId} title="Current task statistics" workspaceId={workspaceId} taskId={taskId} reader={statisticsReader} />)}
        </div>}
    </details>
  )
}
