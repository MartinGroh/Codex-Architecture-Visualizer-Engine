import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  connectArchitectureFeed,
  readAgentFlowLight,
  createBrowserHostBridge,
  createCodexHostBridge,
  rehookMcpFeed,
  type ArchitectureFeedConnectionState,
} from './api'
import type { CaveToolResult, LiveArchitectureSnapshot } from './types'
import type {
  SemanticWorkspace,
  SemanticWorkspaceBatch,
  SemanticWorkspaceOperationResult,
} from './semanticWorkspaceTypes'

const mcp = vi.hoisted(() => ({ create: vi.fn() }))
vi.mock('@modelcontextprotocol/ext-apps', () => ({
  App: vi.fn(function () { return mcp.create() }),
}))

const snapshot = (version: number): LiveArchitectureSnapshot => ({
  version,
  snapshot: {
    metadata: {
      workspaceName: 'Reconnect test',
      providerId: 'test',
      sourceKind: 'CodeGraph',
      isLive: true,
      generatedAtUtc: '2026-08-17T00:00:00Z',
    },
    graph: { nodes: [], relations: [] },
    git: {
      status: 'Unavailable',
      baseline: null,
      worktree: null,
      files: [],
      nodes: [],
      unmappedFiles: [],
      error: 'Not needed by this test.',
    },
    activity: {
      agents: [],
      nodes: [],
      recentEdits: [],
      latestInstruction: null,
      unmappedPaths: [],
      error: null,
    },
    conversation: {
      sharingEnabled: false,
      status: 'Disabled',
      messages: [],
      control: { sessionId: null, turnId: null, state: 'Unavailable', canSend: false, deliveries: [], error: null },
      error: null,
    },
  },
  changedPaths: [],
  activityChanged: false,
  conversationChanged: false,
  observedAtUtc: '2026-08-17T00:00:00Z',
  refreshError: null,
})

const semanticWorkspace = (): SemanticWorkspace => ({
  schemaVersion: 1,
  id: 'semantic-workspace-1',
  name: 'Semantic test',
  revision: 3,
  updatedAtUtc: '2026-08-30T12:00:00Z',
  diagrams: [],
  entities: [],
  relationships: [],
  boards: [],
  assets: [],
})

const semanticBatch: SemanticWorkspaceBatch = {
  actor: { type: 'Human', id: null, name: 'CAVE user' },
  summary: 'Create a diagram',
  operations: [{
    operation: 'create-diagram',
    diagram: {
      id: 'diagram-1',
      title: 'System design',
      purpose: null,
      parentDiagramId: null,
      tracking: null,
      view: { zoom: null, viewportX: null, viewportY: null, entities: [] },
      metadata: {},
    },
  }],
}

const semanticResult = (): SemanticWorkspaceOperationResult => ({
  succeeded: true,
  workspace: semanticWorkspace(),
  createdIds: ['diagram-1'],
  errors: [],
  transaction: {
    id: 'transaction-1',
    actor: semanticBatch.actor,
    occurredAtUtc: '2026-08-30T12:00:00Z',
    summary: semanticBatch.summary,
    affectedIds: ['diagram-1'],
    operationCount: 1,
  },
})

class FakeEventSource {
  static instances: FakeEventSource[] = []

  onopen: ((event: Event) => void) | null = null
  onerror: ((event: Event) => void) | null = null
  readonly close = vi.fn()
  readonly url: string

  constructor(url: string) {
    this.url = url
    FakeEventSource.instances.push(this)
  }

  addEventListener(_type: string, _listener: (event: MessageEvent<string>) => void) {}

  emitOpen() {
    this.onopen?.(new Event('open'))
  }

  emitError() {
    this.onerror?.(new Event('error'))
  }
}

const responseFor = (value: LiveArchitectureSnapshot) => new Response(
  JSON.stringify(value),
  { status: 200, headers: { 'Content-Type': 'application/json' } },
)

const flushPromises = async () => {
  await Promise.resolve()
  await Promise.resolve()
}

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  FakeEventSource.instances = []
  mcp.create.mockReset()
})

class FakeMcpApp {
  readonly listeners = new Map<string, Set<(params: never) => void>>()
  readonly connect = vi.fn(async () => {})
  readonly close = vi.fn(async () => {})
  readonly getHostContext = vi.fn(() => ({ theme: 'dark', availableDisplayModes: ['fullscreen'] }))
  readonly getHostCapabilities = vi.fn(() => ({}))
  readonly requestDisplayMode = vi.fn(async () => ({}))
  readonly callServerTool = vi.fn<(params: { name: string }, options?: { signal?: AbortSignal }) => Promise<unknown>>(
    () => new Promise(() => {}),
  )

