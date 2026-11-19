import { useLayoutEffect, useMemo, useRef, useState } from 'react'
import { Activity, ArrowDown, Check, Circle, FilePenLine, LogIn, MessageSquare, Network, UserRound } from 'lucide-react'
import { AgentChatContent } from './AgentChatContent'
import { agentChatNotice, sharedAgentMessages, type AgentChatAgent, type AgentChatMessage, type AgentChatSession } from './agentChat'
import { agentDisplayName } from './agentIdentity'
import { agentHistoryMilestones, type MachineActivityLane, type MachineActivityMilestone } from './machineActivityTimeline'
import './AgentChatFeed.css'

type FeedItem = { id: string; time: string; message: AgentChatMessage } | { id: string; time: string; event: MachineActivityMilestone }

const messageIcons = { Prompt: UserRound, Commentary: MessageSquare, Final: Check }
const milestoneIcons = { Start: LogIn, Architecture: Network, Edit: FilePenLine, Phase: Activity, Complete: Check }

interface AgentChatFeedProps {
  lane?: MachineActivityLane | null
  agent?: AgentChatAgent
  chat: AgentChatSession
  view?: 'combined' | 'conversation'
  reveal?: { messageId: string } | null
}

/** A single scrollable history shared by the Flow inspector and every wall pane. */
export function AgentChatFeed(props: AgentChatFeedProps) {
  const { agent, lane, chat } = props
  const identity = agent?.id ?? lane?.agent.publicId ?? lane?.agent.agentId ?? 'agent'
  return <AgentChatHistory key={JSON.stringify([identity, chat.sharing, chat.periodId])} {...props} />
}

