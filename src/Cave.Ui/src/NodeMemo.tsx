import { MessageSquareText, Sparkles, X } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { CodexText } from './CodexText'
import type { ArchitectureNode } from './types'

export interface NodeMemoState {
  id: string
  nodeName: string
  question: string
  kind: 'Explain' | 'Ask'
  status: 'Loading' | 'Ready' | 'Error'
  text: string | null
  error: string | null
}

export function NodeContextMenu({
  node,
  x,
  y,
  canAsk,
  executionHint,
  onExplain,
  onAsk,
  onClose,
}: {
  node: ArchitectureNode
  x: number
  y: number
  canAsk: boolean
  executionHint: string
  onExplain(): void
  onAsk(): void
  onClose(): void
}) {
  return (
    <div className="node-context-menu" style={{ left: x, top: y }} role="menu" onMouseLeave={onClose}>
      <strong>{node.name}</strong>
      <button type="button" role="menuitem" disabled={!canAsk} onClick={onExplain}>
        <Sparkles size={13} /> Explain this
      </button>
      <button type="button" role="menuitem" disabled={!canAsk} onClick={onAsk}>
        <MessageSquareText size={13} /> Ask this box
      </button>
      <small>{executionHint}</small>
    </div>
  )
}

export function AskNodeDialog({ node, submitLabel, onCancel, onAsk }: {
  node: ArchitectureNode
  submitLabel: string
  onCancel(): void
  onAsk(question: string): void
}) {
  const [question, setQuestion] = useState('')
  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (question.trim()) onAsk(question.trim())
  }
  return (
    <div className="panel-scrim" role="presentation" onMouseDown={onCancel}>
      <form
        className="ask-node-dialog"
        role="dialog"
        aria-modal="true"
        aria-label={`Ask ${node.name}`}
        onSubmit={submit}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <span className="panel-eyebrow">Ask this box</span>
        <h2>{node.name}</h2>
        <p>Ask a focused question about this node and its architecture evidence.</p>
        <textarea autoFocus value={question} placeholder="What do you want to know?" onChange={(event) => setQuestion(event.target.value)} />
        <div>
          <button type="button" onClick={onCancel}>Cancel</button>
          <button type="submit" disabled={!question.trim()}>{submitLabel}</button>
        </div>
      </form>
    </div>
  )
}

export function NodeMemoDialog({ memo, onClose }: {
  memo: NodeMemoState
  onClose(): void
}) {
  return (
    <div className="panel-scrim" role="presentation" onMouseDown={onClose}>
      <article
        className={`node-memo-dialog is-${memo.status.toLowerCase()}`}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${memo.id}-title`}
        onMouseDown={(event) => event.stopPropagation()}
      >
        <header>
          <div>
            <span className="panel-eyebrow"><Sparkles size={13} /> {memo.kind} this box</span>
            <h2 id={`${memo.id}-title`}>{memo.nodeName}</h2>
          </div>
          <button type="button" aria-label={`Close ${memo.nodeName} answer`} onClick={onClose}><X size={16} /></button>
        </header>
        <section className="node-memo-question" aria-label="Question">
          <small>Question</small>
          <p>{memo.question}</p>
        </section>
        <section className="node-memo-answer" aria-live="polite">
          {memo.status === 'Loading' && memo.text === null ? (
            <div className="node-memo-progress">
              <span aria-hidden="true" />
              <p>Waiting for Codex to answer…</p>
            </div>
          ) : null}
          {memo.text !== null ? <CodexText text={memo.text} /> : null}
          {memo.status === 'Loading' && memo.text !== null ? <small>Codex is still writing…</small> : null}
          {memo.status === 'Error' ? <p className="node-memo-error">{memo.error}</p> : null}
        </section>
        <footer>
          <small>This temporary side-chat answer stays in this dialog until you close it.</small>
          <button type="button" onClick={onClose}>Close</button>
        </footer>
      </article>
    </div>
  )
}
