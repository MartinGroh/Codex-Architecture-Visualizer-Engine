import { useId } from 'react'
import {
  Braces,
  CheckSquare2,
  Code2,
  Image as ImageIcon,
  PenLine,
  Sigma,
  Trash2,
  Type,
} from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import type {
  SemanticAssetReference,
  SemanticContentBlock,
  SemanticDrawingStroke,
} from './semanticWorkspaceTypes'
import './SemanticWorkspace.css'

export interface RichContentEditorProps {
  blocks: SemanticContentBlock[]
  assets: SemanticAssetReference[]
  onChangeBlock: (block: SemanticContentBlock) => void
  onDeleteBlock?: (blockId: string) => void
  resolveAssetUrl?: (asset: SemanticAssetReference) => string | null
  readOnly?: boolean
  heading?: string
}

/** Renders and edits semantic content blocks without storing presentation state in the block model. */
export function RichContentEditor({
  blocks,
  assets,
  onChangeBlock,
  onDeleteBlock,
  resolveAssetUrl,
  readOnly = false,
  heading = 'Rich content',
}: RichContentEditorProps) {
  const headingId = useId()
  return (
    <section className="rich-content" aria-labelledby={headingId}>
      <header className="rich-content__header">
        <div>
          <h2 id={headingId}><Braces size={19} /> {heading}</h2>
        </div>
        <span>{blocks.length} {blocks.length === 1 ? 'block' : 'blocks'}</span>
      </header>

      {blocks.length === 0
        ? (
            <div className="rich-content__empty" role="status">
              <PenLine size={20} />
              <span>No content blocks have been added yet.</span>
            </div>
          )
        : (
            <div className="rich-content__blocks">
              {blocks.map((block) => (
                <RichContentBlockEditor
                  key={block.id}
                  block={block}
                  assets={assets}
                  readOnly={readOnly}
                  onChange={onChangeBlock}
                  onDelete={onDeleteBlock ? () => onDeleteBlock(block.id) : undefined}
                  resolveAssetUrl={resolveAssetUrl}
                />
              ))}
            </div>
          )}
    </section>
  )
}

export interface RichContentPreviewProps {
  blocks: SemanticContentBlock[]
  assets: SemanticAssetReference[]
  resolveAssetUrl?: (asset: SemanticAssetReference) => string | null
  heading?: string
}

/** Read-only convenience projection for the same semantic content blocks. */
export function RichContentPreview({ blocks, assets, resolveAssetUrl, heading }: RichContentPreviewProps) {
  return (
    <RichContentEditor
      blocks={blocks}
      assets={assets}
      resolveAssetUrl={resolveAssetUrl}
      heading={heading}
      readOnly
      onChangeBlock={() => undefined}
    />
  )
}

interface RichContentBlockEditorProps {
  block: SemanticContentBlock
  assets: SemanticAssetReference[]
  readOnly: boolean
  onChange: (block: SemanticContentBlock) => void
  onDelete?: () => void
  resolveAssetUrl?: (asset: SemanticAssetReference) => string | null
}

function RichContentBlockEditor({
  block,
  assets,
  readOnly,
  onChange,
  onDelete,
  resolveAssetUrl,
}: RichContentBlockEditorProps) {
  return (
    <article className={`rich-content-block rich-content-block--${block.kind}`}>
      <header>
        <span>{blockIcon(block.kind)} {blockLabel(block.kind)}</span>
        {!readOnly && onDelete && (
          <button type="button" onClick={onDelete} aria-label={`Delete ${blockLabel(block.kind)} block`}>
            <Trash2 size={14} />
          </button>
        )}
      </header>
      {renderBlock(block, assets, readOnly, onChange, resolveAssetUrl)}
    </article>
  )
}

