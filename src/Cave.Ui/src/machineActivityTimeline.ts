import type {
  AgentActivity,
  AgentActivityPhase,
  AgentInstructionMarker,
  ConversationMessage,
  LiveArchitectureSnapshot,
  WorkspaceOverview,
} from './types'

export interface MachineActivitySource {
  workspace: WorkspaceOverview
  update: LiveArchitectureSnapshot
}

export interface MachineActivityMilestone {
  id: string
  label: string
  detail: string
  observedAtUtc: string
  positionPercent: number
  kind: 'Start' | 'Architecture' | 'Edit' | 'Phase' | 'Complete'
}

export interface MachineActivityLane {
  agent: AgentActivity
  durationMs: number
  durationPercent: number
  milestones: MachineActivityMilestone[]
  completionSummary: MachineActivityNarrative | null
}

export interface MachineActivityNarrative {
  text: string
  occurredAtUtc: string
  isTruncated: boolean
}

export interface MachineActivityProjectTimeline {
  workspace: WorkspaceOverview
  instructionId: string | null
  instructionAtUtc: string
  prompt: MachineActivityNarrative | null
  lanes: MachineActivityLane[]
}

/** Builds a comparable, evidence-only timeline for every currently active agent. */
export function buildMachineActivityTimelines(
  sources: MachineActivitySource[],
  nowMs: number,
): MachineActivityProjectTimeline[] {
  const activeAgents = sources.flatMap(({ workspace, update }) => {
    const instructionAtMs = update.snapshot.activity.latestInstruction === null
      ? 0
      : Date.parse(update.snapshot.activity.latestInstruction.observedAtUtc)
    const endAtMs = timelineEndMs(workspace, update, nowMs)
    return update.snapshot.activity.agents
      .filter((agent) => agent.state === 'Active')
      .map((agent) => ({ agent, instructionAtMs, endAtMs }))
  })
  const maxDurationMs = Math.max(
    1,
    ...activeAgents.map(({ agent, instructionAtMs, endAtMs }) => Math.max(
      0,
      endAtMs - Math.max(Date.parse(agent.startedAtUtc), instructionAtMs),
    )),
  )

  return sources.map(({ workspace, update }) => {
    const { activity, conversation, graph } = update.snapshot
    const endAtMs = timelineEndMs(workspace, update, nowMs)
    const agents = activity.agents
      .filter((agent) => agent.state === 'Active')
      .sort((left, right) => Number(left.isSubagent) - Number(right.isSubagent)
        || Date.parse(left.startedAtUtc) - Date.parse(right.startedAtUtc))
    const earliestStart = agents.reduce(
      (earliest, agent) => Date.parse(agent.startedAtUtc) < Date.parse(earliest)
        ? agent.startedAtUtc
        : earliest,
      agents[0]?.startedAtUtc ?? update.observedAtUtc,
    )

    return {
      workspace,
      instructionId: activity.latestInstruction?.id ?? null,
      instructionAtUtc: activity.latestInstruction?.observedAtUtc ?? earliestStart,
      prompt: findPrompt(conversation.messages, activity.latestInstruction),
      lanes: agents.map((agent) => {
        const agentStartedAtMs = Date.parse(agent.startedAtUtc)
        const instructionAtMs = activity.latestInstruction === null
          ? agentStartedAtMs
          : Date.parse(activity.latestInstruction.observedAtUtc)
        const startedAtMs = Math.max(agentStartedAtMs, instructionAtMs)
        const durationMs = Math.max(0, endAtMs - startedAtMs)
        const nodeNames = new Map(graph.nodes.map((node) => [node.id, node.name]))
        const architectureSteps = activity.nodes
          .filter((node) => node.agentId === agent.agentId)
          .map((node) => ({
            id: `node:${node.nodeId}:${node.updatedAtUtc}`,
            label: nodeNames.get(node.nodeId) ?? 'Architecture scope',
            detail: node.evidence === 'Observed' ? 'Observed architecture work' : 'Agent-declared scope',
            observedAtUtc: node.updatedAtUtc,
            kind: 'Architecture' as const,
          }))
        const editSteps = activity.recentEdits
          .filter((edit) => edit.agentId === agent.agentId)
          .map((edit) => ({
            id: `edit:${edit.filePath}:${edit.observedAtUtc}`,
            label: fileName(edit.filePath),
            detail: 'Observed edit',
            observedAtUtc: edit.observedAtUtc,
            kind: 'Edit' as const,
          }))
        const middleSteps = deduplicateSteps([...architectureSteps, ...editSteps])
          .sort((left, right) => Date.parse(left.observedAtUtc) - Date.parse(right.observedAtUtc))
          .slice(-3)
        const rawSteps = [
          {
            id: `start:${agent.agentId}`,
            label: agentStartedAtMs < startedAtMs
              ? 'Continued into prompt'
              : agent.isSubagent ? 'Subagent started' : 'Main agent started',
            detail: agentStartedAtMs < startedAtMs
              ? 'Agent was already active when this instruction arrived'
              : agent.hasObservedActivity ? 'Observed lifecycle evidence' : 'Declared scope evidence',
            observedAtUtc: new Date(startedAtMs).toISOString(),
            kind: 'Start' as const,
          },
          ...middleSteps,
          {
            id: `phase:${agent.agentId}:${agent.phase ?? 'Working'}`,
            label: phaseLabel(agent.phase),
            detail: agent.summary ?? 'Current observed phase',
            observedAtUtc: agent.updatedAtUtc,
            kind: 'Phase' as const,
          },
        ]

        const milestones = spreadMilestones(rawSteps.map((step) => ({
          ...step,
          positionPercent: step.kind === 'Start'
            ? 0
            : step.kind === 'Phase'
              ? 100
              : durationMs === 0
                ? 50
                : clamp((Date.parse(step.observedAtUtc) - startedAtMs) / durationMs * 100, 8, 92),
        })))

        return {
          agent,
          durationMs,
          durationPercent: clamp(durationMs / maxDurationMs * 100, 18, 100),
          milestones,
          completionSummary: findCompletionSummary(
            conversation.messages,
            activity.latestInstruction,
            agent,
          ),
        }
      }),
    }
  }).filter((project) => project.lanes.length > 0)
}

