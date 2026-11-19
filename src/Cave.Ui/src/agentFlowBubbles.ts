import { useMemo, useState } from 'react'
import { compareChatMessages, type AgentChatAgent, type AgentChatArtifact, type AgentChatSession } from './agentChat'
import { chatMessageOutputs, readAgentChatMarkdown } from './agentChatOutputIndex'
import { getLongTextPreview } from './longTextPreview'
import { observedTaskRoot, type MachineActivityLane } from './machineActivityTimeline'

export interface AgentFlowBubble {
  id: string
  parent: string
  kind: 'task' | 'activity' | 'text' | 'image' | 'diagram'
  label: string
  text: string
  messageId?: string
  artifact?: AgentChatArtifact
  source?: string
  size?: 'expanded' | 'report'
  reading?: { words: number; shortened?: boolean; evidence?: string }
}

type BubbleContent = Omit<AgentFlowBubble, 'id' | 'parent'>
type ParsedMarkdown = ReturnType<typeof readAgentChatMarkdown> & { preview: ReturnType<typeof getLongTextPreview> }

const excerptLength = 220

function excerpt(text: string | null | undefined) {
  const plain = text?.replace(/\s+/g, ' ').trim() ?? ''
  return plain.length <= excerptLength ? plain
    : plain.slice(0, excerptLength - 1).replace(/[\uD800-\uDBFF]$/, '').trimEnd() + '…'
}

function graphCopy(preview: ReturnType<typeof getLongTextPreview>, text: string, shortened?: boolean, evidence?: string): Pick<BubbleContent, 'text' | 'size' | 'reading'> {
  if (preview.isLong) return { text: excerpt(preview.excerpt), size: 'report', reading: { words: preview.wordCount, shortened, evidence } }
  const copy = excerpt(text)
  return copy.length > 76 ? { text: copy, size: 'expanded' } : { text: copy }
}

function hasTaskAncestry(agent: AgentChatAgent, agents: Map<string, AgentChatAgent>, taskId: string) {
  const visited = new Set<string>()
  let current: AgentChatAgent | undefined = agent
  while (current && !visited.has(current.id)) {
    if (current.id === taskId) return current.parentId === null
    visited.add(current.id)
    current = current.parentId ? agents.get(current.parentId) : undefined
  }
  return false
}

function latestPublication(agent: AgentChatAgent, read: (text: string) => ParsedMarkdown, allowImages: boolean) {
  let prompt: BubbleContent | undefined
  let reply: BubbleContent | undefined
  let image: BubbleContent | undefined
  let diagram: BubbleContent | undefined
  for (const message of [...agent.messages].sort((left, right) => compareChatMessages(right, left))) {
    if (message.role === 'User') {
      if (!prompt && message.kind === 'Prompt') {
        const markdown = read(message.text)
        prompt = { kind: 'task', label: 'Task prompt', ...graphCopy(markdown.preview, markdown.visibleText, message.isTruncated), messageId: message.id }
      }
      continue
    }
    if (message.role !== 'Assistant' || (message.kind !== 'Commentary' && message.kind !== 'Final')) continue
    if (reply && (!allowImages || image || !message.artifacts.some((artifact) => artifact.contentType))
      && (diagram || !message.text.includes('mermaid'))) continue

    const markdown = read(message.text)
    if (!reply && markdown.visibleText) {
      reply = { kind: 'text', label: message.kind === 'Final' ? 'Final reply' : 'Commentary',
        ...graphCopy(markdown.preview, markdown.visibleText, message.isTruncated), messageId: message.id }
    }
    for (const output of chatMessageOutputs(message, markdown)) {
      const origin = { text: excerpt(output.caption), messageId: output.messageId }
      if (output.kind === 'diagram' && !diagram) {
        diagram = { ...origin, kind: 'diagram', label: 'Diagram', source: output.source }
      }
      if (output.kind === 'image' && allowImages && !image && output.artifact.contentType) {
        image = { ...origin, kind: 'image', label: 'Image', artifact: output.artifact }
      }
    }
  }
  return { prompt, reply, image, diagram }
}

