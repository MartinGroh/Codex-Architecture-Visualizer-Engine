// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AskNodeDialog, NodeContextMenu, NodeMemoDialog } from './NodeMemo'
import type { ArchitectureNode } from './types'

const node: ArchitectureNode = {
  id: 'app',
  kind: 'Project',
  name: 'Cave.Application',
  parentId: null,
  qualifiedName: 'Cave.Application',
  description: '.NET class library',
  categoryId: null,
  tags: [],
  sourceLocations: [],
}

describe('node questions', () => {
  afterEach(cleanup)

  it('enables Ask and Explain for a temporary side chat', () => {
    const onExplain = vi.fn()
    const onAsk = vi.fn()
    render(
      <NodeContextMenu
        node={node}
        x={20}
        y={30}
        canAsk
        executionHint="Runs in a temporary side chat."
        onExplain={onExplain}
        onAsk={onAsk}
        onClose={vi.fn()}
      />,
    )

    expect(screen.getByText('Runs in a temporary side chat.')).toBeTruthy()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Explain this' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Ask this box' }))
    expect(onExplain).toHaveBeenCalledOnce()
    expect(onAsk).toHaveBeenCalledOnce()
  })

  it('uses the temporary side-chat label in the question dialog', () => {
    const onAsk = vi.fn()
    render(
      <AskNodeDialog
        node={node}
        submitLabel="Run in temporary side chat"
        onCancel={vi.fn()}
        onAsk={onAsk}
      />,
    )

    fireEvent.change(screen.getByPlaceholderText('What do you want to know?'), {
      target: { value: 'Why does this box matter?' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Run in temporary side chat' }))
    expect(onAsk).toHaveBeenCalledWith('Why does this box matter?')
  })

  it('shows an Explain answer in its own modal', () => {
    const onClose = vi.fn()
    render(
      <NodeMemoDialog
        memo={{
          id: 'memo-1',
          nodeName: 'Cave.Application',
          question: 'Explain why this matters.',
          kind: 'Explain',
          status: 'Ready',
          text: '## Purpose\n\nOwns application use cases.',
          error: null,
        }}
        onClose={onClose}
      />,
    )

    expect(screen.getByRole('dialog', { name: 'Cave.Application' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Purpose' })).toBeTruthy()
    expect(screen.getByText('Owns application use cases.')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Close' }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('keeps an Ask question visible while Codex is responding', () => {
    render(
      <NodeMemoDialog
        memo={{
          id: 'memo-2',
          nodeName: 'Cave.Application',
          question: 'What depends on this?',
          kind: 'Ask',
          status: 'Loading',
          text: 'Cave.Host depends on it.',
          error: null,
        }}
        onClose={vi.fn()}
      />,
    )

    expect(screen.getByText('What depends on this?')).toBeTruthy()
    expect(screen.getByText('Cave.Host depends on it.')).toBeTruthy()
    expect(screen.getByText('Codex is still writing…')).toBeTruthy()
  })
})
