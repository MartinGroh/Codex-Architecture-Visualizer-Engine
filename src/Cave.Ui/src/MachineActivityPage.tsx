import { useEffect, useMemo, useState } from 'react'
import {
  Activity,
  ArrowLeft,
  Bot,
  Check,
  CircleAlert,
  Clock3,
  GitCommitHorizontal,
  Info,
  LayoutGrid,
  List,
  MessageSquareText,
  RefreshCw,
  Route,
  Target,
} from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { connectArchitectureFeed } from './api'
import { MachinePageFrame } from './MachinePageFrame'
import {
  buildMachineActivityTimelines,
  type MachineActivityLane,
  type MachineActivityProjectTimeline,
} from './machineActivityTimeline'
import type { LiveArchitectureSnapshot, WorkspaceOverview } from './types'
import { useWorkspaceOverview } from './useWorkspaceOverview'

const clockRefreshMs = 1_000
const machineActivityLayoutKey = 'cave.machine-activity.layout'
type MachineActivityLayout = 'grid' | 'list'

/** Shows the current work streams across every active machine workspace. */
export function MachineActivityPage() {
  const { workspaces, diagnostics, error, isLoading, refresh } = useWorkspaceOverview()
  const activeWorkspaces = useMemo(() => workspaces.filter((workspace) => (
    workspace.isAvailable && workspace.activeAgentCount > 0
  )), [workspaces])
  const activeWorkspaceIdList = useMemo(
    () => activeWorkspaces.map((workspace) => workspace.workspaceId),
    [activeWorkspaces],
  )
  const activeWorkspaceIds = activeWorkspaceIdList.join('|')
  const [retainedWorkspaceIds, setRetainedWorkspaceIds] = useState<string[]>([])
  const [updates, setUpdates] = useState<Record<string, LiveArchitectureSnapshot>>({})
  const [lastActiveUpdates, setLastActiveUpdates] = useState<Record<string, LiveArchitectureSnapshot>>({})
  const [feedErrors, setFeedErrors] = useState<Record<string, string>>({})
  const [layout, setLayout] = useState<MachineActivityLayout>(readMachineActivityLayout)
  const [nowMs, setNowMs] = useState(Date.now)

  useEffect(() => {
    if (activeWorkspaceIdList.length === 0) return

    setRetainedWorkspaceIds((current) => {
      const next = appendWorkspaceIds(current, activeWorkspaceIdList)
      return next.length === current.length && next.every((item, index) => item === current[index])
        ? current
        : next
    })
  }, [activeWorkspaceIdList, activeWorkspaceIds])

  const visibleWorkspaceIdList = useMemo(
    () => appendWorkspaceIds(retainedWorkspaceIds, activeWorkspaceIdList),
    [activeWorkspaceIdList, retainedWorkspaceIds],
  )
  const visibleWorkspaceIds = visibleWorkspaceIdList.join('|')
  const visibleWorkspaces = useMemo(() => {
    const workspacesById = new Map(workspaces.map((workspace) => [workspace.workspaceId, workspace]))
    return visibleWorkspaceIdList.flatMap((workspaceId) => {
      const workspace = workspacesById.get(workspaceId)
      return workspace?.isAvailable === true ? [workspace] : []
    })
  }, [visibleWorkspaceIdList, workspaces])

  useEffect(() => {
    const interval = window.setInterval(() => setNowMs(Date.now()), clockRefreshMs)
    return () => window.clearInterval(interval)
  }, [])

  useEffect(() => {
    const workspaceIds = visibleWorkspaceIds.length === 0 ? [] : visibleWorkspaceIds.split('|')
    let disposed = false
    const disconnectors: Array<() => void> = []

    for (const workspaceId of workspaceIds) {
      void connectArchitectureFeed(
        (update) => {
          if (disposed) return
          setUpdates((current) => ({ ...current, [workspaceId]: update }))
          if (update.snapshot.activity.agents.some((agent) => agent.state === 'Active')) {
            setLastActiveUpdates((current) => ({ ...current, [workspaceId]: update }))
          }
          setFeedErrors((current) => {
            if (!(workspaceId in current)) return current
            const next = { ...current }
            delete next[workspaceId]
            return next
          })
        },
        (reason) => {
          if (!disposed) setFeedErrors((current) => ({ ...current, [workspaceId]: reason.message }))
        },
        () => undefined,
        workspaceId,
      ).then((disconnect) => {
        if (disposed) disconnect()
        else disconnectors.push(disconnect)
      }).catch((reason: unknown) => {
        if (!disposed) setFeedErrors((current) => ({
          ...current,
          [workspaceId]: reason instanceof Error ? reason.message : 'Live activity is unavailable.',
        }))
      })
    }

    return () => {
      disposed = true
      for (const disconnect of disconnectors) disconnect()
    }
  }, [visibleWorkspaceIds])

  const sources = useMemo(() => visibleWorkspaces.flatMap((workspace) => {
    const latestUpdate = updates[workspace.workspaceId]
    const activityUpdate = latestUpdate?.snapshot.activity.agents.some((agent) => agent.state === 'Active')
      ? latestUpdate
      : lastActiveUpdates[workspace.workspaceId] ?? latestUpdate
    if (activityUpdate === undefined) return []

    const update = latestUpdate === undefined || latestUpdate === activityUpdate
      ? activityUpdate
      : {
          ...activityUpdate,
          snapshot: {
            ...activityUpdate.snapshot,
            conversation: latestUpdate.snapshot.conversation,
          },
        }
    return [{ workspace, update }]
  }), [lastActiveUpdates, updates, visibleWorkspaces])
  const timelines = useMemo(
    () => buildMachineActivityTimelines(sources, nowMs),
    [nowMs, sources],
  )
  const activeAgentCount = activeWorkspaces.reduce(
    (total, workspace) => total + workspace.activeAgentCount,
    0,
  )
  const activeSubagentCount = activeWorkspaces.reduce(
    (total, workspace) => total + workspace.activeSubagentCount,
    0,
  )
  const dismissCompletedProject = (workspaceId: string) => {
    setRetainedWorkspaceIds((current) => current.filter((item) => item !== workspaceId))
    setUpdates((current) => omitWorkspace(current, workspaceId))
    setLastActiveUpdates((current) => omitWorkspace(current, workspaceId))
    setFeedErrors((current) => omitWorkspace(current, workspaceId))
  }
  const selectLayout = (nextLayout: MachineActivityLayout) => {
    setLayout(nextLayout)
    try {
      window.localStorage.setItem(machineActivityLayoutKey, nextLayout)
    } catch {
      // The preference is optional when browser storage is unavailable.
    }
  }

  return (
    <MachinePageFrame className="machine-activity-page">
      <section className="dashboard-content machine-activity-content">
        <div className="machine-activity-overview">
          <div className="machine-activity-heading">
            <div>
              <a className="machine-activity-back" href="/"><ArrowLeft size={14} /> Projects</a>
              <span className="dashboard-kicker"><Route size={14} /> Active work across this machine</span>
              <h1>Agent flow</h1>
              <p>Each project prompt is a root; agent lanes show comparable work time and objective CAVE milestones.</p>
            </div>
            <div className="machine-activity-heading__actions">
              <div className="machine-activity-layout-toggle" role="group" aria-label="Project layout">
                <button
                  type="button"
                  className={layout === 'grid' ? 'is-active' : ''}
                  aria-pressed={layout === 'grid'}
                  onClick={() => selectLayout('grid')}
                  title="Show projects side by side"
                >
                  <LayoutGrid size={14} /> Grid
                </button>
                <button
                  type="button"
                  className={layout === 'list' ? 'is-active' : ''}
                  aria-pressed={layout === 'list'}
                  onClick={() => selectLayout('list')}
                  title="Show projects in one column"
                >
                  <List size={14} /> List
                </button>
              </div>
              <button type="button" onClick={() => void refresh()} disabled={isLoading}>
                <RefreshCw size={15} className={isLoading ? 'is-spinning' : ''} /> Refresh
              </button>
            </div>
          </div>

          <section className="machine-activity-summary" aria-label="Active agent summary">
            <span><Bot size={18} /><b>{activeAgentCount}</b><small>active agents</small></span>
            <span><Activity size={18} /><b>{activeWorkspaces.length}</b><small>active projects</small></span>
            <span><Route size={18} /><b>{activeSubagentCount}</b><small>subagents</small></span>
          </section>
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
            <span><strong>Some workspace records need attention.</strong>{diagnostics.join(' ')}</span>
          </div>
        )}

        {isLoading && workspaces.length === 0 ? (
          <div className="dashboard-empty"><span className="loading-orbit"><Route size={22} /></span>Reading active work…</div>
        ) : visibleWorkspaces.length === 0 ? (
          <div className="dashboard-empty machine-activity-empty">
            <Clock3 size={28} />
            <strong>No agents are working right now.</strong>
            <span>The project overview is still available, and this page will populate automatically when a hooked task starts.</span>
            <a href="/">Open project overview</a>
          </div>
        ) : (
          <div className={`machine-activity-projects machine-activity-projects--${layout}`}>
            {visibleWorkspaces.map((workspace) => {
              const timeline = timelines.find((item) => item.workspace.workspaceId === workspace.workspaceId)
              const isActive = workspace.activeAgentCount > 0
              return timeline === undefined ? (
                <ActivityProjectLoading
                  key={workspace.workspaceId}
                  workspace={workspace}
                  error={feedErrors[workspace.workspaceId] ?? null}
                  active={isActive}
                  onDismiss={() => dismissCompletedProject(workspace.workspaceId)}
                />
              ) : (
                <ActivityProjectFlow
                  key={workspace.workspaceId}
                  timeline={timeline}
                  nowMs={nowMs}
                  active={isActive}
                  onDismiss={() => dismissCompletedProject(workspace.workspaceId)}
                />
              )
            })}
          </div>
        )}
      </section>
    </MachinePageFrame>
  )
}

