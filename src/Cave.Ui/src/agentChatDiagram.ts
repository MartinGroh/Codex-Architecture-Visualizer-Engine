const maxSourceLength = 16_384
const maxSvgLength = 1_048_576
const responseTimeoutMs = 5_000

let framePromise: Promise<HTMLIFrameElement> | undefined
let renderQueue: Promise<unknown> = Promise.resolve()

/** Returns a local static preview for a bounded Mermaid source block. */
export function renderAgentChatDiagram(id: string, source: string, signal: AbortSignal): Promise<string | null> {
  if (source.length === 0 || source.length > maxSourceLength) return Promise.resolve(null)
  const render = renderQueue.then(() => signal.aborted ? null : renderInFrame(id, source))
  renderQueue = render.catch(() => undefined)
  return render
}

async function renderInFrame(id: string, source: string): Promise<string | null> {
  framePromise ??= createFrame()
  const frame = await framePromise.catch((error: unknown) => {
    framePromise = undefined
    throw error
  })
  const svg = await new Promise<string | null>((resolve) => {
    const finish = (value: string | null) => {
      window.clearTimeout(timer)
      window.removeEventListener('message', receive)
      resolve(value)
    }
    const receive = (event: MessageEvent) => {
      if (event.source !== frame.contentWindow || event.origin !== 'null'
          || event.data?.type !== 'cave-mermaid-result' || event.data.id !== id) return
      const value: unknown = event.data.svg
      finish(typeof value === 'string' && value.length <= maxSvgLength ? value : null)
    }
    const timer = window.setTimeout(() => {
      frame.remove()
      framePromise = undefined
      finish(null)
    }, responseTimeoutMs)
    window.addEventListener('message', receive)
    frame.contentWindow?.postMessage({ type: 'cave-mermaid-render', id, source }, '*')
  })
  if (!svg) return null

  const { default: purify } = await import('dompurify')
  const sanitized = purify.sanitize(svg, {
    USE_PROFILES: { svg: true, svgFilters: true },
    FORBID_TAGS: ['a', 'foreignObject', 'image', 'feImage'],
    FORBID_ATTR: ['href', 'xlink:href', 'src'],
  })
  return sanitized.startsWith('<svg') ? `data:image/svg+xml;charset=utf-8,${encodeURIComponent(sanitized)}` : null
}

async function createFrame(): Promise<HTMLIFrameElement> {
  const { default: base64 } = await import('virtual:cave-mermaid-gzip')
  const compressed = Uint8Array.from(atob(base64), (byte) => byte.charCodeAt(0))
  const decoded = new Blob([compressed]).stream().pipeThrough(new DecompressionStream('gzip'))
  const mermaidBundle = await new Response(decoded).text()
  const frame = document.createElement('iframe')
  frame.className = 'agent-chat-diagram-workspace'
  frame.title = 'Diagram renderer'
  frame.setAttribute('aria-hidden', 'true')
  frame.setAttribute('sandbox', 'allow-scripts')
  frame.tabIndex = -1
  // CONSTRAINT: Mermaid can fetch images during layout, before returning SVG. Execute its
  // bundled renderer in a separate origin with no network access and never bind interactions.
  frame.srcdoc = `<!doctype html>
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data: blob:">
    <body><script>${mermaidBundle.replace(/<(?=\/?script\b|!--)/gi, '\\x3c')}</script>
    <script>
      globalThis.mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', suppressErrorRendering: true,
        secure: ['secure', 'securityLevel', 'startOnLoad', 'maxTextSize', 'suppressErrorRendering', 'maxEdges', 'htmlLabels', 'themeCSS', 'dompurifyConfig'],
        htmlLabels: false, maxTextSize: ${maxSourceLength}, maxEdges: 128 });
      addEventListener('message', async event => {
        if (event.source !== parent || event.data?.type !== 'cave-mermaid-render') return;
        const { id, source } = event.data;
        let svg = null;
        try { ({ svg } = await globalThis.mermaid.render(id, source)); } catch {}
        parent.postMessage({ type: 'cave-mermaid-result', id, svg }, '*');
      });
    </script></body>`
  return new Promise((resolve, reject) => {
    const timer = window.setTimeout(() => {
      frame.remove()
      reject(new Error('The diagram workspace did not load.'))
    }, responseTimeoutMs)
    frame.addEventListener('load', () => {
      window.clearTimeout(timer)
      resolve(frame)
    }, { once: true })
    document.body.append(frame)
  })
}