function findPrompt(
  messages: ConversationMessage[],
  instruction: AgentInstructionMarker | null,
): MachineActivityNarrative | null {
  if (instruction === null) return null

  return toNarrative(findLast(messages, (message) => (
    message.role === 'User'
    && message.kind === 'Prompt'
    && belongsToInstruction(message, instruction.id)
  )), true)
}

function findCompletionSummary(
  messages: ConversationMessage[],
  instruction: AgentInstructionMarker | null,
  agent: AgentActivity,
): MachineActivityNarrative | null {
  if (instruction === null) return null

  const instructionAtMs = Date.parse(instruction.observedAtUtc)
  const finalMessage = findLast(messages, (message) => {
    if (message.role !== 'Assistant' || message.kind !== 'Final' || message.isStreaming) return false
    if (Date.parse(message.occurredAtUtc) < instructionAtMs) return false

    return agent.isSubagent
      ? message.isSubagent && message.agentId === agent.agentId
      : !message.isSubagent && belongsToInstruction(message, instruction.id)
  })
  return toNarrative(finalMessage)
}

function belongsToInstruction(message: ConversationMessage, instructionId: string): boolean {
  const messageInstructionId = `${message.sessionId ?? 'session'}:${message.turnId ?? message.eventId}`
  return messageInstructionId === instructionId
}

function findLast<T>(items: T[], predicate: (item: T) => boolean): T | null {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    if (predicate(items[index])) return items[index]
  }
  return null
}

function toNarrative(
  message: ConversationMessage | null,
  sanitizeInstruction = false,
): MachineActivityNarrative | null {
  return message === null
    ? null
    : {
        text: sanitizeInstruction ? extractUserInstructionText(message.text) : message.text,
        occurredAtUtc: message.occurredAtUtc,
        isTruncated: message.isTruncated,
      }
}

/**
 * Conversation evidence can include Codex-supplied ambient UI context before the
 * user's real instruction. That context is useful to the agent, but it is not the
 * project prompt and must not become a visible timeline root.
 */
export function extractUserInstructionText(text: string): string {
  const withoutLeadingAmbientContext = text.replace(
    /^\s*<in-app-browser-context source=(?:"ambient-ui-state"|'ambient-ui-state')>[\s\S]*?<\/in-app-browser-context>\s*(?:## My request:\s*(?:\r?\n|$))?/i,
    '',
  ).trim()

  return withoutLeadingAmbientContext.length > 0
    ? withoutLeadingAmbientContext
    : 'User instruction received'
}

function timelineEndMs(
  workspace: WorkspaceOverview,
  update: LiveArchitectureSnapshot,
  nowMs: number,
): number {
  if (workspace.activeAgentCount > 0) return nowMs

  const observedAtMs = Date.parse(update.observedAtUtc)
  return Number.isNaN(observedAtMs) ? nowMs : Math.min(nowMs, observedAtMs)
}

function spreadMilestones(
  milestones: MachineActivityMilestone[],
): MachineActivityMilestone[] {
  if (milestones.length <= 2) return milestones

  const middleCount = milestones.length - 2
  const minimumGap = Math.min(14, 72 / (middleCount + 1))
  let previousPosition = 0
  return milestones.map((milestone, index) => {
    if (index === 0 || index === milestones.length - 1) return milestone

    const remainingMiddle = middleCount - index
    const minimum = previousPosition + minimumGap
    const maximum = 92 - Math.max(0, remainingMiddle) * minimumGap
    const positionPercent = clamp(milestone.positionPercent, minimum, maximum)
    previousPosition = positionPercent
    return { ...milestone, positionPercent }
  })
}

function deduplicateSteps<T extends { label: string; kind: string }>(steps: T[]): T[] {
  const latest = new Map<string, T>()
  for (const step of steps) latest.set(`${step.kind}:${step.label}`, step)
  return [...latest.values()]
}

function fileName(path: string): string {
  const normalized = path.replaceAll('\\', '/')
  return normalized.slice(normalized.lastIndexOf('/') + 1) || path
}

function phaseLabel(phase: AgentActivityPhase | null): string {
  return phase === null ? 'Working' : phase
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value))
}
