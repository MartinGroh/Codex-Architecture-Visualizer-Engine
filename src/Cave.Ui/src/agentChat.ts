import { useCallback, useEffect, useState } from 'react'

export type AgentChatSharing = 'Private' | 'Legacy' | 'Shared'
export type AgentChatAvailability = 'Available' | 'Partial' | 'Unavailable'
export type AgentChatStatus = AgentChatAvailability | 'Idle' | 'Loading' | 'Reconnecting'

export interface AgentChatArtifact {
  id: string
  contentType: string | null
}

export interface AgentChatMessage {
  id: string
  role: 'User' | 'Assistant'
  kind: 'Prompt' | 'Commentary' | 'Final'
  occurredAtUtc: string
  text: string
  isTruncated: boolean
  artifacts: AgentChatArtifact[]
}

export interface AgentChatAgent {
  id: string
  parentId: string | null
  displayName: string
  startedAtUtc: string | null
  status: AgentChatAvailability
  messages: AgentChatMessage[]
}

export interface AgentChatResponse {
  sharing: AgentChatSharing
  status: AgentChatAvailability
  agents: AgentChatAgent[]
  periodId: string | null
  sourceMode?: 'Live' | 'Demo'
}

export interface AgentChatSession {
  sharing: AgentChatSharing | null
  status: AgentChatStatus
  agents: AgentChatAgent[]
  periodId: string | null
  sourceMode?: 'Live' | 'Demo'
  artifactUrl: (artifactId: string) => string
  enableSharing: () => Promise<void>
}

const pollIntervalMs = 2_000
const retryIntervalMs = 5_000
const requestTimeoutMs = 20_000
const historyLimit = 512
const emptyAgents: AgentChatAgent[] = []

export function compareChatMessages(left: AgentChatMessage, right: AgentChatMessage) {
  return Date.parse(left.occurredAtUtc) - Date.parse(right.occurredAtUtc) || left.id.localeCompare(right.id)
}

/** Retain rolling pages only within the same confirmed sharing period and agent identity. */
export function mergeChatHistory(previous: AgentChatResponse | null, incoming: AgentChatResponse): AgentChatResponse {
  if (incoming.sharing === 'Private') return { ...incoming, agents: [] }
  const continuesPeriod = incoming.sharing === 'Shared' && incoming.periodId !== null && incoming.status !== 'Unavailable'
    && previous?.sharing === 'Shared' && previous.periodId === incoming.periodId
  const oldAgents = new Map(continuesPeriod ? previous.agents.map((agent) => [agent.id, agent]) : [])
  const agents = incoming.agents.map((agent) => {
    const oldAgent = oldAgents.get(agent.id)
    const sameIdentity = oldAgent?.parentId === agent.parentId
    const messages = new Map(agent.status !== 'Unavailable' && sameIdentity
      ? oldAgent.messages.map((message) => [message.id, message])
      : [])
    agent.messages.forEach((message) => messages.set(message.id, message))
    return { ...agent, messages: [...messages.values()].sort(compareChatMessages) }
  })
  const retained = new Set(agents.flatMap(({ messages }) => messages).sort(compareChatMessages).slice(-historyLimit))
  return { ...incoming, agents: agents.map((agent) => ({
    ...agent, messages: agent.messages.filter((message) => retained.has(message)),
  })) }
}

function readChatResponse(value: unknown): AgentChatResponse {
  const response = value as Partial<AgentChatResponse> | null
  if (!response || !['Private', 'Legacy', 'Shared'].includes(response.sharing ?? '')
    || !['Available', 'Partial', 'Unavailable'].includes(response.status ?? '')
    || !Array.isArray(response.agents)
    || response.agents.some((agent) => !agent || typeof agent.id !== 'string' || !Array.isArray(agent.messages))) {
    throw new Error('Invalid agent chat response')
  }
  return { ...response, periodId: typeof response.periodId === 'string' ? response.periodId : null } as AgentChatResponse
}

/** One cancelable refresh loop for the selected task, shared by all visible panes. */
export function useAgentChat(workspaceId: string, taskId: string | null | undefined, sharingEnabled: boolean | null): AgentChatSession {
  const scope = taskId ? new URLSearchParams({ workspace: workspaceId, task: taskId }).toString() : null
  const allowed = scope !== null && sharingEnabled !== false
  const [version, refresh] = useState(0)
  const [state, setState] = useState<{ scope: string; response: AgentChatResponse | null; unavailable?: boolean } | null>(null)
  if (state && (!allowed || state.scope !== scope)) setState(null)

  useEffect(() => {
    if (!allowed || scope === null) return
    let disposed = false
    let activeRequest: AbortController | null = null
    let timer: number | undefined

    const poll = async () => {
      if (disposed || activeRequest || document.hidden) return
      const request = new AbortController()
      activeRequest = request
      let delay = pollIntervalMs
      let timedOut = false
      const deadline = window.setTimeout(() => {
        timedOut = true
        request.abort()
      }, requestTimeoutMs)
      try {
        const result = await fetch('/api/agent-chat?' + scope, { cache: 'no-store', signal: request.signal })
        if (disposed || request.signal.aborted) return
        if (result.status === 404) {
          delay = retryIntervalMs
          setState({ scope, response: null, unavailable: true })
          return
        }
        if (!result.ok) throw new Error('Agent chat request failed')
        const response = readChatResponse(await result.json())
        if (disposed || request.signal.aborted) return
        setState((current) => ({
          scope,
          response: mergeChatHistory(current?.scope === scope ? current.response : null, response),
        }))
      } catch {
        if (disposed || (request.signal.aborted && !timedOut)) return
        delay = retryIntervalMs
        setState({ scope, response: null })
      } finally {
        window.clearTimeout(deadline)
        activeRequest = null
        if (!disposed && !document.hidden) timer = window.setTimeout(() => void poll(), delay)
      }
    }
    const onVisibilityChange = () => {
      window.clearTimeout(timer)
      if (document.hidden) activeRequest?.abort()
      else void poll()
    }
    document.addEventListener('visibilitychange', onVisibilityChange)
    void poll()
    return () => {
      disposed = true
      activeRequest?.abort()
      window.clearTimeout(timer)
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }, [allowed, scope, version])

  const snapshot = allowed && state?.scope === scope ? state : null
  const response = snapshot?.response ?? null
  const artifactUrl = useCallback((artifactId: string) =>
    '/api/agent-chat/artifacts?' + (scope ?? '') + '&artifact=' + encodeURIComponent(artifactId), [scope])
  return {
    sharing: sharingEnabled === false ? 'Private' : response?.sharing ?? null,
    status: scope === null || sharingEnabled === false ? 'Idle'
      : response?.status ?? (snapshot === null ? 'Loading' : snapshot.unavailable ? 'Unavailable' : 'Reconnecting'),
    agents: response?.agents ?? emptyAgents,
    periodId: response?.periodId ?? null,
    sourceMode: response?.sourceMode,
    artifactUrl,
    enableSharing: async () => {
      const response = await fetch('/api/conversation/sharing?' + new URLSearchParams({ workspace: workspaceId }), {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ enabled: true }),
      })
      if (!response.ok) throw new Error('Could not enable live agent chat.')
      refresh((value) => value + 1)
    },
  }
}
