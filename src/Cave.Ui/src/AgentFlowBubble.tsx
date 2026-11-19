import { Activity, BookOpen, Image, MessageSquareText, Target, Waypoints } from 'lucide-react'
import { MermaidPreview, SharedImagePreview } from './AgentChatContent'
import type { AgentFlowBubble as Bubble } from './agentFlowBubbles'
import './AgentFlowBubble.css'

const icons = { task: Target, activity: Activity, text: MessageSquareText, image: Image, diagram: Waypoints }

/** Presents a canvas excerpt with the shared reader for visual outputs. */
export function AgentFlowBubble({ bubble, artifactUrl }: {
  bubble: Bubble
  artifactUrl: (id: string) => string
}) {
  const { kind, label, text } = bubble
  const Icon = icons[kind]
  const media = kind === 'image' || kind === 'diagram'

  return <div className={`agent-flow-bubble agent-flow-bubble--${kind}${media ? ' agent-flow-bubble--media' : ''}`} data-size={bubble.size}>
    <div className="agent-flow-bubble__label">
      <Icon size={15} aria-hidden="true" />
      <strong>{label}</strong>
    </div>
    {media ? <div className="agent-flow-bubble__media agent-chat-content">
      {kind === 'image' && bubble.artifact
        ? <SharedImagePreview artifact={bubble.artifact} artifactUrl={artifactUrl} label="Shared image" compact />
        : kind === 'diagram' && bubble.source
          ? <MermaidPreview source={bubble.source} compact />
          : <small className="agent-chat-content__unavailable">Preview unavailable</small>}
    </div> : text && text !== label && <p className="agent-flow-bubble__excerpt">{text}</p>}
    {bubble.reading && <div className="agent-flow-bubble__read">
      <span><BookOpen size={14} aria-hidden="true" />{kind === 'text' ? 'Read reply' : kind === 'task' && bubble.messageId ? 'Read prompt' : 'Read details'}</span>
      <small>{bubble.reading.words.toLocaleString()} {bubble.reading.words === 1 ? 'word' : 'words'}{bubble.reading.shortened ? ' captured' : ''}</small>
    </div>}
  </div>
}