function ActivityProjectLoading({
  workspace,
  error,
  active,
  onDismiss,
}: {
  workspace: WorkspaceOverview
  error: string | null
  active: boolean
  onDismiss: () => void
}) {
  return (
    <section className={`machine-project-flow is-loading ${active ? '' : 'is-complete'}`}>
      <header>
        <div><span className={active ? 'loading-orbit' : ''}><Route size={16} /></span><strong>{workspace.name}</strong></div>
        <ProjectFlowActions workspace={workspace} active={active} onDismiss={onDismiss} />
      </header>
      <p>{error ?? (active
        ? 'Connecting to this project’s live activity feed…'
        : 'Work completed before detailed timeline evidence arrived.')}</p>
    </section>
  )
}

function ActivityProjectFlow({
  timeline,
  nowMs,
  active,
  onDismiss,
}: {
  timeline: MachineActivityProjectTimeline
  nowMs: number
  active: boolean
  onDismiss: () => void
}) {
  return (
    <section className={`machine-project-flow ${active ? '' : 'is-complete'}`}>
      <header>
        <div>
          <span className="machine-project-flow__icon"><Route size={18} /></span>
          <span><strong>{timeline.workspace.name}</strong><small>{timeline.workspace.rootPath}</small></span>
        </div>
        <ProjectFlowActions workspace={timeline.workspace} active={active} onDismiss={onDismiss} />
      </header>

      <div className="machine-prompt-root">
        <span><GitCommitHorizontal size={17} /></span>
        <div>
          <small>Project prompt / instruction</small>
          <strong>{timeline.instructionId === null ? 'Observed task root' : 'Current prompt root'}</strong>
          <p className={timeline.prompt === null ? 'is-private' : ''}>
            {timeline.prompt?.text
              ?? 'Prompt text is not shared. Enable public conversation sharing for this workspace to show it.'}
          </p>
        </div>
        <div className="machine-prompt-root__meta">
          <time>{formatClock(timeline.instructionAtUtc)}</time>
          {timeline.prompt !== null && (
            <span
              className="machine-evidence-trigger"
              tabIndex={0}
              aria-label="Show the full project prompt"
            >
              <MessageSquareText size={13} /> Full prompt
              <span className="machine-evidence-popover" role="tooltip">
                <strong>Shared project prompt</strong>
                <span>{timeline.prompt.text}{timeline.prompt.isTruncated ? ' …' : ''}</span>
                <time>{formatClock(timeline.prompt.occurredAtUtc)}</time>
              </span>
            </span>
          )}
        </div>
      </div>

      <div className="machine-agent-lanes">
        {timeline.lanes.map((lane) => (
          <AgentTimelineLane key={lane.agent.agentId} lane={lane} nowMs={nowMs} active={active} />
        ))}
      </div>
    </section>
  )
}

