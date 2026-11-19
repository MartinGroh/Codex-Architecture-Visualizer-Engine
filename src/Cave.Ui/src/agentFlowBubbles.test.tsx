import { cleanup, renderHook } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import type { AgentChatAgent, AgentChatMessage, AgentChatSession } from './agentChat'
import * as markdown from './agentChatOutputIndex'
import { useAgentFlowBubbles } from './agentFlowBubbles'
import type { MachineActivityLane } from './machineActivityTimeline'

const time = '2026-11-19T09:00:00.000Z'

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

function lane(agentId: string, publicId?: string, parentAgentId?: string): MachineActivityLane {
  return {
    agent: { agentId, publicId, parentAgentId, displayName: 'Alex', agentType: 'agent', isSubagent: !!parentAgentId,
      startedAtUtc: time, updatedAtUtc: time, state: 'Active', phase: 'Editing', summary: null,
      hasObservedActivity: true, hasDeclaredScope: false },
    active: true, durationMs: 100, durationPercent: 100, milestones: [], completionSummary: null,
  }
}

function message(id: string, text: string, minute: number, fields?: Partial<AgentChatMessage>): AgentChatMessage {
  return { id, text, occurredAtUtc: new Date(Date.parse(time) + minute * 60_000).toISOString(),
    role: 'Assistant', kind: 'Commentary', artifacts: [], isTruncated: false, ...fields }
}

function chatAgent(id: string, messages: AgentChatMessage[], parentId: string | null = null): AgentChatAgent {
  return { id, parentId, displayName: 'Alex', startedAtUtc: time, status: 'Available', messages }
}

function chatSession(agents: AgentChatAgent[], fields?: Partial<AgentChatSession>): AgentChatSession {
  return { agents, sharing: 'Shared', periodId: 'period-1', status: 'Available', artifactUrl: (id) => id,
    enableSharing: async () => {}, ...fields }
}

it('shows declared and observed work independently of sharing, with one distinct mapped milestone', () => {
  const root = lane('native-root', 'public-root')
  root.agent.summary = 'Fix the timeline resize'
  root.agent.summaryEvidence = 'Declared'
  root.agent.lastObservedActivity = 'Using apply_patch: src/Flow.tsx'
  root.milestones = [
    { id: 'edit:other', label: 'Inspector.tsx', detail: 'Observed edit', kind: 'Edit', observedAtUtc: time, positionPercent: 70 },
    { id: 'edit:current', label: 'Flow.tsx', detail: 'Observed edit', kind: 'Edit', observedAtUtc: time, positionPercent: 80 },
    { id: 'phase', label: 'Editing', detail: 'Current observed phase', kind: 'Phase', observedAtUtc: time, positionPercent: 100 },
  ]
  const child = lane('native-child', 'public-child', 'native-root')
  child.agent.summary = 'Pinned only to the canonical feed'
  child.completionSummary = { text: 'Unscoped final text', occurredAtUtc: time, isTruncated: false }
  const session = chatSession([chatAgent('public-root', [message('private', 'Private text', 1)])], { sharing: 'Private' })
  const { result } = renderHook(() => useAgentFlowBubbles([root, child], session, 'public-root'))

  expect(result.current.map(({ kind, label, text, parent }) => ({ kind, label, text, parent }))).toEqual([
    { kind: 'task', label: 'Declared focus', text: 'Fix the timeline resize', parent: 'native-root' },
    { kind: 'activity', label: 'Observed action', text: 'Using apply_patch: src/Flow.tsx', parent: 'native-root' },
    { kind: 'activity', label: 'Observed edit', text: 'Inspector.tsx', parent: 'native-root' },
  ])
})

