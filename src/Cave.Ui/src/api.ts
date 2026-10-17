import { App } from '@modelcontextprotocol/ext-apps'
import type {
  CaveToolResult,
  CaveInfoSnapshot,
  AgentFlowLightSnapshot,
  ConversationDelivery,
  LiveArchitectureSnapshot,
  WorkspaceOverviewSnapshot,
} from './types'
import {
  semanticWorkspaceSchemaVersion,
  type SemanticWorkspace,
  type SemanticWorkspaceBatch,
  type SemanticWorkspaceOperationResult,
} from './semanticWorkspaceTypes'
import { setEmbeddedHostTheme } from './theme'
import { readAgentStatistics, type AgentStatisticsSnapshot, type StatisticsReader } from './agentStatistics'

type UpdateHandler = (update: LiveArchitectureSnapshot) => void
type ErrorHandler = (error: Error) => void
type ConnectionHandler = (state: ArchitectureFeedConnectionState) => void
type HostBridgeHandler = (bridge: CodexHostBridge | null) => void

export interface CodexHostBridge {
  canSendMessage: boolean
  canSample: boolean
  sendMessage(text: string, expectedSessionId?: string): Promise<ConversationDelivery | null>
  sampleMemo(prompt: string, expectedSessionId?: string): Promise<string>
  setConversationSharing(enabled: boolean): Promise<void>
  readInfo(): Promise<CaveInfoSnapshot>
  readStatistics: StatisticsReader
  readSemanticWorkspace(): Promise<SemanticWorkspace>
  applySemanticBatch(batch: SemanticWorkspaceBatch): Promise<SemanticWorkspaceOperationResult>
  undoSemanticWorkspace(): Promise<SemanticWorkspaceOperationResult>
  redoSemanticWorkspace(): Promise<SemanticWorkspaceOperationResult>
  exportSemanticWorkspace(format: 'json' | 'markdown', includePresentation: boolean): Promise<string>
}

export type ArchitectureFeedConnectionState =
  | { status: 'Connecting'; attempt: 0; retryInMs: null }
  | { status: 'Connected'; attempt: 0; retryInMs: null }
  | { status: 'Failed'; attempt: 0; retryInMs: null }
  | { status: 'Reconnecting'; attempt: number; retryInMs: number }

const reconnectDelaysMs = [500, 1_000, 2_000, 4_000, 5_000] as const
const snapshotRequestTimeoutMs = 8_000
const eventStreamOpenTimeoutMs = 10_000

export async function connectArchitectureFeed(
  onUpdate: UpdateHandler,
  onError: ErrorHandler,
  onConnection: ConnectionHandler,
  browserWorkspaceId?: string,
  onHostBridge: HostBridgeHandler = () => {},
  signal?: AbortSignal,
): Promise<() => void> {
  onConnection({ status: 'Connecting', attempt: 0, retryInMs: null })
  if (window.parent === window) {
    if (browserWorkspaceId === undefined || browserWorkspaceId.trim().length === 0) {
      onHostBridge(null)
      throw new Error('Choose a workspace from the CAVE project dashboard.')
    }

    onHostBridge(createBrowserHostBridge(browserWorkspaceId))
    const stop = await connectBrowserFeed(onUpdate, onError, onConnection, browserWorkspaceId)
    return () => {
      onHostBridge(null)
      stop()
    }
  }

  return connectMcpFeed(onUpdate, onError, onConnection, onHostBridge, signal)
}

export async function readCaveInfo(signal?: AbortSignal): Promise<CaveInfoSnapshot> {
  const response = await fetch('/api/info', {
    headers: { Accept: 'application/json' },
    signal,
  })
  if (!response.ok) {
    throw new Error(`CAVE information request failed with status ${response.status}.`)
  }

  return (await response.json()) as CaveInfoSnapshot
}

export async function readWorkspaceOverview(signal?: AbortSignal): Promise<WorkspaceOverviewSnapshot> {
  const response = await fetch('/api/workspaces', {
    headers: { Accept: 'application/json' },
    signal,
  })
  if (!response.ok) {
    throw new Error(`Workspace catalog request failed with status ${response.status}.`)
  }

  return (await response.json()) as WorkspaceOverviewSnapshot
}

