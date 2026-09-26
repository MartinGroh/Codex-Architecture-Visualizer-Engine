import type { WorkspaceOverview } from './types'
import { AgentAvatar } from './AgentAvatar'
import { useWorkspaceOverview } from './useWorkspaceOverview'

export interface WorkspaceRailProps {
  workspaces: WorkspaceOverview[]
  currentWorkspaceId: string
  expanded?: boolean
}

export function WorkspaceRailCatalog({
  currentWorkspaceId,
  expanded,
}: {
  currentWorkspaceId: string
  expanded: boolean
}) {
  const { workspaces } = useWorkspaceOverview()
  return (
    <WorkspaceRail
      workspaces={workspaces}
      currentWorkspaceId={currentWorkspaceId}
      expanded={expanded}
    />
  )
}

/** Renders compact machine-workspace navigation without duplicating catalog activity semantics. */
export function WorkspaceRail({
  workspaces,
  currentWorkspaceId,
  expanded = false,
}: WorkspaceRailProps) {
  if (workspaces.length === 0) return null

  return (
    <div className="workspace-rail-switcher">
      <span className="workspace-rail-separator" aria-hidden="true" />
      <nav className="workspace-rail-list" aria-label="Machine projects">
        {workspaces.map((workspace) => (
          <WorkspaceRailItem
            key={workspace.workspaceId}
            workspace={workspace}
            isCurrent={workspace.workspaceId === currentWorkspaceId}
            expanded={expanded}
          />
        ))}
      </nav>
    </div>
  )
}

function WorkspaceRailItem({
  workspace,
  isCurrent,
  expanded,
}: {
  workspace: WorkspaceOverview
  isCurrent: boolean
  expanded: boolean
}) {
  const hasActiveAgents = workspace.activeAgentCount > 0
  const hasActiveMainAgent = workspace.activeAgentCount > workspace.activeSubagentCount
  const phaseToken = (workspace.currentPhase ?? 'Working').toLowerCase()
  const status = workspaceStatusText(workspace)
  const className = [
    'workspace-rail-project',
    isCurrent ? 'is-current' : '',
    hasActiveAgents ? 'is-active' : 'is-quiet',
    hasActiveAgents ? `phase-${phaseToken}` : '',
    hasActiveAgents && !hasActiveMainAgent ? 'is-subagent-only' : '',
    workspace.isAvailable ? '' : 'is-unavailable',
  ].filter(Boolean).join(' ')
  const content = (
    <>
      <span className="workspace-rail-project__monogram" aria-hidden="true">
        {workspaceMonogram(workspace.name)}
      </span>
      {expanded && (
        <span className="workspace-rail-project__name" aria-hidden="true">
          {workspace.name}
        </span>
      )}
      <span className="workspace-rail-project__status" aria-hidden="true">
        {hasActiveAgents && (
          <AgentAvatar
            phase={workspace.currentPhase}
            active
            isSubagent={!hasActiveMainAgent}
            size={expanded ? 28 : 18}
          />
        )}
      </span>
    </>
  )
  const label = `${workspace.name}. ${status}`
  const title = `${workspace.name}\n${status}${workspace.activitySummary ? `\n${workspace.activitySummary}` : ''}`

  return workspace.isAvailable ? (
    <a
      className={className}
      href={`/?workspace=${encodeURIComponent(workspace.workspaceId)}`}
      aria-label={label}
      aria-current={isCurrent ? 'page' : undefined}
      title={title}
    >
      {content}
    </a>
  ) : (
    <span className={className} aria-label={label} aria-disabled="true" title={title}>
      {content}
    </span>
  )
}

function workspaceStatusText(workspace: WorkspaceOverview): string {
  if (!workspace.isAvailable) return 'Workspace unavailable'
  if (workspace.activeAgentCount === 0) return 'Quiet. No active agents'

  const agentLabel = `${workspace.activeAgentCount} active agent${workspace.activeAgentCount === 1 ? '' : 's'}`
  return `${agentLabel}. ${workspace.currentPhase ?? 'Working'}`
}

function workspaceMonogram(name: string): string {
  const words = name.trim().split(/[\s._-]+/).filter(Boolean)
  if (words.length === 0) return '•'
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase()
  return `${words[0][0]}${words[1][0]}`.toUpperCase()
}
