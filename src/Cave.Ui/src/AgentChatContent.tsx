import { ChevronDown, Code2, Expand, Frame, Image, Minus, Plus, Waypoints, X } from 'lucide-react'
import { useEffect, useId, useLayoutEffect, useRef, useState, type CSSProperties, type SyntheticEvent } from 'react'
import type { Components } from 'react-markdown'
import type { AgentChatArtifact } from './agentChat'
import { CodexText } from './CodexText'
import { LongText } from './LongText'
import { renderAgentChatDiagram } from './agentChatDiagram'
import './AgentChatContent.css'

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
export function AgentChatContent({ text, artifacts, artifactUrl, title = 'message', byline, truncated, openRequest }: {
  text: string
  artifacts: readonly AgentChatArtifact[]
  artifactUrl: (id: string) => string
  title?: string
  byline?: string
  truncated?: boolean
  openRequest?: object
}) {
  return <div className="agent-chat-content">
    {text && <LongText text={text} title={title} byline={byline} format="markdown" truncated={truncated} openRequest={openRequest}>
      <CodexText text={text} attributed={false} components={chatComponents} />
    </LongText>}
    {artifacts.length > 0 && <div className="agent-chat-content__artifacts">
      {artifacts.map((artifact, index) => <SharedImagePreview key={artifact.id}
        artifact={artifact} artifactUrl={artifactUrl}
        label={artifacts.length === 1 ? 'Shared image' : `Shared image ${index + 1}`} />)}
    </div>}
  </div>
}

export function SharedImagePreview({ artifact, artifactUrl, label = 'Shared image', compact = false }: {
  artifact: AgentChatArtifact
  artifactUrl: (id: string) => string
  label?: string
  compact?: boolean
}) {
  const src = artifact.contentType && rasterTypes.has(artifact.contentType)
    ? localArtifactUrl(artifactUrl(artifact.id)) : null
  return src ? <MediaPreview key={src} src={src} label={label} compact={compact} />
    : <small className="agent-chat-content__unavailable">Image preview unavailable</small>
}

function localArtifactUrl(value: string): string | null {
  try {
    const url = new URL(value, window.location.href)
    return url.origin === window.location.origin && /^https?:$/.test(url.protocol) ? url.href : null
  } catch {
    return null
  }
}

interface ImageSize { width: number; height: number }

// Native preview controls inside a React Flow node have their own interaction target.
const canvasInteractionClasses = 'nodrag nopan nowheel'
function stopCanvasEvent(event: SyntheticEvent) { event.stopPropagation() }

function MediaPreview({ src, label, diagram = false, compact = false }: {
  src: string; label: string; diagram?: boolean; compact?: boolean
}) {
  const opener = useRef<HTMLButtonElement>(null)
  const [failed, setFailed] = useState(false)
  const [open, setOpen] = useState(false)
  const [size, setSize] = useState<ImageSize | null>(null)
  const Icon = diagram ? Waypoints : Image

  if (failed) return <small className="agent-chat-content__unavailable">{label} preview unavailable</small>

  return <>
    <button type="button" className={`agent-chat-preview${compact ? ` agent-chat-preview--compact ${canvasInteractionClasses}` : ''}`}
      aria-label={`Open ${label.toLowerCase()} preview`} ref={opener}
      onPointerDown={compact ? stopCanvasEvent : undefined}
      onDoubleClick={compact ? stopCanvasEvent : undefined}
      onKeyDown={compact ? stopCanvasEvent : undefined}
      onClick={(event) => {
        if (compact) stopCanvasEvent(event)
        setOpen(true)
      }}>
      {compact
        ? <span className="agent-chat-preview__corner"><Expand size={14} aria-hidden="true" /></span>
        : <span className="agent-chat-preview__bar"><Icon size={16} aria-hidden="true" /><strong>{label}</strong>
        {!diagram && size && <small>{size.width} × {size.height}</small>}
        <Expand className="agent-chat-preview__expand" size={15} aria-hidden="true" />
      </span>}
      <img src={src} alt="" loading="lazy" referrerPolicy="no-referrer" onError={() => setFailed(true)}
        onLoad={({ currentTarget }) => setSize({ width: currentTarget.naturalWidth, height: currentTarget.naturalHeight })} />
    </button>
    {open && <ExpandedPreview src={src} label={label} size={size} diagram={diagram} compact={compact} onClose={() => {
      setOpen(false)
      opener.current?.focus({ preventScroll: true })
    }} />}
  </>
}

const zoomSteps = [1, 1.5, 2, 3, 4, 6, 8, 12, 16]