/** Reads only the bounded device feed; this route never requests a semantic graph. */
export async function readAgentFlowLight(workspaceId: string, signal?: AbortSignal): Promise<AgentFlowLightSnapshot> {
  const response = await fetch(`/api/agent-flow?workspace=${encodeURIComponent(workspaceId)}`, {
    headers: { Accept: 'application/json' },
    cache: 'no-store',
    signal,
  })
  if (!response.ok) {
    throw new Error(`Agent Flow Light request failed with status ${response.status}.`)
  }

  const snapshot: unknown = await response.json()
  if (typeof snapshot !== 'object' || snapshot === null
    || !('schemaVersion' in snapshot) || snapshot.schemaVersion !== 1
    || !('workspaceId' in snapshot) || snapshot.workspaceId !== workspaceId
    || !('agents' in snapshot) || !Array.isArray(snapshot.agents)
    || !('mainGoal' in snapshot) || typeof snapshot.mainGoal !== 'object' || snapshot.mainGoal === null) {
    throw new Error('The Agent Flow Light response is incompatible with this viewer.')
  }
  return snapshot as AgentFlowLightSnapshot
}

/** Builds the trusted local/Tailscale browser bridge to the host machine's exact Codex task. */
export function createBrowserHostBridge(workspaceId: string): CodexHostBridge {
  const encodedWorkspaceId = encodeURIComponent(workspaceId)
  return {
    canSendMessage: true,
    canSample: true,
    async sendMessage(text, expectedSessionId) {
      if (expectedSessionId === undefined || expectedSessionId.trim().length === 0) {
        throw new Error('CAVE has not observed an exact Codex task for this workspace yet.')
      }

      const response = await fetch(`/api/conversation/messages?workspace=${encodedWorkspaceId}`, {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedSessionId, text }),
      })
      if (!response.ok) {
        throw new Error(await readProblemDetail(response, 'The message could not be queued.'))
      }
      return readConversationDelivery(await response.json())
    },
    async sampleMemo(prompt, expectedSessionId) {
      if (expectedSessionId === undefined || expectedSessionId.trim().length === 0) {
        throw new Error('CAVE has not observed an exact Codex task for this workspace yet.')
      }

      const response = await fetch(`/api/conversation/node-memos?workspace=${encodedWorkspaceId}`, {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify({ expectedSessionId, text: prompt }),
      })
      if (!response.ok) {
        throw new Error(await readProblemDetail(response, 'The temporary Codex side chat failed.'))
      }
      const payload = (await response.json()) as { text?: unknown }
      if (typeof payload.text !== 'string' || payload.text.trim().length === 0) {
        throw new Error('The temporary Codex side chat returned no answer.')
      }
      return payload.text
    },
    async setConversationSharing(enabled) {
      const response = await fetch(`/api/conversation/sharing?workspace=${encodedWorkspaceId}`, {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify({ enabled }),
      })
      if (!response.ok) {
        throw new Error(await readProblemDetail(response, 'Conversation sharing could not be changed.'))
      }
    },
    async readInfo() {
      return readCaveInfo()
    },
    async readStatistics(taskId, signal) {
      return readAgentStatistics(workspaceId, taskId, signal)
    },
    async readSemanticWorkspace() {
      const response = await fetch(`/api/semantic-workspace?workspace=${encodedWorkspaceId}`, {
        headers: { Accept: 'application/json' },
      })
      return readSemanticWorkspaceResponse(response)
    },
    async applySemanticBatch(batch) {
      const response = await fetch(`/api/semantic-workspace/operations?workspace=${encodedWorkspaceId}`, {
        method: 'POST',
        headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
        body: JSON.stringify(batch),
      })
      return readSemanticOperationResponse(response)
    },
    async undoSemanticWorkspace() {
      const response = await fetch(`/api/semantic-workspace/undo?workspace=${encodedWorkspaceId}`, {
        method: 'POST',
        headers: { Accept: 'application/json' },
      })
      return readSemanticOperationResponse(response)
    },
    async redoSemanticWorkspace() {
      const response = await fetch(`/api/semantic-workspace/redo?workspace=${encodedWorkspaceId}`, {
        method: 'POST',
        headers: { Accept: 'application/json' },
      })
      return readSemanticOperationResponse(response)
    },
    async exportSemanticWorkspace(format, includePresentation) {
      const query = new URLSearchParams({
        workspace: workspaceId,
        format,
        includePresentation: includePresentation.toString(),
      })
      const response = await fetch(`/api/semantic-workspace/export?${query.toString()}`, {
        headers: { Accept: 'application/json' },
      })
      if (!response.ok) {
        throw new Error(await readProblemDetail(response, 'The semantic workspace could not be exported.'))
      }
      return readSemanticExport(await response.json())
    },
  }
}

