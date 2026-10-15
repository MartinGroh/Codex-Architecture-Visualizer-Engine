import { useEffect, useId, useState } from 'react'
import { ArrowLeft, ChartNoAxesCombined, ChevronDown } from 'lucide-react'
import { ModelFamilyIcon } from './AgentModel'
import { effortLabel, modelDisplayName } from './agentModelPresentation'
import { readAgentStatistics, statisticsDuration, type AgentStatisticsRow, type AgentStatisticsSnapshot, type StatisticsReader } from './agentStatistics'
import './AgentStatistics.css'

type Breakdown = 'families' | 'models' | 'efforts' | 'groups' | 'projects' | 'tasks'
type Metric = 'totalTokens' | 'outputTokens' | 'observedMilliseconds' | 'agentCount'

/** The same accounting projection serves task, project and overall comparisons. */
export function AgentStatisticsPanel({ workspaceId = null, taskId = null, title = 'Usage statistics',
  defaultOpen = false, reader }: { workspaceId?: string | null; taskId?: string | null; title?: string;
    defaultOpen?: boolean; reader?: StatisticsReader }) {
  const id = useId()
  const [expanded, setExpanded] = useState(defaultOpen)
  const [snapshot, setSnapshot] = useState<AgentStatisticsSnapshot | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [metric, setMetric] = useState<Metric>('totalTokens')
  const [breakdown, setBreakdown] = useState<Breakdown>(workspaceId === null && taskId === null && !reader ? 'projects' : 'families')
  const [selectedTask, setSelectedTask] = useState<string | null>(taskId)
  const [shown, setShown] = useState(10)
  const [leastFirst, setLeastFirst] = useState(false)

  useEffect(() => {
    if (!expanded) return
    const lifetime = new AbortController()
    let reading = false
    const refresh = async () => {
      if (reading || lifetime.signal.aborted) return
      reading = true
      const request = new AbortController()
      const cancel = () => request.abort()
      lifetime.signal.addEventListener('abort', cancel, { once: true })
      const timeout = window.setTimeout(cancel, 15_000)
      try {
        const next = await (reader ? reader(selectedTask, request.signal) : readAgentStatistics(workspaceId, selectedTask, request.signal))
        if (!lifetime.signal.aborted) { setSnapshot(next); setError(null) }
      } catch (reason: unknown) {
        if (!lifetime.signal.aborted) {
          setSnapshot(null)
          setError(reason instanceof Error ? reason.message : 'Statistics are unavailable.')
        }
      } finally {
        reading = false
        window.clearTimeout(timeout)
        lifetime.signal.removeEventListener('abort', cancel)
      }
    }
    void refresh()
    const interval = window.setInterval(() => void refresh(), 30_000)
    return () => { lifetime.abort(); window.clearInterval(interval) }
  }, [expanded, workspaceId, selectedTask, reader])

  const totals = snapshot?.totals
  const rows = snapshot?.[breakdown] ?? []
  const sorted = [...rows].sort((left, right) => {
    const a = left.totals[metric], b = right.totals[metric]
    return a === null ? b === null ? left.label.localeCompare(right.label) : 1
      : b === null ? -1 : (leastFirst ? a - b : b - a) || left.label.localeCompare(right.label)
  })
  const maximum = Math.max(0, ...rows.map(row => row.totals[metric] ?? 0))
  const denominator = totals?.[metric] ?? null
  const labels: Record<Metric, string> = { totalTokens: 'Total tokens', outputTokens: 'Output tokens', observedMilliseconds: 'Observed agent time', agentCount: 'Agents' }
  const number = (value: number | null | undefined) => value == null ? 'Unavailable' : value.toLocaleString()

  const value = (row: AgentStatisticsRow) => metric === 'observedMilliseconds'
    ? statisticsDuration(row.totals.observedMilliseconds) : number(row.totals[metric])
  return <details className="agent-statistics" open={expanded} onToggle={event => setExpanded(event.currentTarget.open)}>
    <summary><ChartNoAxesCombined size={17} /><strong>{title}</strong><span>{totals ? `${totals.taskCount} task${totals.taskCount === 1 ? '' : 's'} · ${totals.agentCount} agent${totals.agentCount === 1 ? '' : 's'}` : 'Models, effort and recorded usage'}</span><ChevronDown size={16} /></summary>
    {expanded && <div className="agent-statistics__body">
      {selectedTask !== null && <div className="agent-statistics__task-heading">
        {taskId === null && <button type="button" onClick={() => { setSelectedTask(null); setSnapshot(null) }}><ArrowLeft size={14} /> All {workspaceId ? 'project tasks' : 'projects'}</button>}
        <strong>Task and its subagents</strong>
      </div>}
      {error ? <p role="status">{error} Retained activity and the architecture view remain available.</p>
        : !snapshot ? <p role="status">Reading retained statistics…</p>
          : totals?.agentCount === 0 ? <div className="agent-statistics__empty">
              {snapshot.sharedAgentCount > 0 ? <>
                <p>{snapshot.sharedAgentCount} agents are shared with other projects. Their usage is counted once in the overall shared-project group, so this project has no exclusive usage yet.</p>
                {snapshot.tasks.map(task => <button key={task.id} type="button" onClick={() => { setSelectedTask(task.id); setBreakdown('families'); setSnapshot(null) }}>{task.label} · View shared task</button>)}
              </> : <p>No statistics have been collected for this scope yet. Observed tasks will appear here after the first collection.</p>}
            </div>
            : totals && <>
              <dl className="agent-statistics__totals">
                <div><dt>Agents</dt><dd>{number(totals.agentCount)} <small>{number(totals.subagentCount)} subagents</small></dd></div>
                <div><dt>Reported total tokens</dt><dd>{number(totals.totalTokens)}<small>{totals.tokenCoverageComplete ? 'Source-estimated' : `${totals.reportingAgents} of ${totals.agentCount} agents reporting · partial`}</small></dd></div>
                <div><dt>Observed agent time</dt><dd>{statisticsDuration(totals.observedMilliseconds)}<small>Sampled active time, summed across agents</small></dd></div>
              </dl>
              <p className="agent-statistics__coverage">
                {snapshot.isSynthetic && <strong>Synthetic demo statistics. </strong>}
                {snapshot.trackingSinceUtc && <>Tracking since {new Date(snapshot.trackingSinceUtc).toLocaleDateString()}. </>}
                {snapshot.updatedAtUtc && <>Last read {new Date(snapshot.updatedAtUtc).toLocaleTimeString()}. </>}
                {totals.totalTokens === null ? 'Task token counts are unavailable from Codex for this scope. ' : 'Tokens are source estimates; missing counters are excluded. '}
                {totals.staleUsageAgents > 0 && `${totals.staleUsageAgents} agents have retained, stale usage. `}
                {snapshot.sharedAgentCount > 0 && `${snapshot.sharedAgentCount} shared agents are excluded from this project’s exclusive totals. `}
                {snapshot.error}
              </p>
              <div className="agent-statistics__controls">
                <label htmlFor={`${id}-group`}>Group by<select id={`${id}-group`} value={breakdown} onChange={event => { setBreakdown(event.target.value as Breakdown); setShown(10) }}>
                  <option value="families">Model family</option><option value="models">Exact model version</option><option value="efforts">Thinking effort</option><option value="groups">Model and effort</option>
                  {selectedTask === null && <><option value="projects">Projects</option><option value="tasks">Tasks</option></>}
                </select></label>
                <label htmlFor={`${id}-metric`}>Compare<select id={`${id}-metric`} value={metric} onChange={event => setMetric(event.target.value as Metric)}>
                  {Object.entries(labels).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
                </select></label>
                <label htmlFor={`${id}-order`}>Order<select id={`${id}-order`} value={leastFirst ? 'least' : 'most'} onChange={event => setLeastFirst(event.target.value === 'least')}>
                  <option value="most">Most first</option><option value="least">Least first</option>
                </select></label>
              </div>
              <div className="agent-statistics__table-scroll">
                <table><caption className="sr-only">{title}, grouped by {breakdown}, comparing {labels[metric]}</caption>
                  <thead><tr><th scope="col">{breakdown === 'efforts' ? 'Thinking effort' : breakdown === 'projects' ? 'Project' : breakdown === 'tasks' ? 'Task' : 'Model'}</th><th scope="col">{labels[metric]}</th><th scope="col">Share</th><th scope="col">Agents / subagents</th></tr></thead>
                  <tbody>{sorted.slice(0, shown).map(row => {
                    const raw = row.totals[metric]
                    const percentage = raw !== null && denominator !== null && denominator > 0 ? 100 * raw / denominator : null
                    return <tr key={row.id}>
                      <th scope="row"><span className="agent-statistics__row-label">
                        {['families', 'models', 'groups'].includes(breakdown) && <ModelFamilyIcon model={row.model} size={17} />}
                        {breakdown === 'tasks' ? <button type="button" onClick={() => { setSelectedTask(row.id); setBreakdown('families'); setSnapshot(null) }}>{row.label}</button>
                          : breakdown === 'projects' && row.id !== 'shared' ? <a href={`/?workspace=${encodeURIComponent(row.id)}`}>{row.label}</a>
                            : <span>{breakdown === 'efforts' ? effortLabel(row.reasoningEffort) : modelDisplayName(row.label)}{breakdown === 'groups' && <small>{effortLabel(row.reasoningEffort)}</small>}</span>}
                      </span></th>
                      <td><span className="agent-statistics__value">{value(row)}</span><span className="agent-statistics__bar" aria-hidden="true"><i style={{ width: `${raw === null || maximum === 0 ? 0 : 100 * raw / maximum}%` }} /></span></td>
                      <td>{percentage === null ? '—' : `${percentage.toFixed(1)}%`}</td><td>{row.totals.agentCount} / {row.totals.subagentCount}</td>
                    </tr>
                  })}</tbody>
                </table>
              </div>
              {sorted.length > shown && <button className="agent-statistics__more" type="button" onClick={() => setShown(current => current + 20)}>Show more ({sorted.length - shown})</button>}
              <p className="agent-statistics__note">Share uses the selected scope’s {labels[metric].toLowerCase()}{metric.includes('Tokens') && !totals.tokenCoverageComplete ? ' with reported counters' : ''}. An agent can appear in more than one model or effort group, so agent shares can overlap. Time and tokens describe usage, not work quality.</p>
              <details className="agent-statistics__source-detail"><summary>Token and duration details</summary>
                <dl><div><dt>Input tokens</dt><dd>{number(totals.inputTokens)}</dd></div><div><dt>Cached input (part of input)</dt><dd>{number(totals.cachedInputTokens)}</dd></div><div><dt>Output tokens</dt><dd>{number(totals.outputTokens)}</dd></div><div><dt>Recorded completed-turn time</dt><dd>{statisticsDuration(totals.completedMilliseconds)}</dd></div></dl>
                <p>{!totals.durationCoverageComplete && 'Completed-turn duration has partial coverage. '}Completed-turn time may include history from before tracking. Historical time has no model attribution and is kept separate from sampled active time. Gaps and configuration changes are not credited to the latest model.</p>
              </details>
            </>}
    </div>}
  </details>
}
