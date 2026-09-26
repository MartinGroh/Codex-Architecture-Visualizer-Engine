import Markdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { parseCodexText, remarkCodexAttribution } from './codexTextFormat'
import './CodexText.css'

const variants: Record<string, string> = {
  email: 'Email', chat_message: 'Message', social_post: 'Social post', document: 'Document', standard: 'Draft',
}

/** One read-only rendering owner for public Codex chat and isolated node answers. */
export function CodexText({ text, attributed = true }: { text: string; attributed?: boolean }) {
  const parts = attributed ? parseCodexText(text) : [{ kind: 'markdown' as const, text }]
  return (
    <div className="codex-text">
      {parts.map((part, index) => {
        if (part.kind === 'markdown') return <CodexMarkdown key={index} text={part.text} attributed={attributed} />
        if (part.kind === 'thread') {
          return <p key={index} className="codex-text__thread">Created chat · <code>{part.attributes.threadId ?? part.attributes.clientThreadId}</code></p>
        }
        if (part.kind === 'comment') {
          const { title, body, file, start, end } = part.attributes
          return (
            <section key={index} className="codex-text__comment" aria-label={title}>
              <strong>{title}</strong>
              <small>{file}{start ? `:${start}${end && end !== start ? `–${end}` : ''}` : ''}</small>
              <CodexMarkdown text={body} />
            </section>
          )
        }
        const { variant, subject, recipient, cc, bcc } = part.attributes
        return (
          <section key={index} className="codex-text__writing" aria-label={subject || variants[variant]}>
            <header>
              <strong>{variants[variant]}</strong>
              {subject && <span>{subject}</span>}
              {recipient && <small>To: {recipient}</small>}
              {cc && <small>Cc: {cc}</small>}
              {bcc && <small>Bcc: {bcc}</small>}
            </header>
            <CodexMarkdown text={part.text} />
          </section>
        )
      })}
    </div>
  )
}

function CodexMarkdown({ text, attributed = true }: { text: string; attributed?: boolean }) {
  return <Markdown remarkPlugins={attributed ? [remarkGfm, remarkCodexAttribution] : [remarkGfm]}>{text}</Markdown>
}
