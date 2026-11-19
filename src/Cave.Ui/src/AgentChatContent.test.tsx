import { cleanup, render, screen } from '@testing-library/react'
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

  it('uses bounded fenced Mermaid blocks and opens source when a preview fails', async () => {
    const source = 'flowchart LR\n  A --> B'
    vi.mocked(renderAgentChatDiagram).mockResolvedValue(null)
    render(<AgentChatContent text={'```mermaid\n' + source + '\n```\n\n```text\nNon-diagram\n```'}
      artifacts={[]} artifactUrl={() => ''} />)

    expect(await screen.findByText('Diagram preview unavailable')).toBeTruthy()
    expect(renderAgentChatDiagram).toHaveBeenCalledTimes(1)
    expect(renderAgentChatDiagram).toHaveBeenCalledWith(expect.stringMatching(/^cave-diagram-/), source, expect.any(AbortSignal))
    expect(screen.getByText('Mermaid source').closest('details')?.open).toBe(true)
    expect(screen.getByText('Non-diagram').tagName).toBe('CODE')
  })
})
