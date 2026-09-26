import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentActivitySpotlight } from './AgentActivitySpotlight'
import type { AgentActivity } from './types'

const mainAgent: AgentActivity = {
  agentId: 'main',
  agentType: 'Codex',
  isSubagent: false,
  state: 'Active',
  phase: 'Validating',
  summary: 'Checking the promoted status panel',
  hasObservedActivity: true,
  hasDeclaredScope: true,
  startedAtUtc: '2026-08-18T10:00:00Z',
  updatedAtUtc: '2026-08-18T10:01:00Z',
}

afterEach(cleanup)

describe('AgentActivitySpotlight', () => {
  it('promotes the active phase and all live activity metrics', () => {
    render(
      <AgentActivitySpotlight
        agents={[mainAgent]}
        activeNodeCount={2}
        recentEditCount={3}
        unmappedCount={4}
        isSelected
        onSelect={() => undefined}
      />,
    )

    const spotlight = screen.getByRole('button', { name: /Open live agent activity/i })
    expect(spotlight.getAttribute('aria-pressed')).toBe('true')
    expect(spotlight.getAttribute('aria-label')).toContain('1 active agent')
    expect(spotlight.getAttribute('aria-label')).toContain('Main agent Validating: Checking the promoted status panel')
    expect(spotlight.getAttribute('aria-label')).toContain('2 active nodes, 3 recent edits, 4 unmapped paths')
    expect(screen.getByText('Main agent')).toBeTruthy()
    expect(screen.getByText('Validating')).toBeTruthy()
    expect(screen.getByText('Checking the promoted status panel')).toBeTruthy()
    expect(spotlight.querySelector('.agent-avatar--validating')).toBeTruthy()
    expect(spotlight.querySelector('[data-agent-avatar-accessory="check"]')).toBeTruthy()
    expect(spotlight.querySelectorAll('.agent-avatar')).toHaveLength(1)
  })

  it('uses the large avatar for the primary agent and keeps icons for additional agents', () => {
    const subagent: AgentActivity = {
      ...mainAgent,
      agentId: 'subagent-1',
      agentType: 'Explorer',
      isSubagent: true,
      phase: 'Reading',
    }
    const { container } = render(
      <AgentActivitySpotlight
        agents={[mainAgent, subagent]}
        activeNodeCount={2}
        recentEditCount={0}
        unmappedCount={0}
        isSelected
        onSelect={() => undefined}
      />,
    )

    expect(container.querySelector('.agent-activity-spotlight__agent.is-primary .agent-avatar')).toBeNull()
    expect(container.querySelector('.agent-activity-spotlight__agent.is-subagent .agent-avatar--reading')).toBeTruthy()
    expect(container.querySelectorAll('.agent-avatar')).toHaveLength(2)
    expect(container.querySelector('.agent-activity-spotlight__topbar-more')?.textContent).toBe('+1')
  })

  it('opens live activity and presents a clear idle state', () => {
    const onSelect = vi.fn()
    render(
      <AgentActivitySpotlight
        agents={[]}
        activeNodeCount={0}
        recentEditCount={0}
        unmappedCount={0}
        isSelected={false}
        onSelect={onSelect}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /No active agents/i }))
    expect(onSelect).toHaveBeenCalledOnce()
    expect(screen.getByText('No active agents')).toBeTruthy()
    expect(document.querySelector('.agent-avatar--idle')).toBeTruthy()
  })

  it('keeps a narrative visible in the prominent mobile presentation while idle', () => {
    const { container } = render(
      <AgentActivitySpotlight
        agents={[]}
        activeNodeCount={0}
        recentEditCount={0}
        unmappedCount={0}
        isSelected
        prominent
        onSelect={() => undefined}
      />,
    )

    expect(container.querySelector('.agent-activity-spotlight.is-prominent')).toBeTruthy()
    expect(screen.getByText('Waiting for the next mapped Codex action.')).toBeTruthy()
    expect(container.querySelector('.agent-avatar')?.getAttribute('style')).toContain('48px')
  })
})
