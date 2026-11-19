import { useLayoutEffect, useMemo, useRef, useState } from 'react'
import { ArrowDown, Circle, MessageSquare } from 'lucide-react'
import { AgentChatContent } from './AgentChatContent'
import type { AgentChatAgent, AgentChatMessage, AgentChatSession } from './agentChat'
import { agentDisplayName } from './agentIdentity'
import { visibleLaneMilestones, type MachineActivityLane, type MachineActivityMilestone } from './machineActivityTimeline'
import './AgentChatFeed.css'

type FeedItem = { id: string; time: string; message: AgentChatMessage } | { id: string; time: string; event: MachineActivityMilestone }

interface AgentChatFeedProps {
  lane?: MachineActivityLane | null
  agent?: AgentChatAgent
  chat: AgentChatSession
}

/** A single scrollable history shared by the Flow inspector and every wall pane. */
export function AgentChatFeed(props: AgentChatFeedProps) {
  const { agent, lane, chat } = props
  const identity = agent?.id ?? lane?.agent.publicId ?? lane?.agent.agentId ?? 'agent'
  return <AgentChatHistory key={JSON.stringify([identity, chat.sharing, chat.periodId])} {...props} />
}

function AgentChatHistory({ agent, lane, chat }: AgentChatFeedProps) {
  const items = useMemo((): FeedItem[] => {
    const messages: FeedItem[] = chat.sharing === 'Shared' || chat.sharing === 'Legacy'
      ? (agent?.messages ?? []).map((message) => ({ id: 'message:' + message.id, time: message.occurredAtUtc, message }))
      : []
    const milestones = lane ? visibleLaneMilestones({ ...lane, completionSummary: null }, lane.active) : []
    const events: FeedItem[] = milestones.map((event) => ({ id: 'activity:' + event.id, time: event.observedAtUtc, event }))
    return [...messages, ...events].sort((left, right) =>
      Date.parse(left.time) - Date.parse(right.time) || left.id.localeCompare(right.id))
  }, [agent, lane, chat.sharing])
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
  const notice = chat.sharing === 'Private' ? 'Conversation sharing is off.'
    : chat.status === 'Loading' ? 'Loading conversation…'
    : chat.status === 'Reconnecting' ? 'Reconnecting to the conversation feed…'
    : chat.sharing === 'Legacy' ? (agent?.messages.length ? null : 'No captured messages yet.')
    : chat.status === 'Unavailable' || agent?.status === 'Unavailable' ? 'Conversation unavailable for this agent.'
    : agent?.status === 'Partial' ? 'Some messages are unavailable for this agent.'
    : chat.status === 'Idle' ? 'Conversation unavailable for this task.'
    : !items.some((item) => 'message' in item) ? 'No shared messages yet.'
    : null

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
      aria-label={name + ' conversation and activity'}
      tabIndex={0}
      ref={scroller}
      onScroll={({ currentTarget }) => {
        if (currentTarget.clientHeight === 0) return
        lastVisibleOffset.current = currentTarget.scrollTop
        following.current = currentTarget.scrollHeight - currentTarget.clientHeight - currentTarget.scrollTop <= 24
        if (following.current) setUpdatesBelow(false)
      }}
    >
      {notice && <p className="agent-chat-feed__notice" role="status"><MessageSquare size={14} aria-hidden="true" />{notice}</p>}
      <ol className="agent-chat-feed__entries" ref={feed}>
        {items.map((item) => 'message' in item
          ? <li key={item.id} className={'agent-chat-message agent-chat-message--' + item.message.kind.toLowerCase()}>
            <header>
              <strong>{item.message.role === 'User' ? 'You' : item.message.kind === 'Final' ? 'Final reply' : 'Progress'}</strong>
              <MessageTime datetime={item.time} />
            </header>
            <div className="agent-chat-message__content">
              <AgentChatContent text={item.message.text} artifacts={item.message.artifacts} artifactUrl={chat.artifactUrl} />
            </div>
            {item.message.isTruncated && <small className="agent-chat-message__truncated">Message shortened</small>}
          </li>
          : <li className="agent-chat-event" key={item.id}>
            <Circle size={6} aria-hidden="true" />
            <span><strong>{item.event.label}</strong><small>{item.event.detail}</small></span>
            <MessageTime datetime={item.time} />
          </li>)}
      </ol>
    </div>
    {updatesBelow && <div className="agent-chat-feed__jump">
      <button type="button" onClick={jumpToLatest}><ArrowDown size={14} aria-hidden="true" />Jump to latest</button>
    </div>}
  </div>
}

function MessageTime({ datetime }: { datetime: string }) {
  const time = new Date(datetime)
  if (Number.isNaN(time.valueOf())) return null
  return <time dateTime={datetime} title={time.toLocaleString()}>
    {time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
  </time>
}
