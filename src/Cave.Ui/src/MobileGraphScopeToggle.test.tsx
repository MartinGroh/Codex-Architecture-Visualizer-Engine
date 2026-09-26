import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MobileGraphScopeToggle } from './MobileGraphScopeToggle'

afterEach(cleanup)

describe('MobileGraphScopeToggle', () => {
  it('opens the full architecture from the focused work view', () => {
    const onChange = vi.fn()
    render(<MobileGraphScopeToggle scope="Work" onChange={onChange} />)

    const button = screen.getByRole('button', { name: 'Show full architecture' })
    expect(button.getAttribute('aria-pressed')).toBe('false')
    fireEvent.click(button)

    expect(onChange).toHaveBeenCalledWith('Architecture')
  })

  it('returns to the active work branch from the full map', () => {
    const onChange = vi.fn()
    render(<MobileGraphScopeToggle scope="Architecture" onChange={onChange} />)

    const button = screen.getByRole('button', { name: 'Show active work' })
    expect(button.getAttribute('aria-pressed')).toBe('true')
    fireEvent.click(button)

    expect(onChange).toHaveBeenCalledWith('Work')
  })
})