function AgentChatHistory({ agent, lane, chat, view = 'combined', reveal }: AgentChatFeedProps) {
  const shared = sharedAgentMessages(chat, agent)
  const items = useMemo((): FeedItem[] => {
    const messages: FeedItem[] = shared.map((message) => ({
      id: 'message:' + message.id, time: message.occurredAtUtc, message,
    }))
    const milestones = lane && view === 'combined' ? agentHistoryMilestones(lane) : []
    const events: FeedItem[] = milestones.map((event) => ({ id: 'activity:' + event.id, time: event.observedAtUtc, event }))
    return [...messages, ...events].sort((left, right) =>
      Date.parse(left.time) - Date.parse(right.time) || left.id.localeCompare(right.id))
  }, [shared, lane, view])
  const fingerprint = JSON.stringify(items.map((item) => 'message' in item
    ? [item.id, item.time, item.message.text, item.message.kind, item.message.artifacts.map(({ id, contentType }) => [id, contentType])]
    : [item.id, item.time, item.event.label, item.event.detail]))
  const scroller = useRef<HTMLDivElement>(null)
  const feed = useRef<HTMLOListElement>(null)
  const following = useRef(true)
  const lastVisibleOffset = useRef(0)
  const wasHidden = useRef(false)
  const prevFingerprint = useRef<string | null>(null)
  const [updatesBelow, setUpdatesBelow] = useState(false)
  const name = lane ? agentDisplayName(lane.agent) : agent?.displayName ?? 'Agent'
  const notice = agentChatNotice(chat, agent)

  useLayoutEffect(() => {
    if (following.current) {
      const element = scroller.current
      if (element?.clientHeight) element.scrollTop = element.scrollHeight
    } else if (prevFingerprint.current !== null && prevFingerprint.current !== fingerprint) {
      setUpdatesBelow(true)
    }
    prevFingerprint.current = fingerprint
  }, [fingerprint])

  useLayoutEffect(() => {
    if (!reveal || !feed.current || !scroller.current) return
    const message = Array.from(feed.current.children).find((item): item is HTMLElement =>
      item instanceof HTMLElement && item.dataset.messageId === reveal.messageId)
    if (!message) return
    const element = scroller.current
    following.current = false
    element.scrollTop += message.getBoundingClientRect().top - element.getBoundingClientRect().top - 12
    lastVisibleOffset.current = element.scrollTop
    following.current = element.scrollHeight - element.clientHeight - element.scrollTop <= 24
    setUpdatesBelow(!following.current)
    message.focus({ preventScroll: true })
  }, [reveal])

  useLayoutEffect(() => {
    if (typeof ResizeObserver === 'undefined' || !scroller.current || !feed.current) return
    const observer = new ResizeObserver(() => {
      const element = scroller.current
      if (!element) return
      if (!element.clientHeight) {
        wasHidden.current = true
        return
      }
      if (following.current) element.scrollTop = element.scrollHeight
      else if (wasHidden.current) element.scrollTop = lastVisibleOffset.current
      wasHidden.current = false
    })
    observer.observe(scroller.current)
    observer.observe(feed.current)
    return () => observer.disconnect()
  }, [])

  const jumpToLatest = () => {
    if (!scroller.current) return
    following.current = true
    scroller.current.scrollTop = scroller.current.scrollHeight
    scroller.current.focus({ preventScroll: true })
    setUpdatesBelow(false)
  }

  return <div className="agent-chat-feed">
    <div
      className="agent-chat-feed__scroll"
      role="region"
      aria-label={name + ' ' + (view === 'combined' ? 'conversation and activity' : view)}
      tabIndex={0}
      ref={scroller}
      onScroll={({ currentTarget }) => {
        if (currentTarget.clientHeight === 0) return
        lastVisibleOffset.current = currentTarget.scrollTop
        following.current = currentTarget.scrollHeight - currentTarget.clientHeight - currentTarget.scrollTop <= 24
        if (view === 'conversation' || following.current) setUpdatesBelow(!following.current)
      }}
    >
      {notice && <p className="agent-chat-feed__notice" role="status"><MessageSquare size={14} aria-hidden="true" />{notice}</p>}
      <ol className="agent-chat-feed__entries" ref={feed}>
        {items.map((item) => {
          if ('event' in item) return <AgentActivityEntry key={item.id} event={item.event} />
          const Icon = messageIcons[item.message.kind]
          return <li key={item.id} data-message-id={item.message.id} tabIndex={view === 'conversation' ? -1 : undefined}
            className={'agent-chat-message agent-chat-message--' + item.message.kind.toLowerCase()}>
            <header>
              {view === 'conversation' && <span className="agent-chat-message__marker"><Icon size={14} aria-hidden="true" /></span>}
              <strong>{item.message.role === 'User' ? 'You' : item.message.kind === 'Final' ? 'Final reply' : 'Progress'}</strong>
              <MessageTime datetime={item.time} />
            </header>
            <div className="agent-chat-message__content">
              <AgentChatContent text={item.message.text} artifacts={item.message.artifacts} artifactUrl={chat.artifactUrl} />
            </div>
            {item.message.isTruncated && <small className="agent-chat-message__truncated">Message shortened</small>}
          </li>
        })}
      </ol>
    </div>
    {updatesBelow && <div className="agent-chat-feed__jump">
      <button type="button" onClick={jumpToLatest}><ArrowDown size={14} aria-hidden="true" />Jump to latest</button>
    </div>}
  </div>
}

export function AgentActivityEntry({ event, detailed = false }: { event: MachineActivityMilestone; detailed?: boolean }) {
  const Icon = milestoneIcons[event.kind]
  return <li className="agent-chat-event">
    {detailed ? <span className="agent-chat-event__marker"><Icon size={15} aria-hidden="true" /></span>
      : <Circle size={6} aria-hidden="true" />}
    <span><strong>{event.label}</strong><small>{event.detail}</small></span>
    <MessageTime datetime={event.observedAtUtc} />
  </li>
}

function MessageTime({ datetime }: { datetime: string }) {
  const time = new Date(datetime)
  if (Number.isNaN(time.valueOf())) return null
  return <time dateTime={datetime} title={time.toLocaleString()}>
    {time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
  </time>
}