it('shows the exact task and latest meaningful text, image and diagram as separate bubbles', () => {
  const root = lane('native-root', 'public-root')
  const child = lane('native-child', 'public-child', 'native-root')
  const other = lane('native-other', 'public-other')
  const missing = lane('native-missing', 'public-missing', 'native-root')
  const nativeOnly = lane('public-child', undefined, 'native-root')
  const diagram = 'flowchart LR\n  A --> B'
  const raster = { id: 'published-image', contentType: 'image/png' }
  const session = chatSession([
    chatAgent('public-root', [
      message('ask', 'Fix the **input** layout', 1, { role: 'User', kind: 'Prompt' }),
      message('working', 'Trying the layout.', 2),
      message('photo', '![external photo](https://example.invalid/pic.png)', 6, { artifacts: [raster] }),
      message('file-only', '', 7, { artifacts: [{ id: 'unavailable', contentType: null }] }),
      message('diagram', '~~~mermaid\n' + diagram + '\n~~~', 5),
      message('reply', '**Fixed** the layout. See [the tests](/tests).\n\n![diagram alt](https://example.invalid/pic.png)', 4,
        { kind: 'Final' }),
    ]),
    chatAgent('public-child', [message('child', 'Verified cleanly.', 7, { kind: 'Final' })], 'public-root'),
    chatAgent('public-other', [message('other', 'A different task', 8)], 'public-root'),
    chatAgent('public-missing', [message('missing', 'An unverified branch', 8)], 'unrelated'),
  ])
  const { result } = renderHook(() => useAgentFlowBubbles([root, child, other, missing, nativeOnly], session, 'public-root'))

  expect(result.current.map(({ id: _id, ...bubble }) => bubble)).toEqual([
    { parent: 'native-root', kind: 'task', label: 'Task prompt', text: 'Fix the input layout', messageId: 'ask' },
    { parent: 'native-root', kind: 'text', label: 'Final reply', text: 'Fixed the layout. See the tests.', messageId: 'reply' },
    { parent: 'native-root', kind: 'image', label: 'Image', text: '', messageId: 'photo', artifact: raster },
    { parent: 'native-root', kind: 'diagram', label: 'Diagram', text: '', messageId: 'diagram', source: diagram },
    { parent: 'native-child', kind: 'text', label: 'Final reply', text: 'Verified cleanly.', messageId: 'child' },
  ])
})

it('reuses parsed Markdown for unchanged polls and invalidates every published bubble with its period', () => {
  const root = lane('native-root', 'public-root')
  const published = message('reply', 'Published reply', 2, { kind: 'Final' })
  const read = vi.spyOn(markdown, 'readAgentChatMarkdown')
  const initialProps = chatSession([chatAgent('public-root', [published])])
  const { result, rerender } = renderHook((chat: AgentChatSession) => useAgentFlowBubbles([root], chat, 'public-root'), { initialProps })
  const [initial] = result.current
  const wrapper = document.createElement('div')
  const node = wrapper.appendChild(document.createElement('div'))
  node.setAttribute('data-id', initial.id)
  expect(wrapper.querySelector(`[data-id="${initial.id}"]`)).toBe(node)
  expect(read).toHaveBeenCalledTimes(1)
  const next = chatSession([chatAgent('public-root', [{ ...published }])])
  rerender(next)
  expect(result.current[0].id).toBe(initial.id)
  expect(read).toHaveBeenCalledTimes(1)
  rerender(chatSession([chatAgent('public-root', [published, message('next-reply', '~~~text\nBuild passed.\n4 files checked.\n~~~', 3, { kind: 'Final' })])]))
  expect(result.current[0]).toMatchObject({ id: initial.id, messageId: 'next-reply', text: 'Build passed. 4 files checked.' })

  rerender({ ...next, periodId: 'period-2' })
  expect(result.current[0].id).not.toBe(initial.id)
  rerender({ ...next, periodId: null })
  expect(result.current).toEqual([])
  rerender({ ...next, sharing: 'Private' })
  expect(result.current).toEqual([])
})

it('shows captured legacy final text even when native history is unavailable', () => {
  const root = lane('native-root', 'public-root')
  const published = message('reply', 'A captured final reply', 2, { kind: 'Final' })
  const legacyRoot = { ...chatAgent('public-root', [published]), status: 'Unavailable' as const }
  const session = chatSession([legacyRoot], { sharing: 'Legacy', periodId: null, status: 'Unavailable' })
  const { result } = renderHook(() => useAgentFlowBubbles([root], session, 'public-root'))

  expect(result.current).toMatchObject([{ parent: 'native-root', kind: 'text', text: 'A captured final reply', messageId: 'reply' }])
})
