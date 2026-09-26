// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { CodexText } from './CodexText'
import { parseCodexText, readCodexAttributes } from './codexTextFormat'

afterEach(cleanup)

describe('Codex public text', () => {
  it('renders tables, task lists, strikethrough and regular Markdown links', () => {
    render(<CodexText text={'| Feature | State |\n| --- | --- |\n| Chat | Ready |\n\n- [x] Tested\n\n~~Old~~ [Docs](https://example.com/docs)'} />)
    expect(screen.getByRole('table')).toBeTruthy()
    expect(screen.getByRole('cell', { name: 'Ready' })).toBeTruthy()
    expect((screen.getByRole('checkbox') as HTMLInputElement).checked).toBe(true)
    expect(screen.getByText('Old').tagName).toBe('DEL')
    expect(screen.getByRole('link', { name: 'Docs' }).getAttribute('href')).toBe('https://example.com/docs')
  })

  it('decodes writing attributes, escaped quotes and the complete draft body', () => {
    render(<CodexText text={'Before\n\n:::writing{variant="email" id="12345" subject="A \\"quoted\\" subject" recipient="reader@example.com"}\n**Hello**\n\nBody.\n:::\n\nAfter'} />)
    expect(screen.getByRole('region', { name: 'A "quoted" subject' })).toBeTruthy()
    expect(screen.getByText('To: reader@example.com')).toBeTruthy()
    expect(screen.getByText('Hello').tagName).toBe('STRONG')
    expect(screen.getByText('Before')).toBeTruthy()
    expect(screen.getByText('After')).toBeTruthy()
    expect(screen.queryByText(/:::writing/)).toBeNull()
  })

  it('keeps an unfinished streaming writing block and code fences intact', () => {
    const text = ':::writing{variant="document" id="12345"}\n# Draft\n\n```text\n:::\n```\n\nStill writing'
    const { container } = render(<CodexText text={text} />)
    expect(screen.getByRole('heading', { name: 'Draft' })).toBeTruthy()
    expect(container.querySelector('pre code')?.textContent).toBe(':::\n')
    expect(screen.getByText('Still writing')).toBeTruthy()
  })

  it('renders attributed code comments and created-chat markers as read-only content', () => {
    const { container } = render(<CodexText text={'::code-comment{title="[P2] Boundary" body="Keep **ownership** here." file="src/Example.cs" start=12 end=14 priority=2}\n\n::created-thread{threadId="thread-example"}'} />)
    expect(screen.getByRole('region', { name: '[P2] Boundary' })).toBeTruthy()
    expect(screen.getByText('src/Example.cs:12–14')).toBeTruthy()
    expect(screen.getByText('ownership').tagName).toBe('STRONG')
    expect(screen.getByText('thread-example')).toBeTruthy()
    expect(container.querySelector('button')).toBeNull()
  })

  it('decodes inline suggestions and preserves citation identities without inventing links', () => {
    const { container } = render(<CodexText text={'- :codex-followup[Review changes]{prompt="Review \\"this\\" change"}\n\nEvidence. \uE200cite\uE202turn0search0\uE202turn1search2\uE201 \uE200filecite\uE202turn0file0\uE202L1-L3\uE201'} />)
    expect(screen.getByText('Review changes').getAttribute('title')).toBe('Review "this" change')
    expect(screen.getByText('[Sources: turn0search0, turn1search2]')).toBeTruthy()
    expect(screen.getByText('[File source: turn0file0, L1-L3]')).toBeTruthy()
    expect(container.querySelector('a')).toBeNull()
  })

  it('preserves directive examples in fenced and inline code', () => {
    const marker = '::created-thread{threadId="example"}'
    const { container } = render(<CodexText text={'```text\n' + marker + '\n```\n\n`:codex-followup[Example]{prompt="Do something"}`\n\n`\uE200cite\uE202turn0search0\uE201`'} />)
    expect(container.querySelector('pre code')?.textContent).toBe(`${marker}\n`)
    expect(screen.getByText(':codex-followup[Example]{prompt="Do something"}').tagName).toBe('CODE')
    expect(screen.getByText('\uE200cite\uE202turn0search0\uE201').tagName).toBe('CODE')
  })

  it('decodes multiple suggestions containing braces and escaped quotes', () => {
    render(<CodexText text={':codex-followup[First]{prompt="Review {this} \\"change\\""} and :codex-followup[Second]{prompt="Keep \\"that\\" one"}'} />)
    expect(screen.getByText('First').getAttribute('title')).toBe('Review {this} "change"')
    expect(screen.getByText('Second').getAttribute('title')).toBe('Keep "that" one')
  })

  it('keeps malformed, unknown and escaped attributes visible', () => {
    const text = ':::writing{variant="future"}\nUnknown body\n:::\n\n::code-comment{title="Missing body"}\n\n::created-thread{threadId="bad\\q"}'
    const { container } = render(<CodexText text={text} />)
    expect(container.textContent).toContain(':::writing{variant="future"}')
    expect(container.textContent).toContain('::code-comment{title="Missing body"}')
    expect(screen.queryByText('Created chat')).toBeNull()
  })

  it('does not decode user-authored attributed markup', () => {
    const { container } = render(<CodexText attributed={false} text={'::created-thread{threadId="example"}\n\n:codex-followup[Example]{prompt="Do something"}'} />)
    expect(container.textContent).toContain('::created-thread{threadId="example"}')
    expect(container.textContent).toContain(':codex-followup[Example]{prompt="Do something"}')
  })

  it('never executes raw HTML, dangerous URLs or attributed actions', () => {
    const { container } = render(<CodexText text={'<img src=x onerror="alert(1)">\n\n[Unsafe](javascript:alert%281%29)\n\n:::writing{variant="standard" id="12345" subject="<script>alert(1)</script>"}\nText.\n:::'} />)
    expect(container.querySelector('script, img')).toBeNull()
    expect(screen.getByText('Unsafe').getAttribute('href')).toBe('')
    expect(screen.getByText('<script>alert(1)</script>')).toBeTruthy()
  })

  it('rejects duplicate and malformed attributes', () => {
    expect(readCodexAttributes('title="one" title="two"')).toBeNull()
    expect(readCodexAttributes('title="bad\\q"')).toBeNull()
    expect(readCodexAttributes('title="ok" junk')).toBeNull()
    expect(readCodexAttributes('start=12 body="line\\nnext"')).toMatchObject({ start: '12', body: 'line\nnext' })
  })

  it('preserves indented directive examples and mismatched fence closers', () => {
    expect(parseCodexText('    ::created-thread{threadId="example"}')).toEqual([{ kind: 'markdown', text: '    ::created-thread{threadId="example"}' }])
    const text = '````text\n```\n::created-thread{threadId="example"}\n````'
    expect(parseCodexText(text)).toEqual([{ kind: 'markdown', text }])
  })
})
