import {
  Activity,
  Boxes,
  ChevronRight,
  CircleAlert,
  Clock3,
  FolderOpen,
  RefreshCw,
  Route,
  UsersRound,
} from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { MachinePageFrame } from './MachinePageFrame'
import type { WorkspaceOverview } from './types'
import { useWorkspaceOverview } from './useWorkspaceOverview'

export function WorkspaceDashboard() {
  const { hostName, workspaces, diagnostics, error, isLoading, refresh } = useWorkspaceOverview()

  return (
    <MachinePageFrame className="workspace-dashboard">
      <section className="dashboard-content">
        <div className="dashboard-heading">
          <div>
            <span className="dashboard-kicker"><Activity size={14} /> Live on {hostName ?? 'CAVE host'}</span>
            <h1>Your architecture workspaces</h1>
            <p>Choose a project to open its live architecture, Git, and agent activity view.</p>
          </div>
          <div className="dashboard-heading__actions">
            <a href="/activity"><Route size={15} /> Agent flow</a>
            <button type="button" onClick={() => void refresh()} disabled={isLoading}>
              <RefreshCw size={15} className={isLoading ? 'is-spinning' : ''} /> Refresh
            </button>
          </div>
        </div>

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

        {isLoading && workspaces.length === 0 ? (
          <div className="dashboard-empty"><span className="loading-orbit"><Boxes size={22} /></span>Reading machine workspaces…</div>
        ) : workspaces.length === 0 ? (
          <div className="dashboard-empty">
            <FolderOpen size={28} />
            <strong>No CAVE workspaces have been observed yet.</strong>
            <span>Open a repository through the CAVE plugin in Codex; it will appear here automatically.</span>
          </div>
        ) : (
          <div className="workspace-grid">
            {workspaces.map((workspace) => <WorkspaceCard key={workspace.workspaceId} workspace={workspace} />)}
          </div>
        )}
      </section>
    </MachinePageFrame>
  )
}

function WorkspaceCard({ workspace }: { workspace: WorkspaceOverview }) {
  const hasActiveAgents = workspace.activeAgentCount > 0
  const hasOnlySubagents = hasActiveAgents && workspace.activeAgentCount === workspace.activeSubagentCount
  const phaseToken = (workspace.currentPhase ?? 'Working').toLowerCase()
  const content = (
    <>
      <div className="workspace-card__header">
        <span className={`workspace-card__icon ${hasActiveAgents ? `is-active phase-${phaseToken}` : ''}`}>
          {hasActiveAgents
            ? <AgentAvatar phase={workspace.currentPhase} active isSubagent={hasOnlySubagents} size={29} />
            : <Boxes size={20} />}
        </span>
        <span className={`workspace-availability ${workspace.isAvailable ? '' : 'is-unavailable'}`}>
          {workspace.isAvailable ? 'Available' : 'Missing'}
        </span>
      </div>
      <div className="workspace-card__identity">
        <h2>{workspace.name}</h2>
        <p title={workspace.rootPath}>{workspace.rootPath}</p>
      </div>
      <div className="workspace-card__activity">
        {hasActiveAgents ? (
          <>
            <AgentAvatar phase={workspace.currentPhase} active isSubagent={hasOnlySubagents} size={17} />
            <strong>{workspace.currentPhase ?? 'Working'}</strong>
            <span>{workspace.activitySummary ?? 'Agent activity is live'}</span>
          </>
        ) : (
          <>
            <Clock3 size={14} />
            <strong>Quiet</strong>
            <span>{workspace.activitySummary ?? 'No recent agent activity'}</span>
          </>
        )}
      </div>
      <footer>
        <span><AgentAvatar phase={workspace.currentPhase} active={hasActiveAgents} isSubagent={hasOnlySubagents} size={15} /> {workspace.activeAgentCount} active</span>
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