async function readSemanticWorkspaceResponse(response: Response): Promise<SemanticWorkspace> {
  if (!response.ok) {
    throw new Error(await readProblemDetail(response, 'The semantic workspace could not be read.'))
  }
  return readSemanticWorkspace(await response.json())
}

async function readSemanticOperationResponse(response: Response): Promise<SemanticWorkspaceOperationResult> {
  if (!response.ok) {
    throw new Error(await readProblemDetail(response, 'The semantic workspace operation failed.'))
  }
  return readSemanticOperationResult(await response.json())
}

async function readProblemDetail(response: Response, fallback: string): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: string; title?: string }
    return problem.detail ?? problem.title ?? fallback
  } catch {
    return fallback
  }
}

function readConversationDelivery(value: unknown): ConversationDelivery {
  if (typeof value !== 'object' || value === null) {
    throw new Error('CAVE returned an invalid conversation delivery receipt.')
  }
  const candidate = value as Partial<ConversationDelivery>
  if (typeof candidate.messageId !== 'string'
    || typeof candidate.sessionId !== 'string'
    || typeof candidate.queuedAtUtc !== 'string'
    || typeof candidate.updatedAtUtc !== 'string'
    || !['Queued', 'Running', 'Completed', 'Failed'].includes(candidate.state ?? '')) {
    throw new Error('CAVE returned an invalid conversation delivery receipt.')
  }
  return candidate as ConversationDelivery
}

async function connectBrowserFeed(
  onUpdate: UpdateHandler,
  onError: ErrorHandler,
  onConnection: ConnectionHandler,
  workspaceId?: string,
): Promise<() => void> {
  if (workspaceId === undefined || workspaceId.trim().length === 0) {
    throw new Error('Choose a workspace from the CAVE project dashboard.')
  }

  const encodedWorkspaceId = encodeURIComponent(workspaceId)
  let stopped = false
  let generation = 0
  let reconnectAttempt = 0
  let activeController: AbortController | null = null
  let activeEvents: EventSource | null = null
  let reconnectTimer: number | null = null
  let streamOpenTimer: number | null = null

  const clearActiveAttempt = () => {
    activeController?.abort()
    activeController = null
    activeEvents?.close()
    activeEvents = null
    if (streamOpenTimer !== null) {
      window.clearTimeout(streamOpenTimer)
      streamOpenTimer = null
    }
  }

  const scheduleReconnect = (reason: Error, failedGeneration: number) => {
    if (stopped || failedGeneration !== generation) return

    generation += 1
    clearActiveAttempt()
    reconnectAttempt += 1
    const retryInMs = reconnectDelaysMs[
      Math.min(reconnectAttempt - 1, reconnectDelaysMs.length - 1)
    ]
    onError(reason)
    onConnection({ status: 'Reconnecting', attempt: reconnectAttempt, retryInMs })
    reconnectTimer = window.setTimeout(() => {
      reconnectTimer = null
      void connectOnce()
    }, retryInMs)
  }

  const connectOnce = async () => {
    if (stopped) return

    const attemptGeneration = ++generation
    const controller = new AbortController()
    activeController = controller
    let requestTimedOut = false
    const requestTimer = window.setTimeout(() => {
      requestTimedOut = true
      controller.abort()
    }, snapshotRequestTimeoutMs)

    try {
      const response = await fetch(`/api/snapshot?workspace=${encodedWorkspaceId}`, {
        headers: { Accept: 'application/json' },
        signal: controller.signal,
      })
      window.clearTimeout(requestTimer)
      if (stopped || attemptGeneration !== generation) return
      if (!response.ok) {
        throw new Error(`Snapshot request failed with status ${response.status}.`)
      }

      const update = (await response.json()) as LiveArchitectureSnapshot
      if (stopped || attemptGeneration !== generation) return

      activeController = null
      onUpdate(update)
      const events = new EventSource(`/events?workspace=${encodedWorkspaceId}`)
      activeEvents = events
      streamOpenTimer = window.setTimeout(() => {
        scheduleReconnect(
          new Error('The live architecture event stream did not reconnect in time.'),
          attemptGeneration,
        )
      }, eventStreamOpenTimeoutMs)

      events.onopen = () => {
        if (stopped || attemptGeneration !== generation) return
        if (streamOpenTimer !== null) {
          window.clearTimeout(streamOpenTimer)
          streamOpenTimer = null
        }
        reconnectAttempt = 0
        onConnection({ status: 'Connected', attempt: 0, retryInMs: null })
      }
      events.addEventListener('snapshot', (event) => {
        if (stopped || attemptGeneration !== generation) return
        try {
          onUpdate(JSON.parse(event.data) as LiveArchitectureSnapshot)
        } catch {
          scheduleReconnect(
            new Error('CAVE returned an invalid live snapshot event.'),
            attemptGeneration,
          )
        }
      })
      events.onerror = () => scheduleReconnect(
        new Error('The live architecture event stream disconnected.'),
        attemptGeneration,
      )
    } catch (reason: unknown) {
      window.clearTimeout(requestTimer)
      if (stopped || attemptGeneration !== generation) return
      activeController = null
      scheduleReconnect(
        requestTimedOut
          ? new Error('The CAVE snapshot request timed out.')
          : toError(reason),
        attemptGeneration,
      )
    }
  }

  void connectOnce()

  return () => {
    stopped = true
    generation += 1
    clearActiveAttempt()
    if (reconnectTimer !== null) {
      window.clearTimeout(reconnectTimer)
      reconnectTimer = null
    }
  }
}

