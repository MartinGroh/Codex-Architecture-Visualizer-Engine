import { MessageCircle, Send, ShieldCheck, X } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { CodexText } from './CodexText'
import type { ConversationOverlay } from './types'

export function ConversationPanel({
  conversation,
  canSend,
  canManageSharing,
  busy,
  onClose,
  onSend,
  onSetSharing,
}: {
  conversation: ConversationOverlay
  canSend: boolean
  canManageSharing: boolean
  busy: boolean
  onClose(): void
  onSend(text: string): Promise<boolean>
  onSetSharing(enabled: boolean): Promise<void>
}) {
  const [draft, setDraft] = useState('')
  const currentDelivery = conversation.control.state === 'Queued'
    ? [...conversation.control.deliveries].reverse().find((delivery) => delivery.state === 'Queued')
    : conversation.control.state === 'Running'
      ? [...conversation.control.deliveries].reverse().find((delivery) => delivery.state === 'Running')
      : conversation.control.deliveries.at(-1)
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const text = draft.trim()
    if (!text || !canSend || busy) return
    if (await onSend(text)) {
      setDraft('')
    }
  }

  return (
    <aside className="conversation-panel" aria-label="Codex conversation">
      <header>
        <span><MessageCircle size={15} /> Conversation</span>
        <button type="button" aria-label="Close conversation" onClick={onClose}><X size={15} /></button>
      </header>
      <section className="conversation-sharing">
        <div>
          <strong><ShieldCheck size={13} /> Public messages</strong>
          <small>Opt-in copies public prompts and assistant messages. Tools and hidden reasoning stay excluded.</small>
        </div>
        <button
          type="button"
          aria-pressed={conversation.sharingEnabled}
          disabled={!canManageSharing || busy}
          onClick={() => void onSetSharing(!conversation.sharingEnabled)}
        >
          {conversation.sharingEnabled ? 'On' : 'Off'}
        </button>
      </section>
      <div className="conversation-messages">
        {!conversation.sharingEnabled ? (
          <p>Conversation sharing is off for this workspace. The hook does not retain message text.</p>
        ) : conversation.messages.length === 0 ? (
          <p>Sharing is enabled. New public prompts and assistant messages will appear here.</p>
        ) : conversation.messages.map((message) => (
          <article key={message.eventId} className={`conversation-message is-${message.role.toLowerCase()}`}>
            <header>
              <strong>{message.isSubagent ? message.agentType ?? 'Subagent' : message.role}</strong>
              <time>{new Date(message.occurredAtUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</time>
            </header>
            <div className="conversation-message__content">
              <CodexText text={message.text} attributed={message.role === 'Assistant'} />
            </div>
            {message.isStreaming && <small>Codex is still writing…</small>}
            {message.isTruncated && <small>Message was truncated by the retention boundary.</small>}
          </article>
        ))}
      </div>
      <div className={`conversation-control is-${conversation.control.state.toLowerCase()}`}>
        <strong>{controlTitle(conversation.control.state)}</strong>
        <small>
          {controlDescription(conversation.control.state, conversation.control.error)
            ?? (conversation.control.sessionId === null
              ? 'Waiting for an exact Codex task binding.'
              : `Task ${conversation.control.sessionId.slice(0, 8)}…`)}
        </small>
        {currentDelivery !== undefined && (
          <span key={currentDelivery.messageId}>
            {deliveryTitle(currentDelivery.state)}
            {currentDelivery.error ? ` · ${currentDelivery.error}` : ''}
          </span>
        )}
      </div>
      <form className="conversation-composer" onSubmit={(event) => void submit(event)}>
        <textarea
          aria-label="Message Codex"
          value={draft}
          placeholder={canSend ? 'Type back to this Codex task…' : 'Waiting for an exact Codex task binding…'}
          disabled={!canSend || busy}
          onChange={(event) => setDraft(event.target.value)}
        />
        <button type="submit" disabled={!canSend || !draft.trim() || busy}>
          <Send size={13} /> Send to chat
        </button>
      </form>
    </aside>
  )
}

function controlTitle(state: ConversationOverlay['control']['state']): string {
  switch (state) {
    case 'Queued': return 'Waiting'
    case 'Running': return 'Delivering'
    case 'Ready': return 'Ready'
    case 'Failed': return 'Delivery failed'
    default: return 'Unavailable'
  }
}

function controlDescription(
  state: ConversationOverlay['control']['state'],
  error: string | null,
): string | null {
  if (state === 'Queued') {
    return 'Your message is saved. It will enter this task when the current Codex response finishes.'
  }
  if (state === 'Running') {
    return 'Codex accepted the message and is producing the response.'
  }
  return error
}

function deliveryTitle(state: ConversationOverlay['control']['deliveries'][number]['state']): string {
  switch (state) {
    case 'Queued': return 'One message waiting'
    case 'Running': return 'One message in progress'
    case 'Completed': return 'Last message delivered'
    case 'Failed': return 'Last message failed'
  }
}
