export interface AgentStatisticsTotals {
  agentCount: number
  subagentCount: number
  taskCount: number
  observedMilliseconds: number
  completedMilliseconds: number | null
  inputTokens: number | null
  cachedInputTokens: number | null
  outputTokens: number | null
  totalTokens: number | null
  reportingAgents: number
  tokenCoverageComplete: boolean
  staleUsageAgents: number
  durationCoverageComplete: boolean
}

export interface AgentStatisticsRow {
  id: string
  label: string
  model: string | null
  reasoningEffort: string | null
  totals: AgentStatisticsTotals
}

export interface AgentStatisticsSnapshot {
  schemaVersion: 1
  isSynthetic: boolean
  scope: 'Overall' | 'Project' | 'Task'
  trackingSinceUtc: string | null
  updatedAtUtc: string | null
  totals: AgentStatisticsTotals
  groups: AgentStatisticsRow[]
  families: AgentStatisticsRow[]
  models: AgentStatisticsRow[]
  efforts: AgentStatisticsRow[]
  projects: AgentStatisticsRow[]
  tasks: AgentStatisticsRow[]
  sharedAgentCount: number
  error: string | null
}

export type StatisticsReader = (taskId: string | null, signal?: AbortSignal) => Promise<AgentStatisticsSnapshot>

export async function readAgentStatistics(workspaceId: string | null, taskId: string | null, signal?: AbortSignal): Promise<AgentStatisticsSnapshot> {
  const query = new URLSearchParams()
  if (workspaceId) query.set('workspace', workspaceId)
  if (taskId) query.set('task', taskId)
  const response = await fetch(`/api/statistics?${query}`, { signal, cache: 'no-store', headers: { Accept: 'application/json' } })
  if (!response.ok) throw new Error(`Statistics request failed (${response.status}).`)
  const result: unknown = await response.json()
  if (typeof result !== 'object' || result === null || !('totals' in result)
    || !('schemaVersion' in result) || result.schemaVersion !== 1
    || !('families' in result) || !Array.isArray(result.families)) throw new Error('Statistics are incompatible with this viewer.')
  return result as AgentStatisticsSnapshot
}

export function statisticsDuration(milliseconds: number | null): string {
  if (milliseconds === null) return 'Unavailable'
  const seconds = Math.floor(milliseconds / 1000)
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ${seconds % 60}s`
  return `${Math.floor(minutes / 60).toLocaleString()}h ${minutes % 60}m`
}
