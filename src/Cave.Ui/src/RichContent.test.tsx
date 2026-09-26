import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { RichContentEditor, RichContentPreview } from './RichContent'
import type { SemanticAssetReference, SemanticContentBlock } from './semanticWorkspaceTypes'

afterEach(cleanup)

describe('RichContentEditor', () => {
  it('renders all schema-v1 block kinds as accessible previews', () => {
    render(<RichContentPreview blocks={blocks} assets={assets} />)

    expect(screen.getByRole('heading', { name: 'Rich content' })).toBeTruthy()
    expect(screen.getByRole('heading', { name: 'Boundary note' })).toBeTruthy()
    expect(screen.getByRole('checkbox', { name: 'Ship tests' })).toBeTruthy()
    expect(screen.getByText('SemanticWorkspace.cs')).toBeTruthy()
    expect(screen.getByRole('math', { name: /Formula: E = mc\^2/ })).toBeTruthy()
    expect(screen.getByRole('img', { name: 'Architecture sketch' })).toBeTruthy()
    expect(screen.getByRole('img', { name: '1 vector stroke' })).toBeTruthy()
  })

  it('returns changed semantic blocks instead of owning local copies', () => {
    const onChangeBlock = vi.fn()
    const onDeleteBlock = vi.fn()
    render(
      <RichContentEditor
        blocks={blocks}
        assets={assets}
        onChangeBlock={onChangeBlock}
        onDeleteBlock={onDeleteBlock}
      />,
    )

    fireEvent.change(screen.getByRole('textbox', { name: 'Markdown text' }), {
      target: { value: '# Updated' },
    })
    fireEvent.click(screen.getByRole('checkbox', { name: 'Ship tests' }))
    fireEvent.change(screen.getByRole('slider', { name: 'Stroke 1 width' }), {
      target: { value: '9' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Delete Formula block' }))

    expect(onChangeBlock).toHaveBeenCalledWith(expect.objectContaining({ id: 'text-1', text: '# Updated' }))
    expect(onChangeBlock).toHaveBeenCalledWith(expect.objectContaining({ id: 'check-1' }))
    expect(onChangeBlock).toHaveBeenCalledWith(expect.objectContaining({ id: 'drawing-1' }))
    expect(onDeleteBlock).toHaveBeenCalledWith('math-1')
  })

  it('resolves a referenced asset without changing its semantic identity', () => {
    render(
      <RichContentPreview
        blocks={[blocks.find((block) => block.kind === 'image')!]}
        assets={assets}
        resolveAssetUrl={() => '/assets/architecture.png'}
      />,
    )

    const image = screen.getByRole('img', { name: 'Architecture sketch' }) as HTMLImageElement
    expect(image.getAttribute('src')).toBe('/assets/architecture.png')
  })
})

const assets: SemanticAssetReference[] = [{
  id: 'asset-1', relativePath: 'assets/architecture.png', mediaType: 'image/png',
  caption: 'A first-pass boundary sketch.', altText: 'Architecture sketch', metadata: {},
}]

const blocks: SemanticContentBlock[] = [
  { id: 'text-1', kind: 'text', text: '# Boundary note\nKeep the model canonical.' },
  { id: 'check-1', kind: 'checklist', items: [{ id: 'item-1', text: 'Ship tests', completed: false, order: 0 }] },
  { id: 'code-1', kind: 'code', source: 'record Entity(string Id);', language: 'csharp', filename: 'SemanticWorkspace.cs' },
  { id: 'math-1', kind: 'math', source: 'E = mc^2' },
  { id: 'image-1', kind: 'image', assetId: 'asset-1', caption: null, altText: 'Architecture sketch' },
  {
    id: 'drawing-1', kind: 'drawing', strokes: [{
      id: 'stroke-1', color: '#6f5ce7', width: 4,
      points: [{ x: 0, y: 0, pressure: 1 }, { x: 20, y: 15, pressure: 1 }],
    }],
  },
]
