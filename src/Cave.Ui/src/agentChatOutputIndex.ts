import { toString } from 'mdast-util-to-string'
import { useMemo, useState } from 'react'
import remarkGfm from 'remark-gfm'
import remarkParse from 'remark-parse'
import { unified } from 'unified'
import { visit } from 'unist-util-visit'
import { compareChatMessages, type AgentChatArtifact, type AgentChatMessage } from './agentChat'

interface MessageVisuals {
  caption: string
  diagrams: { source: string; caption: string }[]
}

export type AgentChatOutput = {
  id: string
  messageId: string
  occurredAtUtc: string
  caption: string
} & ({ kind: 'diagram'; source: string } | { kind: 'image'; artifact: AgentChatArtifact })

const markdown = unified().use(remarkParse).use(remarkGfm)

function readVisuals(text: string): MessageVisuals {
  const diagrams: MessageVisuals['diagrams'] = []
  let caption = ''
  visit(markdown.parse(text), (node) => {
    if (node.type === 'paragraph' || node.type === 'heading') {
      const plain = toString(node, { includeImageAlt: false, includeHtml: false }).replace(/\s+/g, ' ').trim()
      if (plain) caption = plain
    }
    if (node.type === 'code' && node.lang === 'mermaid') diagrams.push({ source: node.value.trimEnd(), caption })
  })
  return { caption, diagrams }
}

/** Index only the messages currently shared for this agent. Polls may replace identical message objects. */
export function useAgentChatOutputs(messages: readonly AgentChatMessage[] | undefined) {
  const [lastRead, setLastRead] = useState(() => new Map<string, MessageVisuals>())
  const index = useMemo(() => {
    const parsed = new Map<string, MessageVisuals>()
    const outputs: AgentChatOutput[] = []
    const candidates = messages?.filter((message) => message.artifacts.length || /`{3}|~{3}/.test(message.text)) ?? []
    candidates.sort((left, right) => compareChatMessages(right, left))
    for (const message of candidates) {
      const visuals = lastRead.get(message.text) ?? parsed.get(message.text) ?? readVisuals(message.text)
      parsed.set(message.text, visuals)
      const origin = { messageId: message.id, occurredAtUtc: message.occurredAtUtc }
      visuals.diagrams.forEach(({ source, caption }, index) => {
        outputs.push({ ...origin, id: JSON.stringify([message.id, 'diagram', index]), kind: 'diagram', source, caption })
      })
      for (const artifact of message.artifacts) {
        outputs.push({ ...origin, id: JSON.stringify([message.id, 'image', artifact.id]), kind: 'image', artifact, caption: visuals.caption })
      }
    }
    return { parsed, outputs }
  }, [messages, lastRead])
  if (index.parsed.size !== lastRead.size || [...index.parsed.keys()].some((text) => !lastRead.has(text))) {
    setLastRead(index.parsed)
  }
  return index.outputs
}
