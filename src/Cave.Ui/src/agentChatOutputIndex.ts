import { toString } from 'mdast-util-to-string'
import { useMemo, useState } from 'react'
import remarkGfm from 'remark-gfm'
import remarkParse from 'remark-parse'
import { unified } from 'unified'
import { visit } from 'unist-util-visit'
import { compareChatMessages, type AgentChatArtifact, type AgentChatMessage } from './agentChat'

interface AgentChatMarkdown {
  caption: string
  visibleText: string
  diagrams: { source: string; caption: string }[]
}

export type AgentChatOutput = {
  id: string
  messageId: string
  occurredAtUtc: string
  caption: string
} & ({ kind: 'diagram'; source: string } | { kind: 'image'; artifact: AgentChatArtifact })

const markdown = unified().use(remarkParse).use(remarkGfm)

/** Extracts visible text, including code-only replies, and indexes fenced diagrams. */
export function readAgentChatMarkdown(text: string): AgentChatMarkdown {
  const diagrams: AgentChatMarkdown['diagrams'] = []
  const blocks: string[] = []
  let caption = ''
  visit(markdown.parse(text), (node) => {
    if (node.type === 'paragraph' || node.type === 'heading' || node.type === 'tableCell') {
      const plain = toString(node, { includeImageAlt: false, includeHtml: false }).replace(/\s+/g, ' ').trim()
      if (plain) {
        blocks.push(plain)
        if (node.type !== 'tableCell') caption = plain
      }
    }
    if (node.type === 'code' && node.value.trim()) {
      if (node.lang === 'mermaid') diagrams.push({ source: node.value.trimEnd(), caption })
      else blocks.push(node.value.trim())
    }
  })
  return { caption, visibleText: blocks.join(' '), diagrams }
}

export function chatMessageOutputs(message: AgentChatMessage, markdown: AgentChatMarkdown): AgentChatOutput[] {
  const origin = { messageId: message.id, occurredAtUtc: message.occurredAtUtc }
  return [
    ...markdown.diagrams.map(({ source, caption }, index) => ({
      ...origin, id: JSON.stringify([message.id, 'diagram', index]), kind: 'diagram' as const, source, caption,
    })),
    ...message.artifacts.map((artifact) => ({
      ...origin, id: JSON.stringify([message.id, 'image', artifact.id]), kind: 'image' as const, artifact, caption: markdown.caption,
    })),
  ]
}

/** Index only the messages currently shared for this agent. Polls may replace identical message objects. */
export function useAgentChatOutputs(messages: readonly AgentChatMessage[] | undefined) {
  const [lastRead, setLastRead] = useState(() => new Map<string, AgentChatMarkdown>())
  const index = useMemo(() => {
    const parsed = new Map<string, AgentChatMarkdown>()
    const outputs: AgentChatOutput[] = []
    const candidates = messages?.filter((message) => message.artifacts.length || /`{3}|~{3}/.test(message.text)) ?? []
    candidates.sort((left, right) => compareChatMessages(right, left))
    for (const message of candidates) {
      const visuals = lastRead.get(message.text) ?? parsed.get(message.text) ?? readAgentChatMarkdown(message.text)
      parsed.set(message.text, visuals)
      outputs.push(...chatMessageOutputs(message, visuals))
    }
    return { parsed, outputs }
  }, [messages, lastRead])
  if (index.parsed.size !== lastRead.size || [...index.parsed.keys()].some((text) => !lastRead.has(text))) {
    setLastRead(index.parsed)
  }
  return index.outputs
}