async function connectMcpFeed(
  onUpdate: UpdateHandler,
  onError: ErrorHandler,
  onConnection: ConnectionHandler,
  onHostBridge: HostBridgeHandler,
  signal?: AbortSignal,
): Promise<() => void> {
  const app = new App(
    { name: 'CAVE', version: '0.3.0' },
    { availableDisplayModes: ['inline', 'fullscreen', 'pip'] },
    { autoResize: true, strict: true },
  )
  let stopped = false
  const receiveHostContext = () => setEmbeddedHostTheme(app.getHostContext()?.theme)
  app.addEventListener('hostcontextchanged', receiveHostContext)
  const stop = () => {
    if (stopped) return
    stopped = true
    app.removeEventListener('hostcontextchanged', receiveHostContext)
    signal?.removeEventListener('abort', stop)
    setEmbeddedHostTheme(undefined)
    onHostBridge(null)
    void app.close().catch(onError)
  }

  let initial: CaveToolResult
  try {
    initial = await receiveInitialMcpGraph(app, onConnection, signal)
    signal?.throwIfAborted()
  } catch (reason: unknown) {
    stop()
    onConnection({ status: 'Failed', attempt: 0, retryInMs: null })
    throw reason
  }
  signal?.addEventListener('abort', stop, { once: true })
  const bridge = createCodexHostBridge(app, initial, onUpdate)
  onUpdate(initial.update)
  onHostBridge(bridge)
  onConnection({ status: 'Connected', attempt: 0, retryInMs: null })

  void pollForUpdates(app, initial, onUpdate, onError, onConnection, () => stopped)
  return stop
}