  addEventListener(name: string, listener: (params: never) => void) {
    if (!this.listeners.has(name)) this.listeners.set(name, new Set())
    this.listeners.get(name)!.add(listener)
  }

  removeEventListener(name: string, listener: (params: never) => void) {
    this.listeners.get(name)?.delete(listener)
  }

  emit(name: string, params: unknown) {
    for (const listener of this.listeners.get(name) ?? []) listener(params as never)
  }
}

describe('embedded CAVE startup', () => {
  const initial: CaveToolResult = {
    subscriptionId: 'initial-subscription', workspaceRoot: 'C:\\Code\\Example', update: snapshot(1),
  }
  const start = (app: FakeMcpApp, signal?: AbortSignal) => {
    vi.useFakeTimers()
    vi.stubGlobal('parent', {})
    mcp.create.mockReturnValue(app)
    const updates = vi.fn()
    const states = vi.fn()
    const connected = connectArchitectureFeed(updates, vi.fn(), states, undefined, vi.fn(), signal)
    return { connected, updates, states }
  }

  it('accepts a result delivered during the handshake and does not wait for display placement', async () => {
    const app = new FakeMcpApp()
    app.connect.mockImplementation(async () => { app.emit('toolresult', { structuredContent: initial }) })
    app.requestDisplayMode.mockImplementation(() => new Promise(() => {}))
    const { connected, updates } = start(app)
    const stop = await connected
    expect(updates).toHaveBeenCalledExactlyOnceWith(initial.update)
    expect(app.callServerTool).toHaveBeenCalledExactlyOnceWith({
      name: 'cave_poll_architecture', arguments: { subscriptionId: initial.subscriptionId, afterVersion: 1 },
    })
    expect(vi.getTimerCount()).toBe(0)
    stop()
    stop()
    expect(app.close).toHaveBeenCalledOnce()
  })

  it('reattaches once to the complete tool-input workspace when the one-shot result is lost', async () => {
    const app = new FakeMcpApp()
    app.callServerTool.mockResolvedValueOnce({ structuredContent: initial })
    const { connected, updates, states } = start(app)
    app.emit('toolinput', { arguments: { workspacePath: initial.workspaceRoot } })
    await vi.advanceTimersByTimeAsync(2_000)
    const stop = await connected
    expect(app.callServerTool).toHaveBeenNthCalledWith(1, {
      name: 'cave_show_architecture', arguments: { workspacePath: initial.workspaceRoot },
    }, { signal: expect.any(AbortSignal) })
    expect(updates).toHaveBeenCalledExactlyOnceWith(initial.update)
    expect(states.mock.calls.map(([state]) => state.status)).toEqual(['Connecting', 'Reconnecting', 'Connected'])
    expect(vi.getTimerCount()).toBe(0)
    stop()
  })

  it('also recovers if complete tool input arrives after the recovery grace period', async () => {
    const app = new FakeMcpApp()
    app.callServerTool.mockResolvedValueOnce({ structuredContent: initial })
    const { connected } = start(app)
    await vi.advanceTimersByTimeAsync(3_000)
    expect(app.callServerTool).not.toHaveBeenCalled()
    app.emit('toolinputpartial', { arguments: { workspacePath: 'C:\\Partial' } })
    expect(app.callServerTool).not.toHaveBeenCalled()
    app.emit('toolinput', { arguments: { workspacePath: initial.workspaceRoot } })
    const stop = await connected
    expect(app.callServerTool.mock.calls[0][0]).toEqual({
      name: 'cave_show_architecture', arguments: { workspacePath: initial.workspaceRoot },
    })
    stop()
  })

  it('fails with no guessed workspace and releases listeners when neither payload arrives', async () => {
    const app = new FakeMcpApp()
    const { connected, states, updates } = start(app)
    const failure = expect(connected).rejects.toThrow('initial graph or workspace')
    app.emit('toolinput', { arguments: { workspacePath: '../another-project' } })
    await vi.advanceTimersByTimeAsync(30_000)
    await failure
    expect(app.callServerTool).not.toHaveBeenCalled()
    expect(updates).not.toHaveBeenCalled()
    expect(states).toHaveBeenLastCalledWith({ status: 'Failed', attempt: 0, retryInMs: null })
    expect([...app.listeners.values()].every((listeners) => listeners.size === 0)).toBe(true)
    expect(app.close).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('bounds an unanswered handshake and ignores its late completion', async () => {
    const app = new FakeMcpApp()
    let complete!: () => void
    app.connect.mockImplementation(() => new Promise<void>((resolve) => { complete = resolve }))
    const { connected, updates } = start(app)
    const failure = expect(connected).rejects.toThrow('initial graph or workspace')
    await vi.advanceTimersByTimeAsync(30_000)
    await failure
    complete()
    await flushPromises()
    expect(updates).not.toHaveBeenCalled()
    expect(app.requestDisplayMode).not.toHaveBeenCalled()
    expect(vi.getTimerCount()).toBe(0)
  })

  it.each([
    [{ isError: true, structuredContent: initial }, 'could not create'],
    [{ structuredContent: { ...initial, update: null } }, 'invalid CAVE graph'],
    [{ structuredContent: { ...initial, update: { version: 1, snapshot: { metadata: {}, graph: { nodes: [], relations: [] } } } } }, 'invalid CAVE graph'],
    [{ structuredContent: { ...initial, update: { ...snapshot(1), changedPaths: null } } }, 'invalid CAVE graph'],
    [{ structuredContent: { ...initial, update: { ...snapshot(1), snapshot: { ...snapshot(1).snapshot, activity: {} } } } }, 'invalid CAVE graph'],
  ])('reports a failed or malformed initial result immediately', async (payload, message) => {
    const app = new FakeMcpApp()
    const { connected, updates } = start(app)
    const failure = expect(connected).rejects.toThrow(message as string)
    app.emit('toolresult', payload)
    await failure
    expect(updates).not.toHaveBeenCalled()
    expect(app.close).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('bounds a stalled recovery, aborts its request, and does not retry endlessly', async () => {
    const app = new FakeMcpApp()
    const { connected } = start(app)
    const failure = expect(connected).rejects.toThrow('within 30 seconds')
    app.emit('toolinput', { arguments: { workspacePath: initial.workspaceRoot } })
    await vi.advanceTimersByTimeAsync(30_000)
    await failure
    expect(app.callServerTool).toHaveBeenCalledOnce()
    expect(app.callServerTool.mock.calls[0][1]?.signal?.aborted).toBe(true)
    expect(app.close).toHaveBeenCalledOnce()
  })

  it('accepts the original result if it arrives during recovery, without publishing a late replacement', async () => {
    const app = new FakeMcpApp()
    let completeRecovery!: (result: unknown) => void
    app.callServerTool.mockImplementationOnce(() => new Promise((resolve) => { completeRecovery = resolve }))
    const { connected, updates } = start(app)
    app.emit('toolinput', { arguments: { workspacePath: initial.workspaceRoot } })
    await vi.advanceTimersByTimeAsync(2_000)
    app.emit('toolresult', { structuredContent: initial })
    const stop = await connected
    completeRecovery({ structuredContent: { ...initial, update: snapshot(9) } })
    await flushPromises()
    expect(updates).toHaveBeenCalledExactlyOnceWith(initial.update)
    expect(app.callServerTool.mock.calls[0][1]?.signal?.aborted).toBe(true)
    stop()
  })

  it('cancels startup on unmount and leaves no delayed recovery or listeners', async () => {
    const app = new FakeMcpApp()
    const controller = new AbortController()
    const { connected } = start(app, controller.signal)
    const failure = expect(connected).rejects.toMatchObject({ name: 'AbortError' })
    controller.abort()
    await failure
    expect(app.callServerTool).not.toHaveBeenCalled()
    expect(app.close).toHaveBeenCalledOnce()
    expect([...app.listeners.values()].every((listeners) => listeners.size === 0)).toBe(true)
    expect(vi.getTimerCount()).toBe(0)
  })
})

describe('Agent Flow Light transport', () => {
  it('reads only the selected workspace with no caching and rejects a mismatched response', async () => {
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue({
      ok: true,
      json: async () => ({ schemaVersion: 1, workspaceId: 'workspace one', agents: [], mainGoal: { status: 'Private', goal: null } }),
    } as Response)
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    await expect(readAgentFlowLight('workspace one', controller.signal)).resolves.toMatchObject({ workspaceId: 'workspace one' })
    expect(fetchMock).toHaveBeenCalledWith('/api/agent-flow?workspace=workspace%20one', {
      headers: { Accept: 'application/json' }, cache: 'no-store', signal: controller.signal,
    })

    fetchMock.mockResolvedValue({
      ok: true,
      json: async () => ({ schemaVersion: 1, workspaceId: 'another', agents: [], mainGoal: { status: 'Ready', goal: null } }),
    } as Response)
    await expect(readAgentFlowLight('workspace one')).rejects.toThrow('incompatible')
  })
})

describe('connectArchitectureFeed browser recovery', () => {
  it('recovers when the host is unavailable during initial connection', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn<typeof fetch>()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(responseFor(snapshot(2)))
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('EventSource', FakeEventSource as unknown as typeof EventSource)
    const updates: LiveArchitectureSnapshot[] = []
    const states: ArchitectureFeedConnectionState[] = []

    const disconnect = await connectArchitectureFeed(
      (update) => updates.push(update),
      vi.fn(),
      (state) => states.push(state),
      'workspace-test',
    )
    await flushPromises()

    expect(states.at(-1)).toEqual({ status: 'Reconnecting', attempt: 1, retryInMs: 500 })
    await vi.advanceTimersByTimeAsync(500)
    await flushPromises()
    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(fetchMock).toHaveBeenLastCalledWith(
      '/api/snapshot?workspace=workspace-test',
      expect.any(Object),
    )
    expect(updates.map((update) => update.version)).toEqual([2])

    FakeEventSource.instances[0].emitOpen()
    expect(FakeEventSource.instances[0].url).toBe('/events?workspace=workspace-test')
    expect(states.at(-1)).toEqual({ status: 'Connected', attempt: 0, retryInMs: null })
    disconnect()
  })

  it('refetches the canonical snapshot after an established stream disconnects', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(responseFor(snapshot(3)))
      .mockResolvedValueOnce(responseFor(snapshot(4)))
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('EventSource', FakeEventSource as unknown as typeof EventSource)
    const updates: LiveArchitectureSnapshot[] = []
    const states: ArchitectureFeedConnectionState[] = []

    const disconnect = await connectArchitectureFeed(
      (update) => updates.push(update),
      vi.fn(),
      (state) => states.push(state),
      'workspace-test',
    )
    await flushPromises()
    FakeEventSource.instances[0].emitOpen()
    FakeEventSource.instances[0].emitError()

    expect(FakeEventSource.instances[0].close).toHaveBeenCalledOnce()
    expect(states.at(-1)).toEqual({ status: 'Reconnecting', attempt: 1, retryInMs: 500 })
    await vi.advanceTimersByTimeAsync(500)
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledTimes(2)
    expect(updates.map((update) => update.version)).toEqual([3, 4])
    FakeEventSource.instances[1].emitOpen()
    expect(states.at(-1)).toEqual({ status: 'Connected', attempt: 0, retryInMs: null })
    disconnect()
  })

  it('cancels a scheduled reconnect when the client disconnects', async () => {
    vi.useFakeTimers()
    const fetchMock = vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch'))
    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('EventSource', FakeEventSource as unknown as typeof EventSource)

    const disconnect = await connectArchitectureFeed(
      vi.fn(),
      vi.fn(),
      vi.fn(),
      'workspace-test',
    )
    await flushPromises()
    disconnect()
    await vi.advanceTimersByTimeAsync(5_000)

    expect(fetchMock).toHaveBeenCalledOnce()
  })
})

describe('embedded CAVE subscription recovery', () => {
  it('reopens the same workspace instead of retrying a dead subscription forever', async () => {
    const current: CaveToolResult = {
      subscriptionId: 'expired-subscription',
      workspaceRoot: 'C:\\Code\\Example',
      update: snapshot(42),
    }
    const recovered: CaveToolResult = {
      subscriptionId: 'replacement-subscription',
      workspaceRoot: current.workspaceRoot,
      update: snapshot(1),
    }
    const app = {
      callServerTool: vi.fn().mockResolvedValue({ structuredContent: recovered }),
    }

    const result = await rehookMcpFeed(app as never, current)

    expect(app.callServerTool).toHaveBeenCalledWith({
      name: 'cave_show_architecture',
      arguments: { workspacePath: current.workspaceRoot },
    })
    expect(result).toEqual(recovered)
  })
})

describe('trusted browser Codex bridge', () => {
  it('queues chat, runs an ephemeral node memo, and controls conversation sharing', async () => {
    const receipt = {
      messageId: 'message-1', sessionId: 'session-123', turnId: null, state: 'Queued',
      queuedAtUtc: '2026-08-23T12:00:00Z', updatedAtUtc: '2026-08-23T12:00:00Z', error: null,
    }
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(new Response(JSON.stringify(receipt), { status: 202 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ text: 'Isolated answer.' }), { status: 200 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    const bridge = createBrowserHostBridge('workspace with spaces')

    await expect(bridge.sendMessage('Continue remotely', 'session-123')).resolves.toEqual(receipt)
    await expect(bridge.sampleMemo('Explain the node', 'session-123')).resolves.toBe('Isolated answer.')
    await bridge.setConversationSharing(true)

    expect(bridge.canSample).toBe(true)

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/api/conversation/messages?workspace=workspace%20with%20spaces',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ expectedSessionId: 'session-123', text: 'Continue remotely' }),
      }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/conversation/node-memos?workspace=workspace%20with%20spaces',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ expectedSessionId: 'session-123', text: 'Explain the node' }),
      }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      '/api/conversation/sharing?workspace=workspace%20with%20spaces',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ enabled: true }) }),
    )
  })

  it('refuses to guess a task identity', async () => {
    const fetchMock = vi.fn<typeof fetch>()
    vi.stubGlobal('fetch', fetchMock)
    const bridge = createBrowserHostBridge('workspace-test')

    await expect(bridge.sendMessage('Do not guess')).rejects.toThrow('exact Codex task')
    await expect(bridge.sampleMemo('Do not guess')).rejects.toThrow('exact Codex task')
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('uses the canonical workspace-scoped semantic HTTP contract', async () => {
    const workspace = semanticWorkspace()
    const result = semanticResult()
    const fetchMock = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(new Response(JSON.stringify(workspace), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(result), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(result), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify(result), { status: 200 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ text: '# Semantic test' }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const bridge = createBrowserHostBridge('workspace with spaces')

    await expect(bridge.readSemanticWorkspace()).resolves.toEqual(workspace)
    await expect(bridge.applySemanticBatch(semanticBatch)).resolves.toEqual(result)
    await expect(bridge.undoSemanticWorkspace()).resolves.toEqual(result)
    await expect(bridge.redoSemanticWorkspace()).resolves.toEqual(result)
    await expect(bridge.exportSemanticWorkspace('markdown', false)).resolves.toBe('# Semantic test')

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      '/api/semantic-workspace?workspace=workspace%20with%20spaces',
      { headers: { Accept: 'application/json' } },
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      '/api/semantic-workspace/operations?workspace=workspace%20with%20spaces',
      expect.objectContaining({ method: 'POST', body: JSON.stringify(semanticBatch) }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      '/api/semantic-workspace/undo?workspace=workspace%20with%20spaces',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      4,
      '/api/semantic-workspace/redo?workspace=workspace%20with%20spaces',
      expect.objectContaining({ method: 'POST' }),
    )
    expect(fetchMock).toHaveBeenNthCalledWith(
      5,
      '/api/semantic-workspace/export?workspace=workspace+with+spaces&format=markdown&includePresentation=false',
      { headers: { Accept: 'application/json' } },
    )
  })
})

describe('embedded Codex host bridge', () => {
  it('sends chat messages and runs node memos through advertised MCP Apps capabilities', async () => {
    const initial: CaveToolResult = {
      subscriptionId: 'active-subscription',
      workspaceRoot: 'C:\\Code\\Example',
      update: snapshot(7),
    }
    const app = {
      getHostCapabilities: vi.fn().mockReturnValue({ message: {}, sampling: {} }),
      sendMessage: vi.fn().mockResolvedValue({ isError: false }),
      createSamplingMessage: vi.fn().mockResolvedValue({
        role: 'assistant',
        content: { type: 'text', text: 'A scoped architecture memo.' },
      }),
      callServerTool: vi.fn(),
    }
    const bridge = createCodexHostBridge(app as never, initial, vi.fn())

    expect(bridge.canSendMessage).toBe(true)
    expect(bridge.canSample).toBe(true)
    await bridge.sendMessage('Continue with this boundary')
    await expect(bridge.sampleMemo('Explain the boundary')).resolves.toBe('A scoped architecture memo.')

    expect(app.sendMessage).toHaveBeenCalledWith({
      role: 'user',
      content: [{ type: 'text', text: 'Continue with this boundary' }],
    })
    expect(app.createSamplingMessage).toHaveBeenCalledWith({
      messages: [{ role: 'user', content: { type: 'text', text: 'Explain the boundary' } }],
      maxTokens: 1_200,
      systemPrompt: 'Answer as a concise architecture memo. Use only the supplied CAVE evidence and clearly identify uncertainty.',
    })
  })

  it('fails closed when the embedded host does not advertise chat capabilities', async () => {
    const app = {
      getHostCapabilities: vi.fn().mockReturnValue({}),
      sendMessage: vi.fn(),
      createSamplingMessage: vi.fn(),
      callServerTool: vi.fn(),
    }
    const bridge = createCodexHostBridge(app as never, {
      subscriptionId: 'active-subscription',
      workspaceRoot: 'C:\\Code\\Example',
      update: snapshot(7),
    }, vi.fn())

    expect(bridge.canSendMessage).toBe(false)
    expect(bridge.canSample).toBe(false)
    await expect(bridge.sendMessage('Do not send')).rejects.toThrow('does not support messages')
    await expect(bridge.sampleMemo('Do not sample')).rejects.toThrow('does not support isolated model sampling')
    expect(app.sendMessage).not.toHaveBeenCalled()
    expect(app.createSamplingMessage).not.toHaveBeenCalled()
  })

  it('uses the canonical semantic MCP tools for the exact initial workspace', async () => {
    const workspace = semanticWorkspace()
    const result = semanticResult()
    const app = {
      getHostCapabilities: vi.fn().mockReturnValue({}),
      sendMessage: vi.fn(),
      createSamplingMessage: vi.fn(),
      callServerTool: vi.fn().mockImplementation(({ name }: { name: string }) => {
        if (name === 'cave_get_semantic_workspace') return { structuredContent: workspace }
        if (name === 'cave_export_semantic_workspace') return { structuredContent: { text: '{"format":"cave-semantic-workspace"}' } }
        return { structuredContent: result }
      }),
    }
    const initial: CaveToolResult = {
      subscriptionId: 'active-subscription',
      workspaceRoot: 'C:\\Code\\Exact Workspace',
      update: snapshot(7),
    }
    const bridge = createCodexHostBridge(app as never, initial, vi.fn())

    await expect(bridge.readSemanticWorkspace()).resolves.toEqual(workspace)
    await expect(bridge.applySemanticBatch(semanticBatch)).resolves.toEqual(result)
    await expect(bridge.undoSemanticWorkspace()).resolves.toEqual(result)
    await expect(bridge.redoSemanticWorkspace()).resolves.toEqual(result)
    await expect(bridge.exportSemanticWorkspace('json', true)).resolves.toBe('{"format":"cave-semantic-workspace"}')

    expect(app.callServerTool).toHaveBeenNthCalledWith(1, {
      name: 'cave_get_semantic_workspace',
      arguments: { workspacePath: initial.workspaceRoot },
    })
    expect(app.callServerTool).toHaveBeenNthCalledWith(2, {
      name: 'cave_apply_semantic_operations',
      arguments: { workspacePath: initial.workspaceRoot, batch: semanticBatch },
    })
    expect(app.callServerTool).toHaveBeenNthCalledWith(3, {
      name: 'cave_undo_semantic_workspace',
      arguments: { workspacePath: initial.workspaceRoot },
    })
    expect(app.callServerTool).toHaveBeenNthCalledWith(4, {
      name: 'cave_redo_semantic_workspace',
      arguments: { workspacePath: initial.workspaceRoot },
    })
    expect(app.callServerTool).toHaveBeenNthCalledWith(5, {
      name: 'cave_export_semantic_workspace',
      arguments: { workspacePath: initial.workspaceRoot, format: 'json', includePresentation: true },
    })
  })

  it('rejects malformed semantic structured content instead of casting it', async () => {
    const app = {
      getHostCapabilities: vi.fn().mockReturnValue({}),
      sendMessage: vi.fn(),
      createSamplingMessage: vi.fn(),
      callServerTool: vi.fn().mockResolvedValue({ structuredContent: { schemaVersion: 1 } }),
    }
    const bridge = createCodexHostBridge(app as never, {
      subscriptionId: 'active-subscription',
      workspaceRoot: 'C:\\Code\\Example',
      update: snapshot(7),
    }, vi.fn())

    await expect(bridge.readSemanticWorkspace()).rejects.toThrow('invalid semantic workspace')
  })
})
