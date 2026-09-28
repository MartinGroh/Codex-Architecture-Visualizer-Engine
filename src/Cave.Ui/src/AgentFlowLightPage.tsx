import { useEffect, useState } from 'react'
import { ArrowLeft, Check, RefreshCw, Route, Smartphone, WifiOff } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { readAgentFlowLight } from './api'
import { MachinePageFrame } from './MachinePageFrame'
import type { AgentFlowLightAgent, AgentFlowLightMainGoal, AgentFlowLightSnapshot, WorkspaceOverview } from './types'
import { useWorkspaceOverview } from './useWorkspaceOverview'
import './AgentFlowLightPage.css'

const refreshIntervalMs = 5_000
const requestTimeoutMs = 20_000
const completedRetentionMs = 10 * 60_000
const allProjects = 'all'

/** Presents the same bounded device feed as a responsive project and agent hierarchy. */
export function AgentFlowLightPage() {
  const { workspaces, diagnostics, error, isLoading } = useWorkspaceOverview()
  const [selection, setSelection] = useState(() =>
    new URLSearchParams(window.location.search).get('workspace')?.trim() || allProjects)
  const [dismissed, setDismissed] = useState<string[]>([])
  const available = workspaces.filter((workspace) => workspace.isAvailable)
  const selected = selection === allProjects ? available
    : available.filter((workspace) => workspace.workspaceId === selection)

  useEffect(() => {
    const url = new URL(window.location.href)
    url.searchParams.set('workspace', selection)
    window.history.replaceState(null, '', url)
  }, [selection])

  return <MachinePageFrame className="agent-flow-light-page">
    <section className="agent-flow-light-content">
      <header className="agent-flow-light-heading">
        <a className="machine-activity-back" href="/"><ArrowLeft size={15} /> Projects</a>
        <div className="agent-flow-light-heading__row">
          <div><h1>Agent Flow Light</h1>
            <p>Follow main agents and subagents across projects. This view uses the same compact feed as device clients.</p></div>
          <a className="agent-flow-light-full-link" href="/activity"><Route size={17} /> Full agent flow</a>
        </div>
      </header>
      {error !== null && <div className="dashboard-alert" role="alert"><WifiOff size={17} /><span><strong>Project list is reconnecting.</strong>{error}</span></div>}
      {diagnostics.length > 0 && <div className="dashboard-alert dashboard-alert--warning" role="status"><WifiOff size={17} /><span><strong>Some projects need attention.</strong>{diagnostics.join(' ')}</span></div>}
      {isLoading && available.length === 0 ? <div className="dashboard-empty" role="status">Finding projects…</div>
        : available.length === 0 ? <div className="dashboard-empty"><Smartphone size={26} /><strong>No available projects yet.</strong><span>Open a repository through the CAVE plugin, then return to this view.</span></div>
          : <>
            <div className="agent-flow-light-toolbar">
              <label htmlFor="agent-flow-light-workspace">Project</label>
              <select id="agent-flow-light-workspace" value={selection} onChange={(event) => setSelection(event.target.value)}>
                <option value={allProjects}>All projects</option>
                {selection !== allProjects && selected.length === 0 && <option value={selection} disabled>Selected project unavailable</option>}
                {available.map((workspace) => <option key={workspace.workspaceId} value={workspace.workspaceId}>{workspace.name}</option>)}
              </select>
            </div>
            {selection !== allProjects && selected.length === 0
              ? <div className="agent-flow-light-state" role="status">This project is unavailable on this CAVE viewer. Choose another project.</div>
              : <div className="agent-flow-light-projects">
                {selected.map((workspace) => <WorkspaceFlow key={workspace.workspaceId} workspace={workspace}
                  dismissed={dismissed} onDismiss={(keys) => setDismissed((current) => [...current, ...keys])} />)}
              </div>}
          </>}
    </section>
  </MachinePageFrame>
}