/** Acquires one real graph through the owning MCP connection, including one bounded reattachment. */
async function receiveInitialMcpGraph(
  app: App,
  onConnection: ConnectionHandler,
  signal?: AbortSignal,
): Promise<CaveToolResult> {
  let finish!: (result: CaveToolResult) => void
  let fail!: (reason: unknown) => void
  const firstResult = new Promise<CaveToolResult>((resolve, reject) => { finish = resolve; fail = reject })
  const recoveryController = new AbortController()
  let workspacePath: string | null = null
  let connected = false
  let recoveryDue = false
  let recoveryStarted = false
  let settled = false
  let receivedResult: CaveToolResult | null = null
  let recoveryTimer: number | undefined
  const deadline = window.setTimeout(() => fail(new Error(workspacePath === null
    ? 'Codex did not provide CAVE with its initial graph or workspace. Reopen CAVE in Codex.'
    : 'CAVE could not retrieve its initial graph within 30 seconds. Reopen CAVE in Codex.')), 30_000)

  const recover = () => {
    if (!connected || !recoveryDue || recoveryStarted || settled || workspacePath === null) return
    recoveryStarted = true
    onConnection({ status: 'Reconnecting', attempt: 1, retryInMs: 0 })
    // CONSTRAINT: a host can miss the one-shot result while mounting the app. Reacquire
    // only the complete tool-input workspace, through the same canonical MCP tool.
    void app.callServerTool({
      name: 'cave_show_architecture', arguments: { workspacePath },
    }, { signal: recoveryController.signal }).then((response) => {
      if (settled) return
      const result = readToolResult(response)
      if (result === null) throw new Error('CAVE returned an invalid initial graph during reattachment.')
      finish(result)
    }).catch((reason: unknown) => { if (!settled) fail(reason) })
  }
  const receiveInput = (params: Parameters<NonNullable<typeof app.ontoolinput>>[0]) => {
    const path = params.arguments?.workspacePath
    if (workspacePath !== null || typeof path !== 'string' || !/^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+|\/)/.test(path)) return
    workspacePath = path
    recover()
  }
  const receiveResult = (params: Parameters<NonNullable<typeof app.ontoolresult>>[0]) => {
    if (params.isError) {
      fail(new Error('CAVE could not create the architecture snapshot. Check the CAVE plugin diagnostics and reopen the view.'))
      return
    }
    const result = readToolResult(params)
    if (result === null) {
      fail(new Error('Codex delivered an invalid CAVE graph result. Update CAVE and reopen the view.'))
      return
    }
    receivedResult = result
    if (connected) finish(result)
  }
  const cancel = () => fail(signal?.reason ?? new DOMException('CAVE connection cancelled.', 'AbortError'))
  app.addEventListener('toolinput', receiveInput)
  app.addEventListener('toolresult', receiveResult)
  signal?.addEventListener('abort', cancel, { once: true })
  // The deadline covers the handshake too. Placement is optional and must never
  // hold the graph hostage to an unanswered display-mode request.
  if (signal?.aborted) cancel()
  else void app.connect().then(() => {
    if (settled) return
    connected = true
    setEmbeddedHostTheme(app.getHostContext()?.theme)
    void requestLargestDisplay(app)
    if (receivedResult !== null) finish(receivedResult)
    else recoveryTimer = window.setTimeout(() => { recoveryDue = true; recover() }, 2_000)
  }).catch(fail)

  try {
    return await firstResult
  } finally {
    settled = true
    window.clearTimeout(deadline)
    window.clearTimeout(recoveryTimer)
    recoveryController.abort()
    signal?.removeEventListener('abort', cancel)
    app.removeEventListener('toolinput', receiveInput)
    app.removeEventListener('toolresult', receiveResult)
  }
}