function laneWork(lane: MachineActivityLane, prompt?: BubbleContent) {
  const { agent } = lane
  const focus = agent.summaryEvidence === 'Declared' ? agent.summary?.trim() ?? '' : ''
  const observed = agent.lastObservedActivity?.trim()
  const action = observed || (agent.summaryEvidence === 'Observed' ? agent.summary?.trim() ?? '' : '')
  const workCopy = (text: string, evidence: string) => graphCopy(getLongTextPreview(text), text, false, evidence)
  const bubbles: BubbleContent[] = []
  const task = prompt?.text ? prompt : focus ? { kind: 'task' as const, label: 'Declared focus', ...workCopy(focus, 'summary') } : undefined
  if (task) bubbles.push(task)

  const work: BubbleContent[] = []
  if (prompt?.text && focus && prompt.text !== excerpt(focus)) work.push({ kind: 'activity', label: 'Declared focus', ...workCopy(focus, 'summary') })
  if (action && task?.text !== excerpt(action) && action !== focus) work.push({ kind: 'activity', label: 'Observed action', ...workCopy(action, observed && observed !== agent.summary?.trim() ? 'action' : 'summary') })
  if (work.length < 2) {
    const milestone = lane.milestones.findLast((step) => {
      const name = excerpt(step.label)
      return (step.kind === 'Edit' || step.kind === 'Architecture') && name && name !== action && name !== task?.text
        && name !== focus && !action.endsWith('/' + name) && !action.endsWith('\\' + name) && !action.endsWith(': ' + name)
    })
    if (milestone) work.push({ kind: 'activity', label: milestone.detail, ...workCopy(milestone.label, milestone.id) })
  }
  return [...bubbles, ...work]
}

/** Shows independently evidenced work and the newest published item of each output type per lane. */
export function useAgentFlowBubbles(lanes: MachineActivityLane[], chat: AgentChatSession, taskId: string | null | undefined) {
  const { agents, sharing, periodId } = chat
  const scope = taskId && (sharing === 'Legacy' || (sharing === 'Shared' && periodId))
    ? JSON.stringify([taskId, sharing, periodId]) : null
  const [cache, setCache] = useState(() => ({ scope: null as string | null, markdown: new Map<string, ParsedMarkdown>() }))
  const projection = useMemo(() => {
    const markdown = new Map<string, ParsedMarkdown>()
    const read = (text: string) => {
      let parsed = markdown.get(text) ?? (scope === cache.scope ? cache.markdown.get(text) : undefined)
      if (!parsed) {
        const content = readAgentChatMarkdown(text)
        parsed = { ...content, preview: getLongTextPreview(text, 'markdown', content.visibleText) }
      }
      markdown.set(text, parsed)
      return parsed
    }
    const byId = new Map<string, AgentChatAgent>()
    if (scope) {
      for (const agent of agents) byId.set(agent.id, agent)
      if (byId.size !== agents.length) byId.clear()
    }

    const bubbles: AgentFlowBubble[] = []
    for (const lane of lanes) {
      const parent = lane.agent.agentId
      const agent = lane.agent.publicId ? byId.get(lane.agent.publicId) : undefined
      const publication = taskId && agent && hasTaskAncestry(agent, byId, taskId)
        && observedTaskRoot(lanes, parent)?.agent.publicId === taskId ? latestPublication(agent, read, sharing === 'Shared') : undefined
      const work = laneWork(lane, publication?.prompt)
      for (const bubble of [...work, publication?.reply, publication?.image, publication?.diagram]) {
        if (!bubble) continue
        // A new publication replaces the same leaf. Revocation or a new sharing period
        // discards it. The latest messageId still opens the exact source in the inspector.
        const slot = bubble.messageId ? scope : bubble.label
        const id = 'flow-work:' + encodeURIComponent(JSON.stringify([parent, bubble.kind, slot]))
        bubbles.push({ ...bubble, parent, id })
      }
    }
    return { bubbles, markdown }
  }, [agents, cache, lanes, scope, sharing, taskId])

  if (scope !== cache.scope || projection.markdown.size !== cache.markdown.size
    || [...projection.markdown.keys()].some((text) => !cache.markdown.has(text))) {
    setCache({ scope, markdown: projection.markdown })
  }
  return projection.bubbles
}
