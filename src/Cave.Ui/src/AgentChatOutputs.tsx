import { MessageSquareText } from 'lucide-react'
import { MermaidPreview, SharedImagePreview } from './AgentChatContent'
import type { AgentChatOutput } from './agentChatOutputIndex'
import './AgentChatOutputs.css'

export function AgentChatOutputs({ outputs, artifactUrl, onOpenMessage }: {
  outputs: readonly AgentChatOutput[]
  artifactUrl: (id: string) => string
  onOpenMessage: (messageId: string) => void
}) {
  return <ol className="agent-chat-outputs agent-chat-content" aria-label="Shared diagrams and images">
    {outputs.map((output) => {
      const time = new Date(output.occurredAtUtc)
      return <li key={output.id}>
        {output.caption && <p className="agent-chat-outputs__caption">{output.caption}</p>}
        {output.kind === 'diagram' ? <MermaidPreview source={output.source} />
          : <SharedImagePreview artifact={output.artifact} artifactUrl={artifactUrl} />}
        <div className="agent-chat-outputs__origin">
          {Number.isFinite(time.valueOf()) && <time dateTime={output.occurredAtUtc} title={time.toLocaleString()}>
            {time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
          </time>}
          <button type="button" onClick={() => onOpenMessage(output.messageId)}>
            <MessageSquareText aria-hidden="true" size={14} />View message
          </button>
        </div>
      </li>
    })}
  </ol>
}
