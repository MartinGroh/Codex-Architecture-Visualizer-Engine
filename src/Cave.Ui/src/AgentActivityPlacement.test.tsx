import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentActivityPlacement } from './AgentActivityPlacement'

afterEach(cleanup)

describe('AgentActivityPlacement', () => {
  it('offers to move a canvas spotlight into the top bar', () => {
    const onTogglePlacement = vi.fn()
    const { container } = render(
      <AgentActivityPlacement
        placement="canvas"
        agents={[]}
        activeNodeCount={0}
        recentEditCount={0}
        unmappedCount={0}
        isSelected
        onSelect={() => undefined}
        onTogglePlacement={onTogglePlacement}
      />,
    )

    expect(container.querySelector('.agent-activity-placement--canvas')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Move live agent activity to the top bar' }))
    expect(onTogglePlacement).toHaveBeenCalledOnce()
  })

  it('offers to return a top-bar spotlight to the canvas', () => {
    const onTogglePlacement = vi.fn()
    const { container } = render(
      <AgentActivityPlacement
        placement="topbar"
        agents={[]}
        activeNodeCount={0}
        recentEditCount={0}
        unmappedCount={0}
        isSelected
        onSelect={() => undefined}
        onTogglePlacement={onTogglePlacement}
      />,
    )

    expect(container.querySelector('.agent-activity-placement--topbar')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Move live agent activity to the canvas' }))
    expect(onTogglePlacement).toHaveBeenCalledOnce()
  })
})
