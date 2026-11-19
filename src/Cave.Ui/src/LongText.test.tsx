// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { useEffect } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { CodexText } from './CodexText'
import { LongText } from './LongText'
import { getLongTextPreview } from './longTextPreview'

beforeEach(() => {
  // jsdom has dialog elements without the browser's top layer implementation.
  Object.assign(HTMLDialogElement.prototype, {
    showModal(this: HTMLDialogElement) { this.open = true },
    close(this: HTMLDialogElement) { this.open = false; this.dispatchEvent(new Event('close')) },
  })
})
afterEach(cleanup)

const prose = 'The contact log records the scope and the decisions behind the change. '.repeat(9)

describe('long text disclosure', () => {
  it('uses a bounded excerpt for large prose, structural height and long tokens', () => {
    expect(getLongTextPreview('A short sentence.')).toEqual({ excerpt: 'A short sentence.', isLong: false, wordCount: 3 })
    const preview = getLongTextPreview(prose)
    expect(preview.isLong).toBe(true)
    expect(preview.excerpt.length).toBeLessThanOrEqual(241)
    expect(preview.excerpt).toMatch(/^The contact log.+…$/u)
    expect(getLongTextPreview('A\nB\nC\nD\nE\nF\nG\nH').isLong).toBe(true)
    expect(getLongTextPreview('x'.repeat(100)).isLong).toBe(true)
    expect(getLongTextPreview(prose, 'markdown', 'An existing plain projection')).toEqual({ ...preview, excerpt: 'An existing plain projection' })
  })

  it('keeps short semantic content inline and can use a smaller budget for a dense card', () => {
    const { rerender } = render(<LongText title="agent focus" text="Short log."><p><strong>Short log.</strong></p></LongText>)
    expect(screen.getByText('Short log.').tagName).toBe('STRONG')
    expect(screen.queryByRole('button')).toBeNull()
    const medium = 'Three regular lines are enough for the card. '.repeat(4)
    rerender(<LongText title="agent focus" text={medium} compact />)
    expect(screen.getByRole('button', { name: 'Read full agent focus' })).toBeTruthy()
  })

  it('mounts one canonical child only while the reader is open and revokes it with the source', () => {
    const mounted = vi.fn()
    const unmounted = vi.fn()
    const CanonicalReply = () => {
      useEffect(() => { mounted(); return unmounted }, [])
      return <p>Canonical reply</p>
    }
    const { rerender } = render(<LongText title="shared reply" text={prose}><CanonicalReply /></LongText>)
    const opener = screen.getByRole('button', { name: 'Read full shared reply' })
    expect(mounted).not.toHaveBeenCalled()
    fireEvent.click(opener)
    const reader = screen.getByRole('dialog', { name: 'shared reply' })
    expect(document.activeElement).toBe(within(reader).getByRole('heading', { name: 'shared reply' }))
    expect(within(reader).getByText('Canonical reply')).toBeTruthy()
    expect(mounted).toHaveBeenCalledTimes(1)
    fireEvent.click(within(reader).getByRole('button', { name: 'Close reader' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(opener)
    fireEvent.click(opener)
    rerender(<p>Sharing was revoked</p>)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByText('Canonical reply')).toBeNull()
    expect(unmounted).toHaveBeenCalledTimes(2)
  })

  it('shows Markdown as plain preview and keeps its semantics and real headings in the reader', () => {
    const text = `# Result\n\n**Implemented** ${prose}\n\n\`\`\`text\n# Fake heading\n\`\`\`\n\n## Evidence\n\n| Check | Result |\n| --- | --- |\n| Review | Ready |`
    render(<LongText title="reply" text={text} format="markdown"><CodexText text={text} attributed={false} /></LongText>)
    const excerpt = screen.getByText(/^Result Implemented The contact log/u)
    expect(excerpt.textContent).not.toContain('**')
    expect(excerpt.querySelector('strong')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Read full reply' }))
    const reader = screen.getByRole('dialog', { name: 'reply' })
    expect(within(reader).getByText('Implemented').tagName).toBe('STRONG')
    expect(within(reader).getByRole('table').parentElement?.className).toBe('codex-text__table')
    expect(within(reader).getByText('# Fake heading').tagName).toBe('CODE')
    const contents = within(reader).getByRole('button', { name: 'Contents' })
    fireEvent.click(contents)
    expect(contents.getAttribute('aria-expanded')).toBe('true')
    const navigation = within(reader).getByRole('navigation', { name: 'Reader contents' })
    const [result, evidence] = within(navigation).getAllByRole('button')
    expect([result.textContent, evidence.textContent]).toEqual(['Result', 'Evidence'])
    expect(result.getAttribute('aria-current')).toBe('location')
    fireEvent.click(evidence)
    expect(contents.getAttribute('aria-expanded')).toBe('false')
    expect(evidence.getAttribute('aria-current')).toBe('location')
    expect(document.activeElement).toBe(within(reader).getByRole('heading', { name: 'Evidence' }))
  })

  it('labels the author and uses the same localized source count as the graph preview', () => {
    const text = `${'original '.repeat(1200)}ending`
    render(<LongText text={text} title="final reply" byline="Emily" />)
    expect(screen.queryByText('Emily')).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Read full final reply' }))
    const reader = within(screen.getByRole('dialog', { name: 'final reply' }))
    expect(reader.getByText('Emily')).toBeTruthy()
    expect(reader.getByText(`${getLongTextPreview(text).wordCount.toLocaleString()} words`)).toBeTruthy()
  })

  it('reads existing Codex block and inline attributions without listing machine directives', () => {
    const text = `:::writing{variant="email" id="12345" subject="Draft update"}\n:codex-followup[Check scope]{prompt="Read the scope carefully"}\n${prose}\n:::`
    const { excerpt } = getLongTextPreview(text, 'codex')
    expect(excerpt).toMatch(/^Draft update Check scope The contact log/u)
    expect(excerpt).not.toContain(':::writing')
    expect(excerpt).not.toContain('Read the scope carefully')
  })

  it('reports captured-only text in short and long variants', () => {
    const { rerender } = render(<LongText title="instruction" text={'A line\nA second line'} truncated />)
    expect(screen.getByText('Only the captured text is available.')).toBeTruthy()
    expect(screen.getByText(/A line/u).textContent).toBe('A line\nA second line')
    expect(screen.queryByRole('button')).toBeNull()
    rerender(<LongText title="instruction" text={prose} truncated />)
    expect(screen.queryByRole('button', { name: 'Read full instruction' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Read instruction' }))
    expect(within(screen.getByRole('dialog')).getByText('Only the captured text is available.')).toBeTruthy()
  })

  it('responds once to each request and ignores a descendant dialog closing', () => {
    const firstRequest = {}
    function reply(request: object) {
      return <LongText title="reply" text={prose} openRequest={request}><dialog open aria-label="image" /></LongText>
    }
    const { rerender } = render(reply(firstRequest))
    const reader = screen.getByRole('dialog', { name: 'reply' })
    const image = within(reader).getByRole<HTMLDialogElement>('dialog', { name: 'image' })
    image.close()
    expect(screen.getByRole('dialog', { name: 'reply' })).toBe(reader)
    fireEvent.click(within(reader).getByRole('button', { name: 'Close reader' }))
    rerender(reply(firstRequest))
    expect(screen.queryByRole('dialog', { name: 'reply' })).toBeNull()
    rerender(reply({}))
    expect(screen.getByRole('dialog', { name: 'reply' })).toBeTruthy()
  })
})
