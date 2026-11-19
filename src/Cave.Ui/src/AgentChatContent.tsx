import { Expand, X } from 'lucide-react'
import { useEffect, useId, useRef, useState } from 'react'
import type { Components } from 'react-markdown'
import { CodexText } from './CodexText'
import { renderAgentChatDiagram } from './agentChatDiagram'
import './AgentChatContent.css'

interface ChatArtifact {
  id: string
  contentType: string | null
}

const rasterTypes = new Set(['image/png', 'image/jpeg', 'image/webp'])

const chatComponents: Components = {
  // Shared attachments have opaque identifiers. Markdown image URLs never load automatically.
  img: ({ alt }) => <span>Image preview unavailable{alt ? ` (${alt})` : ''}</span>,
  pre: ({ node, children }) => {
    const code = node?.children[0]
    if (code?.type === 'element' && Array.isArray(code.properties.className)
        && code.properties.className.includes('language-mermaid')) {
      const source = code.children[0]
      if (source?.type === 'text') return <MermaidPreview source={source.value.trimEnd()} />
    }
    return <pre>{children}</pre>
  },
}

/** Renders shared chat text and artifacts supplied by the scoped read endpoint. */
export function AgentChatContent({ text, artifacts, artifactUrl }: {
  text: string
  artifacts: readonly ChatArtifact[]
  artifactUrl: (id: string) => string
}) {
  return <div className="agent-chat-content">
    {text && <CodexText text={text} attributed={false} components={chatComponents} />}
    {artifacts.length > 0 && <div className="agent-chat-content__artifacts">
      {artifacts.map(({ id, contentType }, index) => {
        const src = contentType && rasterTypes.has(contentType) ? localArtifactUrl(artifactUrl(id)) : null
        const label = artifacts.length === 1 ? 'Shared image' : `Shared image ${index + 1}`
        return src ? <MediaPreview key={`${id}:${src}`} src={src} label={label} />
          : <small key={id} className="agent-chat-content__unavailable">Image preview unavailable</small>
      })}
    </div>}
  </div>
}

function localArtifactUrl(value: string): string | null {
  try {
    const url = new URL(value, window.location.href)
    return url.origin === window.location.origin && /^https?:$/.test(url.protocol) ? url.href : null
  } catch {
    return null
  }
}

function MediaPreview({ src, label }: { src: string; label: string }) {
  const dialog = useRef<HTMLDialogElement>(null)
  const opener = useRef<HTMLButtonElement>(null)
  const [failed, setFailed] = useState(false)

  if (failed) return <small className="agent-chat-content__unavailable">{label} preview unavailable</small>

  return <>
    <button type="button" className="agent-chat-preview" aria-label={`Open ${label.toLowerCase()} preview`}
      ref={opener} onClick={() => dialog.current?.showModal()}>
      <img src={src} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)} />
      <span><Expand size={14} aria-hidden="true" />{label}</span>
    </button>
    <dialog ref={dialog} className="agent-chat-preview-dialog" aria-label={label}
      onClose={() => opener.current?.focus()}>
      <header>
        <strong>{label}</strong>
        <button type="button" onClick={() => dialog.current?.close()} aria-label="Close preview"><X size={18} aria-hidden="true" /></button>
      </header>
      <img src={src} alt={label} loading="lazy" referrerPolicy="no-referrer" />
    </dialog>
  </>
}

function MermaidPreview({ source }: { source: string }) {
  const id = `cave-diagram-${useId().replace(/[^A-Za-z0-9-]/g, '')}`
  const figure = useRef<HTMLElement>(null)
  const [visible, setVisible] = useState(() => typeof IntersectionObserver === 'undefined')
  const [result, setResult] = useState<{ source: string; src: string | null } | null>(null)
  const src = result?.source === source ? result.src : null
  const failed = result?.source === source && result.src === null

  useEffect(() => {
    if (visible || !figure.current) return
    const observer = new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting) setVisible(true)
    })
    observer.observe(figure.current)
    return () => observer.disconnect()
  }, [visible])

  useEffect(() => {
    if (!visible) return
    const controller = new AbortController()
    void renderAgentChatDiagram(id, source, controller.signal).then((preview) => {
      if (!controller.signal.aborted) setResult({ source, src: preview })
    }, () => {
      if (!controller.signal.aborted) setResult({ source, src: null })
    })
    return () => controller.abort()
  }, [id, source, visible])

  return <figure className="agent-chat-diagram" ref={figure}>
    {src && <MediaPreview key={src} src={src} label="Mermaid diagram" />}
    {failed && <small className="agent-chat-content__unavailable" role="status">Diagram preview unavailable</small>}
    <details open={failed || undefined}>
      <summary>Mermaid source</summary>
      <pre><code>{source}</code></pre>
    </details>
  </figure>
}