function ProjectFlowActions({
  workspace,
  active,
  onDismiss,
}: {
  workspace: WorkspaceOverview
  active: boolean
  onDismiss: () => void
}) {
  return (
    <div className="machine-project-flow__actions">
      {!active && (
        <button
          type="button"
          className="machine-project-flow__dismiss"
          onClick={onDismiss}
          title="Remove this completed project from Agent flow"
          aria-label={`Clear completed work for ${workspace.name}`}
        >
          <Check size={14} /> Done
        </button>
      )}
      <a href={`/?workspace=${encodeURIComponent(workspace.workspaceId)}`}>Open architecture</a>
    </div>
  )
}

function AgentTimelineLane({
  lane,
  nowMs,
  active,
}: {
  lane: MachineActivityLane
  nowMs: number
  active: boolean
}) {
  const { agent } = lane
  const phase = agent.phase ?? 'Working'
  const milestones = active
    ? lane.milestones
    : [
        ...lane.milestones.filter((milestone) => milestone.kind !== 'Phase'),
        {
          id: `complete:${agent.agentId}`,
          label: 'Complete',
          detail: 'Work completed',
          observedAtUtc: agent.updatedAtUtc,
          positionPercent: 100,
          kind: 'Complete' as const,
        },
      ]
  const completionText = lane.completionSummary?.text
    ?? agent.summary
    ?? 'The agent completed without a shared final summary.'
  return (
    <article className={`machine-agent-lane phase-${phase.toLowerCase()} ${active ? '' : 'is-complete'}`}>
      <div className="machine-agent-lane__identity">
        <AgentAvatar phase={agent.phase} active={active} isSubagent={agent.isSubagent} size={35} />
        <span>
          <strong>{agent.isSubagent ? agent.agentType.trim() || 'Subagent' : 'Main agent'}</strong>
          <small>{active ? phase : 'Done'} · {formatDuration(lane.durationMs)}</small>
        </span>
        <div className="machine-agent-lane__action">
          <small>{active ? 'Current action' : 'Last observed action'}</small>
          <p>{agent.summary ?? 'Active without a mapped summary.'}</p>
        </div>
      </div>

      <div className="machine-agent-lane__classification">
        <span><Target size={12} /> {agent.hasDeclaredScope ? 'Scope declared' : 'Observed task'}</span>
      </div>

      <div className="machine-agent-track" aria-label={`${agent.agentId} observed work timeline`}>
        <div className="machine-agent-track__line">
          <span
            className="machine-agent-track__progress"
            style={{ width: `${lane.durationPercent}%` }}
            aria-hidden="true"
          />
          {milestones.map((milestone, index) => (
            <span
              key={milestone.id}
              className={`machine-agent-milestone is-${milestone.kind.toLowerCase()} ${milestoneLabelRow(index, milestones.length)} ${index === 0 ? 'is-first' : ''} ${index === milestones.length - 1 ? 'is-last' : ''}`}
              style={{ left: `${milestone.positionPercent}%` }}
              aria-label={`${milestone.label}. ${milestone.detail}. ${formatClock(milestone.observedAtUtc)}`}
              tabIndex={0}
            >
              <i />
              <b>{milestone.label}</b>
              <span className="machine-evidence-popover machine-agent-milestone__popover" role="tooltip">
                <strong><Info size={12} /> Observed work detail</strong>
                <span>{milestone.detail}</span>
                <time>{formatClock(milestone.observedAtUtc)}</time>
              </span>
            </span>
          ))}
        </div>
        <time>{formatClock(active ? new Date(nowMs).toISOString() : agent.updatedAtUtc)}</time>
      </div>

      {!active && (
        <section className="machine-completion-summary" aria-label={`${agent.agentId} completion summary`}>
          <Check size={17} />
          <div>
            <small>Completed summary</small>
            <p>{completionText}{lane.completionSummary?.isTruncated === true ? ' …' : ''}</p>
          </div>
          <time>{formatClock(lane.completionSummary?.occurredAtUtc ?? agent.updatedAtUtc)}</time>
        </section>
      )}
    </article>
  )
}

function appendWorkspaceIds(current: string[], additions: string[]): string[] {
  const known = new Set(current)
  const next = [...current]
  for (const workspaceId of additions) {
    if (known.has(workspaceId)) continue
    known.add(workspaceId)
    next.push(workspaceId)
  }
  return next
}

function readMachineActivityLayout(): MachineActivityLayout {
  try {
    return window.localStorage.getItem(machineActivityLayoutKey) === 'list' ? 'list' : 'grid'
  } catch {
    return 'grid'
  }
}

function omitWorkspace<T>(current: Record<string, T>, workspaceId: string): Record<string, T> {
  if (!(workspaceId in current)) return current

  const next = { ...current }
  delete next[workspaceId]
  return next
}

function milestoneLabelRow(index: number, count: number): string {
  return index > 0 && index < count - 1 && index % 2 === 1
    ? 'label-row-1'
    : 'label-row-0'
}

function formatDuration(durationMs: number): string {
  const totalSeconds = Math.max(0, Math.floor(durationMs / 1_000))
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return minutes > 0 ? `${minutes}m ${seconds}s` : `${seconds}s`
}

function formatClock(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? 'Unknown time'
    : date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
