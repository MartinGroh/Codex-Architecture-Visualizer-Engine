import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { AgentAvatar } from './AgentAvatar'
import { AgentModelLabel } from './AgentModel'
import { agentModelFamily } from './agentModelPresentation'

afterEach(cleanup)

describe('observed model presentation', () => {
  it.each(['luna', 'terra', 'sol', 'astra'] as const)('uses the %s family glyph independently of phase and role', family => {
    const configuration = { model: `gpt-6.1-${family}`, reasoningEffort: 'xhigh' }
    const { container, rerender } = render(<AgentAvatar phase="Reading" active isSubagent configuration={configuration} />)
    expect(container.querySelector(`[data-model-family="${family}"]`)).toBeTruthy()
    expect(container.querySelector('[data-agent-avatar-accessory="glasses"]')).toBeTruthy()
    expect(screen.getByRole('img').getAttribute('aria-label')).toContain(`gpt-6.1-${family}`)
    expect(screen.getByRole('img').getAttribute('aria-label')).toContain('Extra high')
    rerender(<AgentAvatar phase="Editing" active={false} configuration={configuration} />)
    expect(container.querySelector(`[data-model-family="${family}"]`)).toBeTruthy()
    expect(container.querySelector('[data-agent-phase="Idle"]')).toBeTruthy()
  })

  it('does not guess families from substrings or turn missing values into a default effort', () => {
    expect(agentModelFamily('gpt-consolation')).toBe('unknown')
    expect(agentModelFamily('gpt-lunatic')).toBe('unknown')
    const { container } = render(<AgentModelLabel configuration={null} />)
    expect(screen.getByText('Model unknown')).toBeTruthy()
    expect(container.querySelector('abbr')?.title).toContain('Unknown')
  })

  it('preserves an unfamiliar model and future effort identifier as readable source data', () => {
    render(<AgentModelLabel configuration={{ model: 'provider-next', reasoningEffort: 'adaptive-next' }} />)
    expect(screen.getByText('provider-next')).toBeTruthy()
    expect(screen.getByTitle('Configured thinking effort: adaptive-next')).toBeTruthy()
  })
})