/** Builds the capability-gated bridge from the embedded MCP App to its owning Codex host. */
export function createCodexHostBridge(
  app: Pick<App, 'getHostCapabilities' | 'sendMessage' | 'createSamplingMessage' | 'callServerTool'>,
  initial: CaveToolResult,
  onUpdate: UpdateHandler,
): CodexHostBridge {
  const capabilities = app.getHostCapabilities()
  return {
    canSendMessage: capabilities?.message !== undefined,
    canSample: capabilities?.sampling !== undefined,
    async sendMessage(text) {
      if (capabilities?.message === undefined) {
        throw new Error('This Codex host does not support messages from embedded apps.')
      }
      const result = await app.sendMessage({
        role: 'user',
        content: [{ type: 'text', text }],
      })
      if (result.isError) throw new Error('The Codex host declined the message.')
      return null
    },
    async sampleMemo(prompt) {
      if (capabilities?.sampling === undefined) {
        throw new Error('This Codex host does not support isolated model sampling.')
      }
      const result = await app.createSamplingMessage({
        messages: [{ role: 'user', content: { type: 'text', text: prompt } }],
        maxTokens: 1_200,
        systemPrompt: 'Answer as a concise architecture memo. Use only the supplied CAVE evidence and clearly identify uncertainty.',
      })
      return readSamplingText(result)
    },
    async setConversationSharing(enabled) {
      const response = await app.callServerTool({
        name: 'cave_set_conversation_sharing',
        arguments: { workspacePath: initial.workspaceRoot, enabled },
      })
      const result = readToolResult(response)
      if (result === null) throw new Error('CAVE returned an invalid sharing update.')
      onUpdate(result.update)
    },
    async readInfo() {
      const response = await app.callServerTool({ name: 'cave_get_info', arguments: {} })
      const result = readStructuredContent<CaveInfoSnapshot>(response)
      if (result === null) throw new Error('CAVE returned an invalid information payload.')
      return result
    },
    async readStatistics(taskId, signal) {
      signal?.throwIfAborted()
      const response = await app.callServerTool({ name: 'cave_get_statistics', arguments: { workspacePath: initial.workspaceRoot, taskId } })
      signal?.throwIfAborted()
      const result = readStructuredContent<AgentStatisticsSnapshot>(response)
      if (result === null || !Array.isArray(result.families)) throw new Error('CAVE returned invalid statistics.')
      return result
    },
    async readSemanticWorkspace() {
      const response = await app.callServerTool({
        name: 'cave_get_semantic_workspace',
        arguments: { workspacePath: initial.workspaceRoot },
      })
      return readSemanticWorkspace(readRequiredStructuredContent(
        response,
        'CAVE returned an invalid semantic workspace.',
      ))
    },
    async applySemanticBatch(batch) {
      const response = await app.callServerTool({
        name: 'cave_apply_semantic_operations',
        arguments: { workspacePath: initial.workspaceRoot, batch },
      })
      return readSemanticOperationResult(readRequiredStructuredContent(
        response,
        'CAVE returned an invalid semantic workspace operation result.',
      ))
    },
    async undoSemanticWorkspace() {
      const response = await app.callServerTool({
        name: 'cave_undo_semantic_workspace',
        arguments: { workspacePath: initial.workspaceRoot },
      })
      return readSemanticOperationResult(readRequiredStructuredContent(
        response,
        'CAVE returned an invalid semantic workspace undo result.',
      ))
    },
    async redoSemanticWorkspace() {
      const response = await app.callServerTool({
        name: 'cave_redo_semantic_workspace',
        arguments: { workspacePath: initial.workspaceRoot },
      })
      return readSemanticOperationResult(readRequiredStructuredContent(
        response,
        'CAVE returned an invalid semantic workspace redo result.',
      ))
    },
    async exportSemanticWorkspace(format, includePresentation) {
      const response = await app.callServerTool({
        name: 'cave_export_semantic_workspace',
        arguments: { workspacePath: initial.workspaceRoot, format, includePresentation },
      })
      return readSemanticExport(readRequiredStructuredContent(
        response,
        'CAVE returned an invalid semantic workspace export.',
      ))
    },
  }
}

async function pollForUpdates(
  app: App,
  initial: CaveToolResult,
  onUpdate: UpdateHandler,
  onError: ErrorHandler,
  onConnection: ConnectionHandler,
  isStopped: () => boolean,
): Promise<void> {
  let current = initial
  let reconnectAttempt = 0
  while (!isStopped()) {
    try {
      const response = await app.callServerTool({
        name: 'cave_poll_architecture',
        arguments: {
          subscriptionId: current.subscriptionId,
          afterVersion: current.update.version,
        },
      })
      const next = readToolResult(response)
      if (next === null) {
        throw new Error('CAVE returned an invalid live update payload.')
      }

      if (next.update.version > current.update.version) {
        current = next
        onUpdate(next.update)
      }
      if (reconnectAttempt > 0) {
        reconnectAttempt = 0
        onConnection({ status: 'Connected', attempt: 0, retryInMs: null })
      }
    } catch (reason: unknown) {
      if (!isStopped()) {
        reconnectAttempt += 1
        onError(toError(reason))
        onConnection({ status: 'Reconnecting', attempt: reconnectAttempt, retryInMs: 1_500 })
        try {
          const rehooked = await rehookMcpFeed(app, current)
          if (isStopped()) return
          current = rehooked
          onUpdate(rehooked.update)
          reconnectAttempt = 0
          onConnection({ status: 'Connected', attempt: 0, retryInMs: null })
        } catch (rehookReason: unknown) {
          if (!isStopped()) {
            onError(toError(rehookReason))
            await new Promise((resolve) => window.setTimeout(resolve, 1_500))
          }
        }
      }
    }
  }
}