function ExpandedPreview({ src, label, size, diagram, compact, onClose }: {
  src: string; label: string; size: ImageSize | null; diagram: boolean; compact: boolean; onClose: () => void
}) {
  const dialog = useRef<HTMLDialogElement>(null)
  const canvas = useRef<HTMLDivElement>(null)
  const anchor = useRef<{ x: number; y: number } | null>(null)
  const drag = useRef<{ x: number; y: number; left: number; top: number } | null>(null)
  const [step, setStep] = useState(0)
  const scale = zoomSteps[step]
  const Icon = diagram ? Waypoints : Image

  useLayoutEffect(() => {
    const panel = dialog.current!
    panel.showModal()
    return () => { if (panel.open) panel.close() }
  }, [])

  function zoom(next: number) {
    const port = canvas.current
    if (!port || next === step || next < 0 || next >= zoomSteps.length) return
    anchor.current = {
      x: (port.scrollLeft + port.clientWidth / 2) / port.scrollWidth,
      y: (port.scrollTop + port.clientHeight / 2) / port.scrollHeight,
    }
    setStep(next)
  }

  useLayoutEffect(() => {
    const port = canvas.current
    const center = anchor.current
    if (!port || !center) return
    port.scrollLeft = center.x * port.scrollWidth - port.clientWidth / 2
    port.scrollTop = center.y * port.scrollHeight - port.clientHeight / 2
    anchor.current = null
  }, [step])

  return <dialog ref={dialog} className={`agent-chat-preview-dialog${compact ? ` ${canvasInteractionClasses}` : ''}`} aria-label={label}
    onPointerDown={compact ? stopCanvasEvent : undefined}
    onDoubleClick={compact ? stopCanvasEvent : undefined}
    onClick={compact ? stopCanvasEvent : undefined}
    onClose={(event) => { if (!event.currentTarget.open) onClose() }}
    onKeyDown={(event) => {
      if (compact) stopCanvasEvent(event)
      if (event.ctrlKey || event.metaKey || event.altKey) return
      switch (event.key) {
        case '+': case '=': zoom(step + 1); break
        case '-': zoom(step - 1); break
        case '0': zoom(0); break
        default: return
      }
      event.preventDefault()
    }}>
    <header>
      <strong><Icon size={19} aria-hidden="true" />{label}</strong>
      <div className="agent-chat-preview-dialog__zoom" role="group" aria-label="Preview zoom">
        <button type="button" disabled={step === 0} onClick={() => zoom(step - 1)} aria-label="Zoom out" title="Zoom out (−)"><Minus size={16} /></button>
        <output aria-label="Zoom relative to fit">{scale}×</output>
        <button type="button" disabled={step === zoomSteps.length - 1} onClick={() => zoom(step + 1)} aria-label="Zoom in" title="Zoom in (+)"><Plus size={16} /></button>
        <button type="button" onClick={() => zoom(0)} disabled={step === 0} aria-label="Fit to view" title="Fit to view (0)"><Frame size={16} /></button>
      </div>
      <button type="button" onClick={() => dialog.current?.close()} aria-label="Close preview" title="Close preview"><X size={18} /></button>
    </header>
    <div className="agent-chat-preview-dialog__canvas" ref={canvas} role="region" tabIndex={0} aria-label="Preview canvas"
      data-zoomed={step > 0 || undefined}
      onPointerDown={(event) => {
        if (!step || event.pointerType !== 'mouse' || event.button !== 0) return
        const port = event.currentTarget
        drag.current = { x: event.clientX, y: event.clientY, left: port.scrollLeft, top: port.scrollTop }
        port.setPointerCapture(event.pointerId)
      }}
      onPointerMove={(event) => {
        if (!drag.current) return
        event.currentTarget.scrollLeft = drag.current.left + drag.current.x - event.clientX
        event.currentTarget.scrollTop = drag.current.top + drag.current.y - event.clientY
      }}
      onLostPointerCapture={() => { drag.current = null }}>
      <div className="agent-chat-preview-dialog__sheet">
        <img src={src} alt={label} referrerPolicy="no-referrer" draggable={false}
          style={size?.height ? { width: `calc(min(100cqw, ${size.width / size.height * 100}cqh) * ${scale})` } : undefined} />
      </div>
    </div>
  </dialog>
}

export function MermaidPreview({ source, compact = false }: { source: string; compact?: boolean }) {
  const id = `cave-diagram-${useId().replace(/[^A-Za-z0-9-]/g, '')}`
  const figure = useRef<HTMLElement>(null)
  const [visible, setVisible] = useState(() => typeof IntersectionObserver === 'undefined')
  const [result, setResult] = useState<{ source: string; src: string | null; paper: string } | null>(null)
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
    const element = figure.current
    if (!visible || !element) return
    let controller: AbortController
    const render = () => {
      controller?.abort()
      controller = new AbortController()
      const { signal } = controller
      const style = getComputedStyle(element)
      const color = (token: string) => style.getPropertyValue(token).trim()
      const paper = color('--bg')
      const palette = { paper, node: color('--surface'), ink: color('--text'), edge: color('--text-soft'), accent: color('--violet') }
      void renderAgentChatDiagram(id, source, signal, palette).then((preview) => {
        if (!signal.aborted) setResult({ source, src: preview, paper })
      }, () => {
        if (!signal.aborted) setResult({ source, src: null, paper })
      })
    }
    render()
    const themeRoot = element.closest('[data-theme]')
    const observer = new MutationObserver(render)
    if (themeRoot) observer.observe(themeRoot, { attributes: true, attributeFilter: ['data-theme'] })
    return () => { controller.abort(); observer.disconnect() }
  }, [id, source, visible])

  return <figure className={`agent-chat-diagram${compact ? ' agent-chat-diagram--compact' : ''}`}
    ref={figure} style={{ '--diagram-paper': result?.paper || undefined } as CSSProperties}>
    {src && <MediaPreview key={source} src={src} label="Mermaid diagram" diagram compact={compact} />}
    {failed && <small className="agent-chat-content__unavailable" role="status">Diagram preview unavailable</small>}
    {compact && !src && !failed && <small className="agent-chat-content__unavailable">Rendering diagram…</small>}
    {!compact && <details open={failed || undefined}>
      <summary><Code2 size={14} aria-hidden="true" />Mermaid source<ChevronDown size={13} aria-hidden="true" /></summary>
      <pre><code>{source}</code></pre>
    </details>}
  </figure>
}