function WorkspaceFlow({ workspace, dismissed, onDismiss }: { workspace: WorkspaceOverview; dismissed: string[]; onDismiss: (keys: string[]) => void }) {
  const [snapshot, setSnapshot] = useState<AgentFlowLightSnapshot | null>(null)
  const [feedError, setFeedError] = useState<string | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const [nowMs, setNowMs] = useState(Date.now)

  useEffect(() => {
    const interval = window.setInterval(() => setNowMs(Date.now()), 1_000)
    return () => window.clearInterval(interval)
  }, [])
  useEffect(() => {
    let disposed = false
    let inFlight = false
    let activeRequest: AbortController | null = null
    const refresh = async () => {
      if (disposed || inFlight) return
      inFlight = true
      const request = new AbortController()
      activeRequest = request
      let timedOut = false
      const timeout = window.setTimeout(() => { timedOut = true; request.abort() }, requestTimeoutMs)
      try {
        const next = await readAgentFlowLight(workspace.workspaceId, request.signal)
        if (!disposed) { setSnapshot(next); setFeedError(null) }
      } catch (reason: unknown) {
        if (!disposed) {
          // Failed reads must remove this project's previously shared goal.
          setSnapshot(null)
          setFeedError(timedOut ? 'The light feed timed out.' : reason instanceof Error ? reason.message : 'The light feed is unavailable.')
        }
      } finally {
        window.clearTimeout(timeout)
        activeRequest = null
        inFlight = false
      }
    }
    void refresh()
    const interval = window.setInterval(() => void refresh(), refreshIntervalMs)
    return () => { disposed = true; window.clearInterval(interval); activeRequest?.abort() }
  }, [workspace.workspaceId, refreshKey])

  const current = snapshot?.workspaceId === workspace.workspaceId ? snapshot : null
  const visible = current?.agents.filter((agent) => {
    if (agent.state === 'Active' || agent.state === 'Planned') return true
    const age = nowMs - Date.parse(agent.updatedAtUtc)
    return Number.isFinite(age) && age >= 0 && age < completedRetentionMs
      && !dismissed.includes(dismissKey(workspace.workspaceId, agent))
  }) ?? []
  const main = visible.filter((agent) => !agent.isSubagent)
  const children = visible.filter((agent) => agent.isSubagent)
  const unlinkedChildren = children.filter((agent) => !main.some((parent) => agent.parentAgentId === parent.agentId))
  const finished = visible.filter((agent) => agent.state === 'Completed' || agent.state === 'Idle')
  const dismiss = (agent: AgentFlowLightAgent) => onDismiss([dismissKey(workspace.workspaceId, agent)])

  return <section className="agent-flow-light-project" aria-label={`${workspace.name} agent flow`}>
    <header className="agent-flow-light-project__header">
      <div><h2>{workspace.name}</h2><span>{current?.agents.filter((agent) => agent.state === 'Active').length ?? 0} active</span></div>
      <div className="agent-flow-light-project__actions">
        {finished.length > 0 && <button type="button" onClick={() => onDismiss(finished.map((agent) => dismissKey(workspace.workspaceId, agent)))}><Check size={15} /> Clear finished</button>}
        <button type="button" onClick={() => { setSnapshot(null); setFeedError(null); setRefreshKey((key) => key + 1) }}><RefreshCw size={15} /> Refresh</button>
      </div>
    </header>
    {feedError !== null && <div className="dashboard-alert" role="alert"><WifiOff size={17} /><span><strong>Agent Flow Light disconnected.</strong>{feedError}</span></div>}
    {current === null ? <p className="agent-flow-light-state" role="status">{feedError === null ? 'Reading this project’s light feed…' : 'No current feed is available. Refresh to try again.'}</p> : <>
      <div className="agent-flow-light-status" role="status">
        <span className={`agent-flow-light-source ${current.sourceMode === 'Demo' ? 'is-demo' : ''}`}>{current.sourceMode === 'Demo' ? 'Sample data' : 'Live source'}</span>
        <span>{current.totalAgentCount} observed · {visible.length} shown</span>
        <span>Last activity: {current.sourceUpdatedAtUtc === null ? 'not observed' : <time dateTime={current.sourceUpdatedAtUtc}>{new Date(current.sourceUpdatedAtUtc).toLocaleTimeString()}</time>}</span>
      </div>
      {current.sourceStatus !== 'Ready' && <div className="dashboard-alert dashboard-alert--warning" role="status"><WifiOff size={17} /><span><strong>{current.sourceStatus === 'Unobserved' ? 'No agent activity has been observed yet.' : 'Agent activity is degraded.'}</strong>{current.error}</span></div>}
      <section className="agent-flow-light-goal" aria-label="Main goal">
        <div className="agent-flow-light-section-heading"><h3>Main goal</h3>{current.mainGoal.status === 'Ready' && current.mainGoal.goal !== null && <span className="agent-flow-light-goal-state">{current.mainGoal.goal.status}</span>}</div>
        <GoalContent mainGoal={current.mainGoal} />
      </section>
      <div className="agent-flow-light-tree">
        {main.length === 0 && <AgentGroup title="Main agent" agents={[]} workspaceId={workspace.workspaceId} onDismiss={dismiss} />}
        {main.map((parent) => <div className="agent-flow-light-branch" key={parent.agentId}>
          <AgentGroup title="Main agent" agents={[parent]} workspaceId={workspace.workspaceId} onDismiss={dismiss} />
          {children.some((child) => child.parentAgentId === parent.agentId) &&
            <div className="agent-flow-light-children"><AgentGroup title="Subagents" agents={children.filter((child) => child.parentAgentId === parent.agentId)} workspaceId={workspace.workspaceId} onDismiss={dismiss} /></div>}
        </div>)}
        {unlinkedChildren.length > 0 && <div className="agent-flow-light-children"><AgentGroup title="Subagents without an observed parent" agents={unlinkedChildren} workspaceId={workspace.workspaceId} onDismiss={dismiss} /></div>}
      </div>
    </>}
  </section>
}

