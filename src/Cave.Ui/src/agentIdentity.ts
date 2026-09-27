import type { AgentActivity } from './types'

const givenNames = ['Ada', 'Alex', 'Arlo', 'Bea', 'Cleo', 'Dana', 'Eli', 'Emi', 'Finn', 'Iris', 'Jules', 'Kai', 'Kit', 'Lea', 'Leo', 'Luca', 'Maya', 'Milo', 'Mira', 'Nico', 'Noa', 'Nova', 'Owen', 'Remy', 'Riley', 'Robin', 'Sage', 'Sam', 'Theo', 'Toby', 'Wren', 'Zoe']
const familyNames = ['Ash', 'Banks', 'Bay', 'Bell', 'Birch', 'Blake', 'Brooks', 'Cove', 'Dale', 'Ellis', 'Fern', 'Finch', 'Ford', 'Gray', 'Green', 'Hart', 'Hill', 'Lake', 'Lane', 'Lee', 'Oak', 'Park', 'Pine', 'Reed', 'River', 'Ross', 'Rowan', 'Shaw', 'Stone', 'Vale', 'West', 'Woods']

/** Presentation aliases depend only on the observed identity, across views and reloads. */
export function agentDisplayName(agent: Pick<AgentActivity, 'agentId' | 'isSubagent'>): string {
  if (!agent.isSubagent) return 'Main agent'
  let hash = 2166136261
  for (const character of agent.agentId) {
    hash = Math.imul(hash ^ character.charCodeAt(0), 16777619) >>> 0
  }
  // Preserve the remaining hash bits visibly so matching name pairs remain distinguishable.
  return `${givenNames[hash % givenNames.length]} ${familyNames[(hash >>> 5) % familyNames.length]} · ${(hash >>> 10).toString(36)}`
}

/** A declared focus is intent; an observed tool summary is an action, not a subgoal. */
export function agentFocusLabel(agent: AgentActivity): string {
  return (agent.summaryEvidence === 'Declared'
    || (agent.summaryEvidence === undefined && agent.hasDeclaredScope))
    ? 'Current focus / subgoal' : 'Observed action'
}
