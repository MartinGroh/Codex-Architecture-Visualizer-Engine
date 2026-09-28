import { useEffect, useState } from 'react'
import { ArrowLeft, RefreshCw, Route, Smartphone, WifiOff } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { readAgentFlowLight } from './api'
import { MachinePageFrame } from './MachinePageFrame'
import type { AgentFlowLightAgent, AgentFlowLightMainGoal, AgentFlowLightSnapshot } from './types'
import { useWorkspaceOverview } from './useWorkspaceOverview'
import './AgentFlowLightPage.css'

const refreshIntervalMs = 2_000
const requestTimeoutMs = 20_000

/** Shows the same bounded feed as native displays in a phone-friendly browser view. */
export function AgentFlowLightPage() {
  const { workspaces, diagnostics, error: catalogError, isLoading: catalogLoading } = useWorkspaceOverview()
  const [requestedWorkspaceId, setRequestedWorkspaceId] = useState<string | null>(
    () => new URLSearchParams(window.location.search).get('workspace')?.trim() || null,
  )
  const [snapshot, setSnapshot] = useState<AgentFlowLightSnapshot | null>(null)
  const [feedError, setFeedError] = useState<{ workspaceId: string; message: string } | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)
  const availableWorkspaces = workspaces.filter((workspace) => workspace.isAvailable)
  // The URL pins an automatic choice across catalog refreshes as well as deep links.
  const pinnedWorkspaceId = requestedWorkspaceId
    ?? (new URLSearchParams(window.location.search).get('workspace')?.trim() || null)
  // CONSTRAINT: an explicit opaque ID must never fall back to another project's goal.
  const selectedWorkspaceId = pinnedWorkspaceId
    ?? (catalogLoading ? null
      : (availableWorkspaces.find((workspace) => workspace.activeAgentCount > 0) ?? availableWorkspaces[0])?.workspaceId ?? null)
  const selectedWorkspace = availableWorkspaces.find((workspace) => workspace.workspaceId === selectedWorkspaceId)
  const hasSelectedWorkspace = selectedWorkspace !== undefined

  useEffect(() => {
    if (selectedWorkspaceId === null) return
    const url = new URL(window.location.href)
    if (url.searchParams.get('workspace') !== selectedWorkspaceId) {
      url.searchParams.set('workspace', selectedWorkspaceId)
      window.history.replaceState(null, '', url)
    }
  }, [selectedWorkspaceId])

  useEffect(() => {
    if (selectedWorkspaceId === null || !hasSelectedWorkspace) return
    let disposed = false
    let inFlight = false
    let activeRequest: AbortController | null = null

    const refresh = async () => {
      if (disposed || inFlight) return
      inFlight = true
      const request = new AbortController()
      activeRequest = request
      let timedOut = false
      const timeout = window.setTimeout(() => {
        timedOut = true
        request.abort()
      }, requestTimeoutMs)
      try {
        const next = await readAgentFlowLight(selectedWorkspaceId, request.signal)
        if (!disposed) {
          setSnapshot(next)
          setFeedError(null)
        }
      } catch (reason: unknown) {
        if (!disposed) {
          // CONSTRAINT: a failed read must clear any previously shared main goal.
          setSnapshot(null)
          setFeedError({ workspaceId: selectedWorkspaceId, message: timedOut
            ? 'The light feed timed out. Check the CAVE viewer and refresh.'
            : reason instanceof Error ? reason.message : 'The light feed is unavailable.' })
        }
      } finally {
        window.clearTimeout(timeout)
        activeRequest = null
        inFlight = false
      }
    }

    void refresh()
    const interval = window.setInterval(() => void refresh(), refreshIntervalMs)
    return () => {
      disposed = true
      window.clearInterval(interval)
      activeRequest?.abort()
    }
  }, [selectedWorkspaceId, hasSelectedWorkspace, refreshKey])

  const visibleSnapshot = selectedWorkspace !== undefined && snapshot?.workspaceId === selectedWorkspaceId
    ? snapshot
    : null
  const visibleFeedError = feedError?.workspaceId === selectedWorkspaceId ? feedError.message : null
  const activeCount = visibleSnapshot?.agents.filter((agent) => agent.state === 'Active').length ?? 0
  const hiddenCount = visibleSnapshot === null ? 0 : Math.max(0, visibleSnapshot.totalAgentCount - visibleSnapshot.agents.length)

  const selectWorkspace = (workspaceId: string) => {
    setSnapshot(null)
    setFeedError(null)
    setRequestedWorkspaceId(workspaceId)
  }

  return (
    <MachinePageFrame className="agent-flow-light-page">
      <section className="agent-flow-light-content">
        <header className="agent-flow-light-heading">
          <a className="machine-activity-back" href="/"><ArrowLeft size={15} /> Projects</a>
          <div className="agent-flow-light-heading__row">
            <div>
              <h1>Agent Flow Light</h1>
              <p>A clear view of the current goal and agent focus, using the lightweight display feed.</p>
            </div>
            <a className="agent-flow-light-full-link" href="/activity"><Route size={17} /> Full agent flow</a>
          </div>
        </header>

        {catalogError !== null && <div className="dashboard-alert" role="alert"><WifiOff size={17} /><span><strong>Project list is reconnecting.</strong>{catalogError}</span></div>}
        {diagnostics.length > 0 && <div className="dashboard-alert dashboard-alert--warning" role="status"><WifiOff size={17} /><span><strong>Some projects need attention.</strong>{diagnostics.join(' ')}</span></div>}

        {catalogLoading && availableWorkspaces.length === 0 ? (
          <div className="dashboard-empty" role="status">Finding projects…</div>
        ) : availableWorkspaces.length === 0 ? (
          <div className="dashboard-empty"><Smartphone size={26} /><strong>No available projects yet.</strong><span>Open a repository through the CAVE plugin, then return to this view.</span></div>
        ) : (
          <>
            <div className="agent-flow-light-toolbar">
              <label htmlFor="agent-flow-light-workspace">Project</label>
              <select
                id="agent-flow-light-workspace"
                value={selectedWorkspaceId ?? ''}
                onChange={(event) => selectWorkspace(event.target.value)}
              >
                {selectedWorkspaceId === null && <option value="">Choose a project</option>}
                {selectedWorkspaceId !== null && selectedWorkspace === undefined && (
                  <option value={selectedWorkspaceId} disabled>Selected project unavailable</option>
                )}
                {availableWorkspaces.map((workspace) => (
                  <option key={workspace.workspaceId} value={workspace.workspaceId}>{workspace.name}</option>
                ))}
              </select>
              <button type="button" onClick={() => { setSnapshot(null); setFeedError(null); setRefreshKey((value) => value + 1) }} disabled={selectedWorkspace === undefined}>
                <RefreshCw size={16} /> Refresh
              </button>
            </div>

            {visibleFeedError !== null && <div className="dashboard-alert" role="alert"><WifiOff size={17} /><span><strong>Agent Flow Light disconnected.</strong>{visibleFeedError}</span></div>}

            {visibleSnapshot === null ? (
              <div className="agent-flow-light-state" role="status">
                {selectedWorkspace === undefined
                  ? 'This project is unavailable on this CAVE viewer. Choose another project.'
                  : visibleFeedError === null ? 'Reading this project’s light feed…' : 'No current feed is available. Refresh to try again.'}
              </div>
            ) : (
              <div className="agent-flow-light-stack">
                <div className="agent-flow-light-status" role="status">
                  <span className={`agent-flow-light-source ${visibleSnapshot.sourceMode === 'Demo' ? 'is-demo' : ''}`}>
                    {visibleSnapshot.sourceMode === 'Demo' ? 'Sample data' : 'Live source'}
                  </span>
                  <span>{activeCount} active · {visibleSnapshot.totalAgentCount} total</span>
                  <span>Last activity: {visibleSnapshot.sourceUpdatedAtUtc === null
                    ? 'not observed'
                    : <time dateTime={visibleSnapshot.sourceUpdatedAtUtc}>{new Date(visibleSnapshot.sourceUpdatedAtUtc).toLocaleTimeString()}</time>}
                  </span>
                </div>

                {visibleSnapshot.sourceStatus !== 'Ready' && (
                  <div className="dashboard-alert dashboard-alert--warning" role="status">
                    <WifiOff size={17} />
                    <span><strong>{visibleSnapshot.sourceStatus === 'Unobserved' ? 'No agent activity has been observed yet.' : 'Agent activity is degraded.'}</strong>{visibleSnapshot.error}</span>
                  </div>
                )}

                <section className="agent-flow-light-goal" aria-labelledby="agent-flow-light-goal-title">
                  <div className="agent-flow-light-section-heading">
                    <h2 id="agent-flow-light-goal-title">Main goal</h2>
                    {visibleSnapshot.mainGoal.status === 'Ready' && visibleSnapshot.mainGoal.goal !== null && (
                      <span className="agent-flow-light-goal-state">{visibleSnapshot.mainGoal.goal.status}</span>
                    )}
                  </div>
                  <GoalContent mainGoal={visibleSnapshot.mainGoal} />
                </section>

                <section aria-labelledby="agent-flow-light-agents-title">
                  <div className="agent-flow-light-section-heading">
                    <h2 id="agent-flow-light-agents-title">Agents</h2>
                    <span>{visibleSnapshot.agents.length} shown{hiddenCount > 0 ? ` · ${hiddenCount} more` : ''}</span>
                  </div>
                  {visibleSnapshot.agents.length === 0 ? (
                    <p className="agent-flow-light-no-agents">No agent activity has been observed for this project.</p>
                  ) : (
                    <ul className="agent-flow-light-agents">
                      {visibleSnapshot.agents.map((agent) => <AgentRow key={agent.agentId} agent={agent} />)}
                    </ul>
                  )}
                </section>
              </div>
            )}
          </>
        )}
      </section>
    </MachinePageFrame>
  )
}