function dismissKey(workspaceId: string, agent: AgentFlowLightAgent): string { return `${workspaceId}:${agent.agentId}:${agent.updatedAtUtc}` }

function AgentGroup({ title, agents, workspaceId, onDismiss }: { title: string; agents: AgentFlowLightAgent[]; workspaceId: string; onDismiss: (agent: AgentFlowLightAgent) => void }) {
  return <section aria-label={title}>
    <div className="agent-flow-light-section-heading"><h3>{title}</h3><span>{agents.length}</span></div>
    {agents.length === 0 ? <p className="agent-flow-light-no-agents">No {title.toLowerCase()} currently visible.</p>
      : <ul className="agent-flow-light-agents">{agents.map((agent) => <AgentRow key={agent.agentId} workspaceId={workspaceId} agent={agent} onDismiss={() => onDismiss(agent)} />)}</ul>}
  </section>
}

function GoalContent({ mainGoal }: { mainGoal: AgentFlowLightMainGoal }) {
  if (mainGoal.status === 'Private') return <p className="agent-flow-light-goal-empty">Goal is private. Enable conversation sharing in the project view to show it here.</p>
  if (mainGoal.status === 'Unbound') return <p className="agent-flow-light-goal-empty">No exact Codex task is bound to this project.</p>
  if (mainGoal.status === 'Unavailable') return <p className="agent-flow-light-goal-empty">{mainGoal.error ?? 'The main goal is currently unavailable.'}</p>
  if (mainGoal.goal === null) return <p className="agent-flow-light-goal-empty">No main goal is set for this task.</p>
  return <><p className="agent-flow-light-objective">{mainGoal.goal.objective}</p>{mainGoal.goal.isTruncated && <small>Goal text shortened for this display.</small>}</>
}

function AgentRow({ agent, workspaceId, onDismiss }: { agent: AgentFlowLightAgent; workspaceId: string; onDismiss: () => void }) {
  const active = agent.state === 'Active'
  const finished = agent.state === 'Completed' || agent.state === 'Idle'
  const focusLabel = agent.currentFocus !== null ? 'Declared focus'
    : agent.summaryEvidence === 'Observed' ? 'Last observed activity' : 'Activity summary'
  return <li className={`agent-flow-light-agent ${active ? 'is-active' : ''}`}>
    <AgentAvatar phase={agent.phase} active={active} isSubagent={agent.isSubagent} size={38} />
    <div className="agent-flow-light-agent__body">
      <div className="agent-flow-light-agent__identity"><strong>{agent.displayName}</strong><span>{active ? agent.phase ?? 'Working' : agent.state}</span></div>
      <small>{focusLabel}</small><p>{agent.currentFocus ?? agent.summary ?? 'No work detail was observed.'}</p>
      {agent.currentFocus !== null && <><small>Last observed activity</small><p>{agent.lastObservedActivity ?? 'No work action was observed.'}</p></>}
      <a href={`/activity?workspace=${encodeURIComponent(workspaceId)}&agent=${encodeURIComponent(agent.agentId)}`}>View flow as list</a>
    </div>
    <div className="agent-flow-light-agent__trailing">
      <time dateTime={agent.updatedAtUtc} title="Last activity">{new Date(agent.updatedAtUtc).toLocaleTimeString()}</time>
      {finished && <button type="button" onClick={onDismiss} aria-label={`Dismiss ${agent.displayName}`} title="Hide this finished agent"><Check size={15} /></button>}
    </div>
  </li>
}
