import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { AgentWall } from './AgentWall'
import type { MachineActivityLane } from './machineActivityTimeline'

afterEach(cleanup)

function lane(id: string, name: string, startedAtUtc: string, active = true): MachineActivityLane {
  return {
    agent: {
      agentId: 'native-' + id, publicId: id, displayName: name, agentType: id === 'task' ? 'main' : 'worker',
      state: active ? 'Active' : 'Completed', phase: 'Working', isSubagent: id !== 'task', summary: null,
      hasObservedActivity: true, hasDeclaredScope: false, startedAtUtc, updatedAtUtc: startedAtUtc,
    },
    active, durationMs: 0, durationPercent: 0, milestones: [], completionSummary: null,
  }
}

it('places the parent first and keeps pane identity when an older child is newly observed or another finishes', () => {
  const root = lane('task', 'Main', '2026-11-01T09:00:00Z')
  const alpha = lane('alpha', 'Rowan', '2026-11-01T09:02:00Z')
  const beta = lane('beta', 'Kai', '2026-11-01T09:01:00Z')
  const props = { workspaceId: 'workspace', taskId: 'task', sharingEnabled: false, onBack: vi.fn() }
  const { rerender } = render(<AgentWall {...props} lanes={[alpha, root]} />)
  const headings = () => screen.getAllByRole('heading', { level: 3 }).map((heading) => heading.textContent)
  expect(headings()).toEqual(['Main', 'Rowan'])
  const alphaFeed = screen.getByRole('region', { name: 'Rowan conversation and activity' })

  rerender(<AgentWall {...props} lanes={[beta, { ...alpha, active: false, agent: { ...alpha.agent, state: 'Completed' } }, root]} />)
  expect(headings()).toEqual(['Main', 'Rowan', 'Kai'])
  expect(screen.getByRole('region', { name: 'Rowan conversation and activity' })).toBe(alphaFeed)
  expect(within(screen.getByRole('article', { name: 'Rowan · worker' })).getByText('worker · Completed')).toBeTruthy()

  fireEvent.click(screen.getByRole('button', { name: 'Expand Kai · worker' }))
  expect(headings()).toEqual(['Kai'])
  fireEvent.click(screen.getByRole('button', { name: 'Restore wall from Kai · worker' }))
  expect(headings()).toEqual(['Main', 'Rowan', 'Kai'])
  expect(screen.getByRole('region', { name: 'Rowan conversation and activity' })).toBe(alphaFeed)
})

it('distinguishes repeated names by observed role and keeps identical roles addressable', () => {
  const root = lane('task', 'Main', '2026-11-01T09:00:00Z')
  const reader = lane('reader', 'Kai', '2026-11-01T09:01:00Z')
  reader.agent.agentType = 'Review agent'
  const builder = lane('builder', 'Kai', '2026-11-01T09:02:00Z')
  const secondBuilder = lane('second-builder', 'Kai', '2026-11-01T09:03:00Z')
  render(<AgentWall workspaceId="workspace" taskId="task" lanes={[root, reader, builder, secondBuilder]} sharingEnabled={false} onBack={vi.fn()} />)
  expect(screen.getByRole('option', {name: 'Kai · Review agent'})).toBeTruthy()
  expect(screen.getByRole('button', {name: 'Expand Kai · worker · 3'})).toBeTruthy()
  expect(within(screen.getByRole('article', {name: 'Kai · worker · 4'})).getByRole('heading').textContent).toBe('Kai · 4')
})
