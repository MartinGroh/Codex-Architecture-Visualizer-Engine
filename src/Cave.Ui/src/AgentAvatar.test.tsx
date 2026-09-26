import { cleanup, render } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { AgentAvatar } from './AgentAvatar'

afterEach(cleanup)

describe('AgentAvatar', () => {
  it.each([
    ['Working', 'agent-avatar--working'],
    ['Editing', 'agent-avatar--editing'],
    ['Thinking', 'agent-avatar--thinking'],
    ['Reading', 'agent-avatar--reading'],
    ['Validating', 'agent-avatar--validating'],
  ] as const)('exposes the %s phase to the shared animation vocabulary', (phase, className) => {
    const { container } = render(<AgentAvatar phase={phase} active />)
    const avatar = container.querySelector('.agent-avatar')

    expect(avatar?.classList.contains(className)).toBe(true)
    expect(avatar?.getAttribute('data-agent-phase')).toBe(phase)
  })

  it.each([
    ['Working', 'agent-avatar--working'],
    ['Editing', 'agent-avatar--editing'],
    ['Thinking', 'agent-avatar--thinking'],
    ['Reading', 'agent-avatar--reading'],
    ['Validating', 'agent-avatar--validating'],
  ] as const)('keeps the subagent silhouette in the shared %s phase vocabulary', (phase, className) => {
    const { container } = render(<AgentAvatar phase={phase} active isSubagent />)
    const avatar = container.querySelector('.agent-avatar')

    expect(avatar?.classList.contains(className)).toBe(true)
    expect(avatar?.getAttribute('data-agent-role')).toBe('Subagent')
    expect(container.querySelector('[data-agent-avatar-silhouette="helper-drone"]')).toBeTruthy()
  })

  it('uses role-specific silhouettes without changing the phase vocabulary', () => {
    const { container, rerender } = render(<AgentAvatar phase="Thinking" active />)
    const mainAvatar = container.querySelector('.agent-avatar')

    expect(mainAvatar?.getAttribute('data-agent-role')).toBe('Main agent')
    expect(container.querySelector('[data-agent-avatar-silhouette="main-robot"]')).toBeTruthy()
    expect(container.querySelectorAll('.agent-avatar__antenna-tip')).toHaveLength(1)
    expect(container.querySelector('[data-agent-avatar-accessory="thoughts"]')).toBeTruthy()

    rerender(<AgentAvatar phase="Thinking" active isSubagent />)
    const subagentAvatar = container.querySelector('.agent-avatar')

    expect(subagentAvatar?.getAttribute('data-agent-role')).toBe('Subagent')
    expect(subagentAvatar?.classList.contains('agent-avatar--thinking')).toBe(true)
    expect(container.querySelector('[data-agent-avatar-silhouette="helper-drone"]')).toBeTruthy()
    expect(container.querySelectorAll('.agent-avatar__antenna-tip')).toHaveLength(2)
    expect(container.querySelector('.agent-avatar__drone-skid')).toBeTruthy()
    expect(container.querySelector('[data-agent-avatar-accessory="thoughts"]')).toBeTruthy()
  })

  it.each([false, true])('adds scanning glasses only while reading (subagent: %s)', (isSubagent) => {
    const { container, rerender } = render(<AgentAvatar phase="Reading" active isSubagent={isSubagent} />)
    expect(container.querySelector('[data-agent-avatar-accessory="glasses"]')).toBeTruthy()

    rerender(<AgentAvatar phase="Working" active isSubagent={isSubagent} />)
    expect(container.querySelector('[data-agent-avatar-accessory="glasses"]')).toBeNull()
  })

  it('keeps completed work static even when its last phase is retained', () => {
    const { container } = render(<AgentAvatar phase="Editing" active={false} isSubagent />)
    const avatar = container.querySelector('.agent-avatar')

    expect(avatar?.classList.contains('agent-avatar--idle')).toBe(true)
    expect(avatar?.classList.contains('is-subagent')).toBe(true)
    expect(avatar?.getAttribute('data-agent-phase')).toBe('Idle')
  })
})
