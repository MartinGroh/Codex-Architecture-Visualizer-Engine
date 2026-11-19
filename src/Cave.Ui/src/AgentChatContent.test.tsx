import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentChatContent } from './AgentChatContent'
import { renderAgentChatDiagram } from './agentChatDiagram'

vi.mock('./agentChatDiagram', () => ({ renderAgentChatDiagram: vi.fn() }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
})

describe('AgentChatContent', () => {
  it('uses the artifact endpoint for supported rasters and never loads Markdown images', () => {
    const artifactUrl = vi.fn((id: string) => `/api/chat-artifacts/${id}`)
    render(<AgentChatContent text="**Screenshot** ![Other image](https://example.invalid/unshared.png)" artifacts={[
      { id: 'published', contentType: 'image/png' },
      { id: 'unresolved', contentType: null },
      { id: 'unsupported', contentType: 'image/svg+xml' },
    ]} artifactUrl={artifactUrl} />)

    expect(screen.getByText('Screenshot').tagName).toBe('STRONG')
    expect(screen.getByText('Image preview unavailable (Other image)')).toBeTruthy()
    expect(screen.getAllByText('Image preview unavailable')).toHaveLength(2)
    expect(screen.getByRole('button', { name: 'Open shared image 1 preview' }).querySelector('img')?.src)
      .toBe(new URL('/api/chat-artifacts/published', window.location.href).href)
    expect(artifactUrl.mock.calls).toEqual([['published']])
  })

  it('keeps an image without a local artifact URL unavailable', () => {
    render(<AgentChatContent text="" artifacts={[{ id: 'image', contentType: 'image/webp' }]}
      artifactUrl={() => 'https://example.invalid/image.webp'} />)

    expect(screen.getByText('Image preview unavailable')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /open shared image/i })).toBeNull()
  })

  it('zooms a scoped image from its fitted dimensions, resets it, and returns focus when closed', () => {
    // jsdom provides the element but has no native dialog top layer.
    Object.assign(HTMLDialogElement.prototype, {
      showModal(this: HTMLDialogElement) { this.open = true },
      close(this: HTMLDialogElement) { this.open = false; this.dispatchEvent(new Event('close')) },
    })
    render(<AgentChatContent text="" artifacts={[{ id: 'frame', contentType: 'image/png' }]} artifactUrl={() => '/frame'} />)
    const opener = screen.getByRole('button', { name: 'Open shared image preview' })
    const thumbnail = opener.querySelector('img')!
    Object.defineProperties(thumbnail, { naturalWidth: { value: 3000 }, naturalHeight: { value: 1000 } })
    fireEvent.load(thumbnail)
    fireEvent.click(opener)
    const dialog = screen.getByRole('dialog', { name: 'Shared image' })
    const canvas = within(dialog).getByRole('region', { name: 'Preview canvas' })
    Object.defineProperties(canvas, { clientWidth: { value: 400 }, clientHeight: { value: 300 },
      scrollWidth: { value: 400 }, scrollHeight: { value: 300 } })
    const image = within(canvas).getByRole('img', { name: 'Shared image' })
    const fitted = image.style.width
    expect(fitted).toContain('300cqh')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Zoom in' }))
    expect(image.style.width).not.toBe(fitted)
    expect(within(dialog).getByLabelText('Zoom relative to fit').textContent).toBe('1.5×')
    fireEvent.keyDown(canvas, { key: '+' })
    expect(within(dialog).getByLabelText('Zoom relative to fit').textContent).toBe('2×')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Fit to view' }))
    expect(image.style.width).toBe(fitted)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Close preview' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(opener)
  })

  it('uses bounded fenced Mermaid blocks and opens source when a preview fails', async () => {
    const source = 'flowchart LR\n  A --> B'
    vi.mocked(renderAgentChatDiagram).mockResolvedValue(null)
    render(<AgentChatContent text={'```mermaid\n' + source + '\n```\n\n```text\nNon-diagram\n```'}
      artifacts={[]} artifactUrl={() => ''} />)

    expect(await screen.findByText('Diagram preview unavailable')).toBeTruthy()
    expect(renderAgentChatDiagram).toHaveBeenCalledTimes(1)
    expect(renderAgentChatDiagram).toHaveBeenCalledWith(expect.stringMatching(/^cave-diagram-/), source, expect.any(AbortSignal), expect.any(Object))
    expect(screen.getByText('Mermaid source').closest('details')?.open).toBe(true)
    expect(screen.getByText('Non-diagram').tagName).toBe('CODE')
  })
})
