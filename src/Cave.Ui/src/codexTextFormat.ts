/** Presentation of retained public text only; no directive may execute a host action. */
export type CodexTextPart =
  | { kind: 'markdown'; text: string }
  | { kind: 'writing'; text: string; attributes: Record<string, string> }
  | { kind: 'comment'; attributes: Record<string, string> }
  | { kind: 'thread'; attributes: Record<string, string> }

const writingVariants = new Set(['email', 'chat_message', 'social_post', 'document', 'standard'])

/** Strict attribute reader: malformed or future syntax stays visible as ordinary text. */
export function readCodexAttributes(source: string): Record<string, string> | null {
  const attributes: Record<string, string> = Object.create(null)
  let rest = source.trim()
  while (rest.length > 0) {
    const match = /^([a-zA-Z][\w-]*)\s*=\s*("(?:[^"\\]|\\.)*"|\d+)(?:\s+|$)/.exec(rest)
    if (!match || Object.hasOwn(attributes, match[1])) return null
    try {
      attributes[match[1]] = match[2].startsWith('"') ? JSON.parse(match[2]) as string : match[2]
    } catch {
      return null
    }
    rest = rest.slice(match[0].length)
  }
  return attributes
}

interface Fence { marker: string; length: number }

function nextFence(line: string, fence: Fence | null): Fence | null {
  if (fence) {
    const close = /^ {0,3}(`{3,}|~{3,})\s*$/.exec(line)
    return close && close[1][0] === fence.marker && close[1].length >= fence.length ? null : fence
  }
  const open = /^ {0,3}(`{3,}|~{3,})(.*)$/.exec(line)
  if (!open || (open[1][0] === '`' && open[2].includes('`'))) return null
  return { marker: open[1][0], length: open[1].length }
}

/** Decode known block directives, preserving code examples and unrecognized markup. */
export function parseCodexText(source: string): CodexTextPart[] {
  const parts: CodexTextPart[] = []
  const lines = source.replace(/\r\n/g, '\n').split('\n')
  let markdown: string[] = []
  let fence: Fence | null = null
  const flush = () => {
    if (markdown.length > 0) parts.push({ kind: 'markdown', text: markdown.join('\n') })
    markdown = []
  }

  for (let index = 0; index < lines.length; index++) {
    const line = lines[index]
    const wasFenced = fence !== null
    fence = nextFence(line, fence)
    if (wasFenced || fence !== null) {
      markdown.push(line)
      continue
    }

    const writing = /^ {0,3}:::writing\{(.*)\}\s*$/.exec(line)
    const attributes = writing ? readCodexAttributes(writing[1]) : null
    if (attributes && writingVariants.has(attributes.variant)) {
      flush()
      const body: string[] = []
      let bodyFence: Fence | null = null
      while (++index < lines.length) {
        const bodyLine = lines[index]
        if (bodyFence === null && /^ {0,3}:::\s*$/.test(bodyLine)) break
        bodyFence = nextFence(bodyLine, bodyFence)
        body.push(bodyLine)
      }
      // CONSTRAINT: an unfinished block is a normal streaming message; retain its body.
      parts.push({ kind: 'writing', text: body.join('\n'), attributes })
      continue
    }

    const directive = /^ {0,3}::(code-comment|created-thread)\{(.*)\}\s*$/.exec(line)
    const directiveAttributes = directive ? readCodexAttributes(directive[2]) : null
    if (directive && directiveAttributes) {
      if (directive[1] === 'code-comment' && directiveAttributes.title && directiveAttributes.body && directiveAttributes.file) {
        flush()
        parts.push({ kind: 'comment', attributes: directiveAttributes })
        continue
      }
      if (directive[1] === 'created-thread' && (directiveAttributes.threadId || directiveAttributes.clientThreadId)) {
        flush()
        parts.push({ kind: 'thread', attributes: directiveAttributes })
        continue
      }
    }
    markdown.push(line)
  }
  flush()
  return parts
}

interface TextNode {
  type: string
  value?: string
  children?: TextNode[]
  data?: { hName: string; hProperties: Record<string, string> }
  position?: { start: { offset?: number }; end: { offset?: number } }
}

/** Decode inline attribution after Markdown parsing, so code and URLs remain literal. */
export function remarkCodexAttribution() {
  return (tree: TextNode, file: { value: unknown }) => {
    const source = String(file.value)
    const visit = (parent: TextNode) => {
      if (!parent.children || ['code', 'inlineCode', 'html'].includes(parent.type)) return
      parent.children = parent.children.flatMap((node) => {
        if (node.type !== 'text' || !node.value) {
          visit(node)
          return [node]
        }
        const text = node.value
        // Markdown unescapes quoted attributes. Read them from their original source span.
        const original = source.slice(node.position?.start.offset, node.position?.end.offset)
        const matches: { index: number; length: number; label: string; title: string; followup: boolean }[] = []
        let searchOffset = 0
        for (const raw of original.matchAll(/:codex-followup\[([^\]\n]+)\]\{((?:[^"}\n]|"(?:[^"\\]|\\.)*")*)\}/g)) {
          const attributes = readCodexAttributes(raw[2])
          // CommonMark removes punctuation escapes, including escaped quotes, in text nodes.
          const visible = raw[0].replace(/\\([!"#$%&'()*+,\-./:;<=>?@[\]\\^_`{|}~])/g, '$1')
          const index = text.indexOf(visible, searchOffset)
          if (index < 0) continue
          searchOffset = index + visible.length
          if (!attributes?.prompt) continue
          matches.push({ index, length: visible.length, label: raw[1], title: attributes.prompt, followup: true })
        }
        for (const match of text.matchAll(/\uE200(cite|filecite)\uE202([^\uE201\n]+)\uE201/g)) {
          const label = match[1] === 'filecite' ? 'File source' : 'Sources'
          // Hook text does not carry citation URLs. Keep the ids; never invent destinations.
          const title = `${label}: ${match[2].replace(/\uE202/g, ', ')}`
          matches.push({ index: match.index, length: match[0].length, label: `[${title}]`, title, followup: false })
        }
        matches.sort((left, right) => left.index - right.index)
        const nodes: TextNode[] = []
        let offset = 0
        for (const match of matches) {
          if (match.index < offset) continue
          if (match.index > offset) nodes.push({ type: 'text', value: text.slice(offset, match.index) })
          nodes.push({
            type: 'text',
            value: match.label,
            data: {
              hName: 'span',
              hProperties: {
                className: match.followup ? 'codex-text__followup' : 'codex-text__citation',
                title: match.title,
              },
            },
          })
          offset = match.index + match.length
        }
        if (offset < text.length) nodes.push({ type: 'text', value: text.slice(offset) })
        return nodes
      })
    }
    visit(tree)
  }
}
