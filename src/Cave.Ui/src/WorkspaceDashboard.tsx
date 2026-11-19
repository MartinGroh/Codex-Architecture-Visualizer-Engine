import {
  Boxes,
  ChevronRight,
  CircleAlert,
  FolderOpen,
  RefreshCw,
  Route,
  Smartphone,
  UsersRound,
} from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { AgentModelLabel } from './AgentModel'
import { AgentStatisticsPanel } from './AgentStatisticsPanel'
import { MachinePageFrame } from './MachinePageFrame'
import type { WorkspaceOverview } from './types'
import { useWorkspaceOverview } from './useWorkspaceOverview'
import './MachineOverview.css'

export function WorkspaceDashboard() {
  const { hostName, workspaces, diagnostics, error, isLoading, refresh } = useWorkspaceOverview()

  return (
    <MachinePageFrame className="workspace-dashboard">
      <section className="dashboard-content">
        <header className="dashboard-heading">
          <div>
            <h1>Projects</h1>
            {hostName && <p>Live on {hostName}</p>}
          </div>
          <div className="dashboard-heading__actions">
            <a href="/activity"><Route size={15} /> Agent flow</a>
            <a href="/activity/light"><Smartphone size={15} /> Agent Flow Light</a>
            <button type="button" onClick={() => void refresh()} disabled={isLoading}>
              <RefreshCw size={15} className={isLoading ? 'is-spinning' : ''} /> Refresh
            </button>
          </div>
        </header>

        {error !== null && (
          <div className="dashboard-alert" role="alert">
            <CircleAlert size={17} />
            <span><strong>The CAVE service is reconnecting.</strong>{error}</span>
          </div>
        )}

        {diagnostics.length > 0 && (
          <div className="dashboard-alert dashboard-alert--warning" role="status">
            <CircleAlert size={17} />
            <span><strong>Some local workspace records need attention.</strong>{diagnostics.join(' ')}</span>
          </div>
        )}

        <div className="workspace-dashboard__body">
          {isLoading && workspaces.length === 0 ? (
            <div className="dashboard-empty"><span className="loading-orbit"><Boxes size={22} /></span>Reading machine workspaces…</div>
          ) : workspaces.length === 0 ? (
            <div className="dashboard-empty">
              <FolderOpen size={28} />
              <strong>No CAVE workspaces have been observed yet.</strong>
              <span>Open a repository through the CAVE plugin in Codex to add it here.</span>
            </div>
          ) : (
            <div className="workspace-grid">
              {workspaces.map((workspace) => <WorkspaceCard key={workspace.workspaceId} workspace={workspace} />)}
            </div>
          )}
          <AgentStatisticsPanel title="All project statistics" defaultOpen />
        </div>
      </section>
    </MachinePageFrame>
  )
}

function WorkspaceCard({ workspace }: { workspace: WorkspaceOverview }) {
  const hasActiveAgents = workspace.activeAgentCount > 0
  const hasOnlySubagents = hasActiveAgents && workspace.activeAgentCount === workspace.activeSubagentCount
  const phaseToken = (workspace.currentPhase ?? 'Working').toLowerCase()
  const summary = workspace.activitySummary ?? (hasActiveAgents ? 'Agent activity is live' : 'No recent agent activity')
  const content = (
    <>
      <div className="workspace-card__header">
        <span className={`workspace-card__icon ${hasActiveAgents ? `is-active phase-${phaseToken}` : ''}`}>
          {hasActiveAgents
            ? <AgentAvatar phase={workspace.currentPhase} configuration={workspace.currentAgentConfiguration ?? null} active isSubagent={hasOnlySubagents} size={29} />
            : <Boxes size={20} />}
        </span>
        <div className="workspace-card__identity">
          <h2>{workspace.name}</h2>
          <p title={workspace.rootPath}>{workspace.rootPath}</p>
        </div>
        {!workspace.isAvailable && <span className="workspace-availability is-unavailable">Missing</span>}
      </div>
      <div className="workspace-card__activity">
        <div>
          <strong>{hasActiveAgents ? workspace.currentPhase ?? 'Working' : 'Quiet'}</strong>
          {hasActiveAgents && <AgentModelLabel configuration={workspace.currentAgentConfiguration} />}
        </div>
        <p title={summary}>{summary}</p>
      </div>
      <footer>
        <span>{workspace.activeAgentCount} active</span>
        <span><UsersRound size={14} /> {workspace.activeSubagentCount} {workspace.activeSubagentCount === 1 ? 'subagent' : 'subagents'}</span>
        {workspace.isAvailable && <ChevronRight size={17} className="workspace-card__open" />}
      </footer>
    </>
  )

  return workspace.isAvailable ? (
    <a className={`workspace-card ${hasActiveAgents ? `is-active phase-${phaseToken}` : ''}`} href={`/?workspace=${encodeURIComponent(workspace.workspaceId)}`}>
      {content}
    </a>
  ) : (
    <div className="workspace-card is-unavailable" aria-disabled="true">{content}</div>
  )
}
