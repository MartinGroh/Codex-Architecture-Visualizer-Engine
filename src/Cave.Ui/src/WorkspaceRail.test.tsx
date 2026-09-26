import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { WorkspaceRail } from './WorkspaceRail'
import type { WorkspaceOverview } from './types'

const activeWorkspace: WorkspaceOverview = {
  workspaceId: 'cave',
  name: 'CAVE',
  rootPath: 'C:\\Code\\CAVE',
  isAvailable: true,
  lastSeenAtUtc: '2026-08-18T10:00:00Z',
  knownAgentCount: 1,
  activeAgentCount: 1,
  activeSubagentCount: 0,
  currentPhase: 'Editing',
  activitySummary: 'Polishing the workspace rail',
  lastActivityAtUtc: '2026-08-18T10:00:00Z',
  activityError: null,
}

const missingWorkspace: WorkspaceOverview = {
  ...activeWorkspace,
  workspaceId: 'missing',
  name: 'Missing project',
  rootPath: 'C:\\Code\\Missing',
  isAvailable: false,
  activeAgentCount: 0,
  currentPhase: null,
  activitySummary: null,
}

afterEach(cleanup)

describe('WorkspaceRail', () => {
  it('links available projects with current and live activity semantics', () => {
    render(<WorkspaceRail workspaces={[activeWorkspace]} currentWorkspaceId="cave" />)

    const link = screen.getByRole('link', { name: /CAVE.*1 active agent.*Editing/i })
    expect(link.getAttribute('href')).toBe('/?workspace=cave')
    expect(link.getAttribute('aria-current')).toBe('page')
    expect(link.classList.contains('is-active')).toBe(true)
    expect(link.classList.contains('phase-editing')).toBe(true)
    const avatar = link.querySelector<HTMLElement>('.agent-avatar--editing')
    expect(avatar).toBeTruthy()
    expect(avatar?.style.getPropertyValue('--agent-avatar-size')).toBe('18px')
    expect(link.getAttribute('title')).toContain('Polishing the workspace rail')
  })

  it('shows unavailable registrations without creating a broken link', () => {
    render(<WorkspaceRail workspaces={[missingWorkspace]} currentWorkspaceId="cave" />)

    expect(screen.queryByRole('link')).toBeNull()
    const item = screen.getByLabelText(/Missing project.*unavailable/i)
    expect(item.getAttribute('aria-disabled')).toBe('true')
    expect(item.classList.contains('is-unavailable')).toBe(true)
  })

  it('shows full project names when the browser rail is expanded', () => {
    render(
      <WorkspaceRail
        workspaces={[activeWorkspace, missingWorkspace]}
        currentWorkspaceId="cave"
        expanded
      />,
    )

    expect(document.querySelectorAll('.workspace-rail-project__name'))
      .toHaveLength(2)
    expect(screen.getByText('Missing project')).toBeTruthy()
    expect(document.querySelector<HTMLElement>('.agent-avatar--editing')
      ?.style.getPropertyValue('--agent-avatar-size')).toBe('28px')
  })
})