function renderBlock(
  block: SemanticContentBlock,
  assets: SemanticAssetReference[],
  readOnly: boolean,
  onChange: (block: SemanticContentBlock) => void,
  resolveAssetUrl?: (asset: SemanticAssetReference) => string | null,
) {
  switch (block.kind) {
    case 'text':
      return (
        <div className="rich-content-text">
          {!readOnly && (
            <label>
              <span>Markdown text</span>
              <textarea
                value={block.text}
                aria-label="Markdown text"
                onChange={(event) => onChange({ ...block, text: event.currentTarget.value })}
              />
            </label>
          )}
          <div className="rich-content-markdown">
            {block.text ? <ReactMarkdown>{block.text}</ReactMarkdown> : <p className="rich-content-placeholder">No text yet.</p>}
          </div>
        </div>
      )

    case 'checklist': {
      const items = [...block.items].sort((left, right) => left.order - right.order)
      return (
        <div className="rich-content-checklist">
          {items.length === 0 && <p className="rich-content-placeholder">No checklist items yet.</p>}
          {items.map((item) => (
            <label key={item.id} className={item.completed ? 'is-complete' : ''}>
              <input
                type="checkbox"
                checked={item.completed}
                disabled={readOnly}
                onChange={(event) => onChange({
                  ...block,
                  items: block.items.map((candidate) => candidate.id === item.id
                    ? { ...candidate, completed: event.currentTarget.checked }
                    : candidate),
                })}
              />
              {readOnly
                ? <span>{item.text}</span>
                : (
                    <input
                      type="text"
                      value={item.text}
                      aria-label={`Checklist item ${item.order + 1}`}
                      onChange={(event) => onChange({
                        ...block,
                        items: block.items.map((candidate) => candidate.id === item.id
                          ? { ...candidate, text: event.currentTarget.value }
                          : candidate),
                      })}
                    />
                  )}
            </label>
          ))}
        </div>
      )
    }

    case 'code':
      return (
        <div className="rich-content-code">
          {!readOnly && (
            <div className="rich-content-code__fields">
              <label>
                <span>Language</span>
                <input value={block.language} onChange={(event) => onChange({ ...block, language: event.currentTarget.value })} />
              </label>
              <label>
                <span>Filename</span>
                <input value={block.filename ?? ''} onChange={(event) => onChange({ ...block, filename: event.currentTarget.value || null })} />
              </label>
            </div>
          )}
          {!readOnly && (
            <label>
              <span>Source code</span>
              <textarea value={block.source} aria-label="Source code" onChange={(event) => onChange({ ...block, source: event.currentTarget.value })} />
            </label>
          )}
          <figure>
            <figcaption>{block.filename || block.language || 'Code'}</figcaption>
            <pre><code className={block.language ? `language-${block.language}` : undefined}>{block.source || '// No source yet'}</code></pre>
          </figure>
        </div>
      )

    case 'math':
      return (
        <div className="rich-content-math">
          {!readOnly && (
            <label>
              <span>LaTeX source</span>
              <textarea value={block.source} aria-label="LaTeX source" onChange={(event) => onChange({ ...block, source: event.currentTarget.value })} />
            </label>
          )}
          <div role="math" aria-label={`Formula: ${block.source || 'empty'}`}>
            <Sigma size={20} aria-hidden="true" />
            <code>{block.source || '\\text{No formula yet}'}</code>
          </div>
        </div>
      )

    case 'image': {
      const asset = assets.find((candidate) => candidate.id === block.assetId)
      const assetUrl = asset && resolveAssetUrl ? resolveAssetUrl(asset) : null
      return (
        <div className="rich-content-image">
          {!readOnly && (
            <div className="rich-content-image__fields">
              <label>
                <span>Asset reference</span>
                <select value={block.assetId} onChange={(event) => onChange({ ...block, assetId: event.currentTarget.value })}>
                  {!asset && <option value={block.assetId}>Missing asset · {block.assetId}</option>}
                  {assets.map((candidate) => <option key={candidate.id} value={candidate.id}>{candidate.relativePath}</option>)}
                </select>
              </label>
              <label>
                <span>Alternative text</span>
                <input value={block.altText} onChange={(event) => onChange({ ...block, altText: event.currentTarget.value })} />
              </label>
              <label>
                <span>Caption</span>
                <input value={block.caption ?? ''} onChange={(event) => onChange({ ...block, caption: event.currentTarget.value || null })} />
              </label>
            </div>
          )}
          {assetUrl && asset
            ? <img src={assetUrl} alt={block.altText || asset.altText} />
            : (
                <div className="rich-content-image__reference" role="img" aria-label={block.altText || asset?.altText || 'Image reference'}>
                  <ImageIcon size={24} />
                  <strong>{asset?.relativePath ?? 'Referenced asset is unavailable'}</strong>
                  <span>{asset?.mediaType ?? block.assetId}</span>
                </div>
              )}
          {(block.caption || asset?.caption) && <p>{block.caption ?? asset?.caption}</p>}
        </div>
      )
    }

    case 'drawing':
      return (
        <div className="rich-content-drawing">
          <DrawingPreview strokes={block.strokes} />
          {!readOnly && block.strokes.length > 0 && (
            <div className="rich-content-drawing__strokes">
              {block.strokes.map((stroke, index) => (
                <fieldset key={stroke.id}>
                  <legend>Stroke {index + 1}</legend>
                  <label>
                    <span>Colour</span>
                    <input
                      type="color"
                      value={normaliseColor(stroke.color)}
                      aria-label={`Stroke ${index + 1} colour`}
                      onChange={(event) => onChange(updateStroke(block, stroke.id, { color: event.currentTarget.value }))}
                    />
                  </label>
                  <label>
                    <span>Width</span>
                    <input
                      type="range"
                      min="1"
                      max="20"
                      value={stroke.width}
                      aria-label={`Stroke ${index + 1} width`}
                      onChange={(event) => onChange(updateStroke(block, stroke.id, { width: Number(event.currentTarget.value) }))}
                    />
                  </label>
                </fieldset>
              ))}
            </div>
          )}
        </div>
      )
  }
}

