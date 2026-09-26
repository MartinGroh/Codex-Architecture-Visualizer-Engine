// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ConversationPanel } from './ConversationPanel'

describe('conversation panel', () => {
  afterEach(cleanup)

  it('sends user-entered text through the embedded host callback', async () => {
    const onSend = vi.fn().mockResolvedValue(true)
    render(
      <ConversationPanel
        conversation={{
          sharingEnabled: false,
          status: 'Disabled',
          messages: [],
          control: { sessionId: null, turnId: null, state: 'Unavailable', canSend: false, deliveries: [], error: null },
          error: null,
        }}
        canSend
        canManageSharing
        busy={false}
        onClose={vi.fn()}
        onSend={onSend}
        onSetSharing={vi.fn()}
      />,
    )

    fireEvent.change(screen.getByLabelText('Message Codex'), { target: { value: 'Review this boundary' } })
    fireEvent.click(screen.getByRole('button', { name: 'Send to chat' }))

    expect(onSend).toHaveBeenCalledWith('Review this boundary')
  })

  it('keeps the draft when the host cannot send it', async () => {
    render(
      <ConversationPanel
        conversation={{
          sharingEnabled: false,
          status: 'Disabled',
          messages: [],
          control: { sessionId: null, turnId: null, state: 'Unavailable', canSend: false, deliveries: [], error: null },
          error: null,
        }}
        canSend
        canManageSharing
        busy={false}
        onClose={vi.fn()}
        onSend={vi.fn().mockResolvedValue(false)}
        onSetSharing={vi.fn()}
      />,
    )

    fireEvent.change(screen.getByLabelText('Message Codex'), { target: { value: 'Keep this draft' } })
    fireEvent.click(screen.getByRole('button', { name: 'Send to chat' }))

    expect(await screen.findByDisplayValue('Keep this draft')).toBeTruthy()
  })

  it('keeps typing disabled in the standalone read-only viewer', () => {
    render(
      <ConversationPanel
        conversation={{
          sharingEnabled: true,
          status: 'Ready',
          messages: [],
          control: { sessionId: null, turnId: null, state: 'Unavailable', canSend: false, deliveries: [], error: null },
          error: null,
        }}
        canSend={false}
        canManageSharing={false}
        busy={false}
        onClose={vi.fn()}
        onSend={vi.fn().mockResolvedValue(false)}
        onSetSharing={vi.fn()}
      />,
    )

    expect(screen.getByLabelText('Message Codex').hasAttribute('disabled')).toBe(true)
    expect(screen.getByRole('button', { name: 'On' }).hasAttribute('disabled')).toBe(true)
  })

  it('presents a single truthful waiting state instead of historical status churn', () => {
    render(
      <ConversationPanel
        conversation={{
          sharingEnabled: true,
          status: 'Ready',
          messages: [],
          control: {
            sessionId: 'task-12345678',
            turnId: 'turn-1',
            state: 'Queued',
            canSend: false,
            deliveries: [
              {
                messageId: 'complete', sessionId: 'task-12345678', turnId: 'turn-0',
                state: 'Completed', queuedAtUtc: '2026-08-23T10:00:00Z',
                updatedAtUtc: '2026-08-23T10:01:00Z', error: null,
              },
              {
                messageId: 'waiting', sessionId: 'task-12345678', turnId: null,
                state: 'Queued', queuedAtUtc: '2026-08-23T10:02:00Z',
                updatedAtUtc: '2026-08-23T10:02:00Z', error: null,
              },
            ],
            error: 'Waiting for the desktop-owned Codex turn to finish.',
          },
          error: null,
        }}
        canSend={false}
        canManageSharing
        busy={false}
        onClose={vi.fn()}
        onSend={vi.fn().mockResolvedValue(false)}
        onSetSharing={vi.fn()}
      />,
    )

    expect(screen.getByText('Waiting')).toBeTruthy()
    expect(screen.getByText('One message waiting')).toBeTruthy()
    expect(screen.queryByText('Completed')).toBeNull()
    expect(screen.getByLabelText('Message Codex').hasAttribute('disabled')).toBe(true)
  })

  it('renders retained Markdown as structured public chat content', () => {
    render(
      <ConversationPanel
        conversation={{
          sharingEnabled: true,
          status: 'Ready',
          messages: [{
            eventId: 'message-1', sessionId: 'task', turnId: 'turn', agentId: null,
            agentType: null, isSubagent: false, role: 'Assistant', kind: 'Final',
            text: '**Purpose**\n\n- Coordinates use cases\n- Depends on `Cave.Domain`',
            isTruncated: false, isStreaming: false, occurredAtUtc: '2026-08-23T10:00:00Z',
          }],
          control: { sessionId: 'task', turnId: 'turn', state: 'Ready', canSend: true, deliveries: [], error: null },
          error: null,
        }}
        canSend
        canManageSharing
        busy={false}
        onClose={vi.fn()}
        onSend={vi.fn().mockResolvedValue(true)}
        onSetSharing={vi.fn()}
      />,
    )

    expect(screen.getByText('Purpose').tagName).toBe('STRONG')
    expect(screen.getAllByRole('listitem')).toHaveLength(2)
    expect(screen.getByText('Cave.Domain').tagName).toBe('CODE')
  })

  it('does not mix a completed delivery row into an owner-hook response in progress', () => {
    render(
      <ConversationPanel
        conversation={{
          sharingEnabled: true,
          status: 'Ready',
          messages: [],
          control: {
            sessionId: 'task', turnId: 'turn', state: 'Running', canSend: false,
            deliveries: [{
              messageId: 'claimed', sessionId: 'task', turnId: 'turn', state: 'Completed',
              queuedAtUtc: '2026-08-23T10:00:00Z', updatedAtUtc: '2026-08-23T10:01:00Z', error: null,
            }],
            error: null,
          },
          error: null,
        }}
        canSend={false}
        canManageSharing
        busy={false}
        onClose={vi.fn()}
        onSend={vi.fn().mockResolvedValue(false)}
        onSetSharing={vi.fn()}
      />,
    )

    expect(screen.getByText('Delivering')).toBeTruthy()
    expect(screen.queryByText('Last message delivered')).toBeNull()
  })
})