export async function rehookMcpFeed(
  app: Pick<App, 'callServerTool'>,
  current: CaveToolResult,
): Promise<CaveToolResult> {
  const response = await app.callServerTool({
    name: 'cave_show_architecture',
    arguments: { workspacePath: current.workspaceRoot },
  })
  const rehooked = readToolResult(response)
  if (rehooked === null) {
    throw new Error('CAVE returned an invalid subscription recovery payload.')
  }

  return rehooked
}

async function requestLargestDisplay(app: App): Promise<void> {
  const modes = app.getHostContext()?.availableDisplayModes ?? []
  try {
    if (modes.includes('fullscreen')) {
      await app.requestDisplayMode({ mode: 'fullscreen' })
      return
    }

    if (modes.includes('pip')) {
      await app.requestDisplayMode({ mode: 'pip' })
      return
    }

  } catch {
    // The host owns final placement; the app remains usable inline when it declines.
  }
}

function readStructuredContent<T>(value: unknown): T | null {
  if (typeof value !== 'object' || value === null || !('structuredContent' in value)) {
    return null
  }

  const structured = value.structuredContent
  return typeof structured === 'object' && structured !== null ? structured as T : null
}

function readRequiredStructuredContent(value: unknown, error: string): Record<string, unknown> {
  const structured = readStructuredContent<Record<string, unknown>>(value)
  if (structured === null) throw new Error(error)
  return structured
}

function readSemanticWorkspace(value: unknown): SemanticWorkspace {
  if (!isRecord(value)
    || value.schemaVersion !== semanticWorkspaceSchemaVersion
    || typeof value.id !== 'string'
    || typeof value.name !== 'string'
    || typeof value.revision !== 'number'
    || typeof value.updatedAtUtc !== 'string'
    || !Array.isArray(value.diagrams)
    || !Array.isArray(value.entities)
    || !Array.isArray(value.relationships)
    || !Array.isArray(value.boards)
    || !Array.isArray(value.assets)) {
    throw new Error('CAVE returned an invalid semantic workspace.')
  }
  return value as unknown as SemanticWorkspace
}

function readSemanticOperationResult(value: unknown): SemanticWorkspaceOperationResult {
  if (!isRecord(value)
    || typeof value.succeeded !== 'boolean'
    || !Array.isArray(value.createdIds)
    || !value.createdIds.every((id) => typeof id === 'string')
    || !Array.isArray(value.errors)
    || !(value.transaction === null || isRecord(value.transaction))) {
    throw new Error('CAVE returned an invalid semantic workspace operation result.')
  }
  const workspace = readSemanticWorkspace(value.workspace)
  return { ...value, workspace } as unknown as SemanticWorkspaceOperationResult
}