function GoalContent({ mainGoal }: { mainGoal: AgentFlowLightMainGoal }) {
  if (mainGoal.status === 'Private') return <p className="agent-flow-light-goal-empty">Goal is private. Enable conversation sharing in the project view to show it here.</p>
  if (mainGoal.status === 'Unbound') return <p className="agent-flow-light-goal-empty">No exact Codex task is bound to this project.</p>
  if (mainGoal.status === 'Unavailable') return <p className="agent-flow-light-goal-empty">{mainGoal.error ?? 'The main goal is currently unavailable.'}</p>
  if (mainGoal.goal === null) return <p className="agent-flow-light-goal-empty">No main goal is set for this task.</p>
  return <><p className="agent-flow-light-objective">{mainGoal.goal.objective}</p>{mainGoal.goal.isTruncated && <small>Goal text shortened for this display.</small>}</>
}

function AgentRow({ agent }: { agent: AgentFlowLightAgent }) {
  const active = agent.state === 'Active'
  const focus = agent.currentFocus ?? agent.summary
  const focusLabel = agent.currentFocus !== null ? 'Declared focus'
    : agent.summaryEvidence === 'Observed' ? 'Last observed activity' : 'Activity summary'
  return (
    <li className={`agent-flow-light-agent ${active ? 'is-active' : ''}`}>
      <AgentAvatar phase={agent.phase} active={active} isSubagent={agent.isSubagent} size={38} />
      <div className="agent-flow-light-agent__body">
        <div className="agent-flow-light-agent__identity">
          <strong>{agent.displayName}</strong>
          <span>{active ? agent.phase ?? 'Working' : agent.state}</span>
        </div>
        <small>{focusLabel}</small>
        <p>{focus ?? 'No focus or activity summary yet.'}</p>
      </div>
      <time dateTime={agent.updatedAtUtc} title="Last activity">{new Date(agent.updatedAtUtc).toLocaleTimeString()}</time>
    </li>
  )
}
