import { useMemo, useState } from 'react'
import { ArrowLeft, Maximize2, Minimize2 } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { AgentChatFeed } from './AgentChatFeed'
import { AgentModelLabel } from './AgentModel'
import { useAgentChat, type AgentChatAgent } from './agentChat'
import { agentDisplayName } from './agentIdentity'
import type { MachineActivityLane } from './machineActivityTimeline'
import './AgentWall.css'

interface AgentWallProps {
  workspaceId: string
  taskId: string
  lanes: MachineActivityLane[]
  sharingEnabled: boolean | null
  onBack: () => void
}

interface WallAgent {
  id: string
  name: string
  startedAtUtc: string | null
  lane?: MachineActivityLane
  chat?: AgentChatAgent
}

/** Displays one task's verified root and descendants with a single shared request loop. */
export function AgentWall(props: AgentWallProps) {
  return <TaskAgentWall key={JSON.stringify([props.workspaceId, props.taskId])} {...props} />
}

function TaskAgentWall({ workspaceId, taskId, lanes, sharingEnabled, onBack }: AgentWallProps) {
  const chat = useAgentChat(workspaceId, taskId, sharingEnabled)
  const agents = useMemo(() => {
    const entries = new Map<string, WallAgent>()
    for (const lane of lanes) {
      const id = lane.agent.publicId ?? lane.agent.agentId
      entries.set(id, { id, lane, name: agentDisplayName(lane.agent), startedAtUtc: lane.agent.startedAtUtc })
    }
    for (const agent of chat.agents) {
      const existing = entries.get(agent.id)
      entries.set(agent.id, existing ? { ...existing, chat: agent } : {
        id: agent.id, name: agent.displayName, startedAtUtc: agent.startedAtUtc, chat: agent,
      })
    }
    return [...entries.values()].sort((left, right) => {
      if (left.id === taskId) return -1
      if (right.id === taskId) return 1
      return startTime(left) - startTime(right) || left.id.localeCompare(right.id)
    })
  }, [chat.agents, lanes, taskId])

  const [order, setOrder] = useState<string[]>([])
  const [selectedId, setSelectedId] = useState(taskId)
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [enabling, setEnabling] = useState(false)
  const [sharingError, setSharingError] = useState<string | null>(null)

  const known = new Set(order)
  const added = agents.map(({ id }) => id).filter((id) => !known.has(id) && id !== taskId)
  if (added.length > 0) setOrder([...order, ...added])

  const byId = new Map(agents.map((agent) => [agent.id, agent]))
  const visibleAgents = [...new Set([taskId, ...order, ...byId.keys()])].flatMap((id) => {
    const agent = byId.get(id)
    return agent ? [agent] : []
  })
  const expanded = expandedId && byId.has(expandedId) ? expandedId : null
  const selected = expanded ?? (byId.has(selectedId) ? selectedId : visibleAgents[0]?.id)
  const nameCounts = new Map<string, number>()
  visibleAgents.forEach(({ name }) => nameCounts.set(name, (nameCounts.get(name) ?? 0) + 1))

  const enableSharing = async () => {
    setEnabling(true)
    setSharingError(null)
    try {
      await chat.enableSharing()
    } catch {
      setSharingError('Could not enable live agent chats.')
    } finally {
      setEnabling(false)
    }
  }

  const notice = chat.sourceMode === 'Demo' ? 'Demo'
    : chat.sharing === 'Private' ? 'Conversation sharing is off.'
    : chat.sharing === 'Legacy' ? 'Showing previously shared messages.'
    : chat.status === 'Partial' ? 'Some agent conversations are unavailable.'
    : null

  return <section className="agent-wall" aria-label="Agent wall">
    <div className="agent-wall__toolbar">
      <button type="button" onClick={onBack}><ArrowLeft size={15} aria-hidden="true" /> Back to flow</button>
      {visibleAgents.length > 0 && <select
        className="agent-wall__agent-selector"
        aria-label="Agent conversation"
        value={selected}
        onChange={(event) => { setExpandedId(null); setSelectedId(event.currentTarget.value) }}
      >
        {visibleAgents.map((agent, index) => <option key={agent.id} value={agent.id}>
          {agent.name}{agent.id === taskId
            ? agent.name === 'Main agent' ? '' : ' · Main agent'
            : (nameCounts.get(agent.name) ?? 0) > 1 ? ' · Agent ' + (index + 1) : ''}
        </option>)}
      </select>}
      {notice && <span className="agent-wall__notice">{notice}</span>}
      {chat.sourceMode !== 'Demo' && (chat.sharing === 'Legacy' || chat.sharing === 'Private') && <button
        type="button"
        className="agent-wall__enable"
        disabled={enabling}
        onClick={() => void enableSharing()}
      >Enable live agent chats</button>}
      {sharingError && <span className="agent-wall__error" role="alert">{sharingError}</span>}
    </div>

    {visibleAgents.length === 0 ? <p className="agent-wall__empty" role="status">{chat.status === 'Loading'
      ? 'Loading the selected task…'
      : chat.status === 'Reconnecting' ? 'Reconnecting to the conversation feed…'
      : chat.status === 'Unavailable' ? 'The selected task is unavailable.'
      : 'No agents are available for this task.'}</p>
      : <div className="agent-wall__panes" data-expanded={expanded !== null}>
        {visibleAgents.map((agent) => {
          const isExpanded = expanded === agent.id
          const isMain = agent.id === taskId
          const phase = agent.lane?.agent.phase ?? null
          const active = agent.lane?.active ?? false
          const configuration = agent.lane?.agent.configuration
          const operation = agent.lane
            ? active ? phase ?? 'Working' : agent.lane.agent.state === 'Completed' ? 'Completed' : agent.lane.agent.state
            : null
          return <article
            className="agent-wall__pane"
            aria-label={agent.name + ' · ' + (isMain ? 'Main agent' : 'Subagent')}
            key={agent.id}
            hidden={expanded !== null && !isExpanded}
            data-phone-selected={selected === agent.id}
            onKeyDown={(event) => {
              const dialogOpen = event.target instanceof Element && event.target.closest('dialog[open]')
              if (event.key === 'Escape' && isExpanded && !event.defaultPrevented && !dialogOpen) {
                event.stopPropagation()
                setExpandedId(null)
              }
            }}
          >
            <header className="agent-wall__pane-heading">
              <AgentAvatar phase={phase} active={active} isSubagent={!isMain} size={28} configuration={configuration} />
              <div className="agent-wall__identity">
                <h3 title={agent.name}>{agent.name}</h3>
                <span>{[isMain ? agent.name === 'Main agent' ? null : 'Main agent' : 'Subagent', operation].filter(Boolean).join(' · ')}</span>
              </div>
              {configuration && <AgentModelLabel configuration={configuration} />}
              <button
                className="agent-wall__expand"
                type="button"
                aria-label={(isExpanded ? 'Restore wall from ' : 'Expand ') + agent.name}
                title={isExpanded ? 'Restore wall' : 'Expand pane'}
                aria-pressed={isExpanded}
                onClick={() => {
                  setSelectedId(agent.id)
                  setExpandedId(isExpanded ? null : agent.id)
                }}
              >{isExpanded ? <Minimize2 size={15} aria-hidden="true" /> : <Maximize2 size={15} aria-hidden="true" />}</button>
            </header>
            <AgentChatFeed lane={agent.lane} agent={agent.chat} chat={chat} />
          </article>
        })}
      </div>}
  </section>
}

function startTime(agent: WallAgent) {
  const time = Date.parse(agent.startedAtUtc ?? '')
  return Number.isFinite(time) ? time : Number.MAX_SAFE_INTEGER
}