function readSemanticExport(value: unknown): string {
  if (!isRecord(value) || typeof value.text !== 'string') {
    throw new Error('CAVE returned an invalid semantic workspace export.')
  }
  return value.text
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function readSamplingText(value: unknown): string {
  if (typeof value !== 'object' || value === null || !('content' in value)) {
    throw new Error('The Codex host returned an invalid memo response.')
  }
  const blocks = Array.isArray(value.content) ? value.content : [value.content]
  const text = blocks
    .filter((block): block is { type: 'text'; text: string } => (
      typeof block === 'object'
      && block !== null
      && 'type' in block
      && block.type === 'text'
      && 'text' in block
      && typeof block.text === 'string'
    ))
    .map((block) => block.text.trim())
    .filter(Boolean)
    .join('\n\n')
  if (text.length === 0) throw new Error('The Codex host returned no text for this memo.')
  return text
}

function readToolResult(value: unknown): CaveToolResult | null {
  if (!isRecord(value) || value.isError === true || !('structuredContent' in value)) {
    return null
  }

  const structured = value.structuredContent
  if (typeof structured !== 'object' || structured === null) {
    return null
  }

  const candidate = structured as Partial<CaveToolResult>
  return typeof candidate.subscriptionId === 'string'
    && candidate.subscriptionId.trim().length > 0
    && typeof candidate.workspaceRoot === 'string'
    && candidate.workspaceRoot.trim().length > 0
    && isLiveSnapshot(candidate.update)
    ? (candidate as CaveToolResult)
    : null
}

/** Checks required snapshot containers before publishing a structured MCP result to React. */
function isLiveSnapshot(value: unknown): value is LiveArchitectureSnapshot {
  if (!isRecord(value)
    || !Number.isSafeInteger(value.version) || (value.version as number) < 1
    || !isStringArray(value.changedPaths)
    || typeof value.activityChanged !== 'boolean' || typeof value.conversationChanged !== 'boolean'
    || typeof value.observedAtUtc !== 'string' || !isNullableString(value.refreshError)
    || !isRecord(value.snapshot)) return false
  const { metadata, graph, git, activity, conversation } = value.snapshot
  const baseline = isRecord(git) ? git.baseline : undefined
  return isRecord(metadata)
    && typeof metadata.workspaceName === 'string' && typeof metadata.providerId === 'string'
    && typeof metadata.sourceKind === 'string' && typeof metadata.isLive === 'boolean'
    && typeof metadata.generatedAtUtc === 'string'
    && isRecord(graph) && Array.isArray(graph.nodes) && Array.isArray(graph.relations)
    && graph.nodes.every((node) => isRecord(node)
      && typeof node.id === 'string' && typeof node.name === 'string' && typeof node.kind === 'string'
      && isNullableString(node.parentId) && isNullableString(node.qualifiedName)
      && isNullableString(node.description) && isNullableString(node.categoryId)
      && isStringArray(node.tags) && Array.isArray(node.sourceLocations)
      && node.sourceLocations.every((location: unknown) => isRecord(location)
        && typeof location.filePath === 'string' && Number.isSafeInteger(location.startLine) && Number.isSafeInteger(location.endLine)))
    && graph.relations.every((relation) => isRecord(relation)
      && ['id', 'sourceId', 'targetId', 'kind', 'confidence'].every((key) => typeof relation[key] === 'string')
      && Number.isFinite(relation.weight) && Number.isFinite(relation.evidenceCount))
    && isRecord(git) && typeof git.status === 'string' && isNullableString(git.error)
    && (baseline === null || (isRecord(baseline)
      && ['kind', 'reference', 'resolvedSha'].every((key) => typeof baseline[key] === 'string')))
    && (git.worktree === null || (isRecord(git.worktree)
      && typeof git.worktree.headSha === 'string' && isNullableString(git.worktree.branch)))
    && Array.isArray(git.files) && Array.isArray(git.nodes) && Array.isArray(git.unmappedFiles)
    && isRecord(activity) && Array.isArray(activity.agents) && Array.isArray(activity.nodes)
    && Array.isArray(activity.recentEdits) && isStringArray(activity.unmappedPaths) && isNullableString(activity.error)
    && (activity.latestInstruction === null || (isRecord(activity.latestInstruction)
      && typeof activity.latestInstruction.id === 'string' && typeof activity.latestInstruction.observedAtUtc === 'string'))
    && isRecord(conversation) && typeof conversation.sharingEnabled === 'boolean'
    && typeof conversation.status === 'string' && isNullableString(conversation.error)
    && Array.isArray(conversation.messages) && isRecord(conversation.control)
    && typeof conversation.control.state === 'string' && typeof conversation.control.canSend === 'boolean'
    && isNullableString(conversation.control.sessionId) && isNullableString(conversation.control.turnId)
    && isNullableString(conversation.control.error) && Array.isArray(conversation.control.deliveries)
}

function isNullableString(value: unknown): value is string | null {
  return value === null || typeof value === 'string'
}

function isStringArray(value: unknown): value is string[] {
  return Array.isArray(value) && value.every((item) => typeof item === 'string')
}

function toError(reason: unknown): Error {
  return reason instanceof Error ? reason : new Error('The live architecture feed failed.')
}