function DrawingPreview({ strokes }: { strokes: SemanticDrawingStroke[] }) {
  const points = strokes.flatMap((stroke) => stroke.points)
  if (points.length === 0) {
    return <p className="rich-content-placeholder">No vector strokes yet.</p>
  }

  const minX = Math.min(...points.map((point) => point.x))
  const maxX = Math.max(...points.map((point) => point.x))
  const minY = Math.min(...points.map((point) => point.y))
  const maxY = Math.max(...points.map((point) => point.y))
  const padding = 12
  const width = Math.max(1, maxX - minX)
  const height = Math.max(1, maxY - minY)

  return (
    <svg
      viewBox={`${minX - padding} ${minY - padding} ${width + padding * 2} ${height + padding * 2}`}
      role="img"
      aria-label={`${strokes.length} vector ${strokes.length === 1 ? 'stroke' : 'strokes'}`}
      preserveAspectRatio="xMidYMid meet"
    >
      {strokes.map((stroke) => (
        <polyline
          key={stroke.id}
          points={stroke.points.map((point) => `${point.x},${point.y}`).join(' ')}
          fill="none"
          stroke={stroke.color}
          strokeWidth={stroke.width}
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      ))}
    </svg>
  )
}

function updateStroke(
  block: Extract<SemanticContentBlock, { kind: 'drawing' }>,
  strokeId: string,
  changes: Partial<Pick<SemanticDrawingStroke, 'color' | 'width'>>,
): SemanticContentBlock {
  return {
    ...block,
    strokes: block.strokes.map((stroke) => stroke.id === strokeId ? { ...stroke, ...changes } : stroke),
  }
}

function blockLabel(kind: SemanticContentBlock['kind']) {
  switch (kind) {
    case 'text': return 'Text'
    case 'checklist': return 'Checklist'
    case 'code': return 'Code'
    case 'math': return 'Formula'
    case 'image': return 'Image reference'
    case 'drawing': return 'Drawing'
  }
}

function blockIcon(kind: SemanticContentBlock['kind']) {
  switch (kind) {
    case 'text': return <Type size={14} />
    case 'checklist': return <CheckSquare2 size={14} />
    case 'code': return <Code2 size={14} />
    case 'math': return <Sigma size={14} />
    case 'image': return <ImageIcon size={14} />
    case 'drawing': return <PenLine size={14} />
  }
}

function normaliseColor(value: string) {
  return /^#[0-9a-f]{6}$/i.test(value) ? value : '#6f5ce7'
}
