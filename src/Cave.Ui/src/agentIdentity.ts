import type { AgentActivity } from './types'

/** All views consume the application-owned name; unenriched evidence uses its role label. */
export function agentDisplayName(agent: Pick<AgentActivity, 'isSubagent' | 'displayName'>): string {
  return agent.displayName ?? (agent.isSubagent ? 'Subagent' : 'Main agent')
}

/** A declared focus is intent; an observed tool summary is an action, not a subgoal. */
export function agentFocusLabel(agent: AgentActivity): string {
  return agent.summaryEvidence === 'Declared'
    ? 'Current focus / subgoal'
    : agent.summaryEvidence === 'Observed' ? 'Observed action' : 'Work summary'
}
