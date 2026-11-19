// @vitest-environment node
import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'

const css = readFileSync('src/App.css', 'utf8')



describe('architecture viewport containment', () => {
  it('constrains the desktop shell row instead of letting navigation content expand the graph offscreen', () => {
    const shell = css.match(/\.app-shell\s*\{([^}]+)\}/)?.[1]
    expect(shell).toMatch(/grid-template-rows:\s*minmax\(0,\s*1fr\)/)
  })

  it('keeps expanded current work scrollable without consuming the graph viewport', () => {
    const context = css.match(/\.architecture-work-context\s*\{([^}]+)\}/)?.[1]
    expect(context).toMatch(/max-height:\s*40%/)
    expect(context).toMatch(/overflow-y:\s*auto/)
  })
})
