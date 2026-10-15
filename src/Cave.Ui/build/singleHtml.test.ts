// @vitest-environment node
import { mkdtemp, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { pathToFileURL } from 'node:url'
import { afterEach, describe, expect, it } from 'vitest'
import { build } from 'vite'
import { singleHtml } from './singleHtml.ts'

const fixtures: string[] = []

afterEach(async () => {
  Reflect.deleteProperty(globalThis, 'caveSingleHtmlReady')
  for (const root of fixtures.splice(0)) {
    if (!root.startsWith(join(tmpdir(), 'cave-single-html-'))) throw new Error('Unexpected fixture root')
    await rm(root, { recursive: true, force: true })
  }
})

async function fixture() {
  const root = await mkdtemp(join(tmpdir(), 'cave-single-html-'))
  fixtures.push(root)
  await writeFile(join(root, 'index.html'), '<!doctype html><html><head></head><body><script type="module" src="./main.js"></script></body></html>')
  await writeFile(join(root, 'main.js'), `
    import './style.css'
    import logo from './logo.svg'
    globalThis.caveSingleHtmlReady = import('./lazy.js').then(({ payload }) => ({ payload, logo }))
  `)
  await writeFile(join(root, 'lazy.js'), `export const payload = '</script><script><!--'`)
  await writeFile(join(root, 'style.css'), ':root { --closing-tag: "</style>"; color: red; }')
  await writeFile(join(root, 'logo.svg'), `<svg xmlns="http://www.w3.org/2000/svg"><!--${'x'.repeat(6000)}--><path d="M0 0"/></svg>`)
  return root
}

describe('singleHtml build contract', () => {
  it('embeds CSS, large assets and dynamic imports without changing script text values', async () => {
    const root = await fixture()
    await build({
      root,
      configFile: false,
      plugins: [singleHtml()],
      logLevel: 'silent',
      build: { minify: false, cssMinify: false },
    })

    expect(await readdir(join(root, 'dist'))).toEqual(['index.html'])
    const html = await readFile(join(root, 'dist/index.html'), 'utf8')
    expect(html).toContain('<style>')
    expect(html).not.toMatch(/<script\b[^>]*\bsrc=|<link\b[^>]*rel="stylesheet"/i)
    expect(html.match(/<\/style>/gi)).toHaveLength(1)
    expect(html.match(/<\/script>/gi)).toHaveLength(1)
    const script = html.match(/<script\b[^>]*>([\s\S]*?)<\/script\b[^>]*>/i)?.[1]
    expect(script).toBeDefined()
    const modulePath = join(root, 'embedded.mjs')
    await writeFile(modulePath, script!)
    await import(pathToFileURL(modulePath).href)
    const result = await (Reflect.get(globalThis, 'caveSingleHtmlReady') as Promise<{ payload: string; logo: string }>)
    expect(result?.payload).toBe('</script><script><!--')
    expect(result?.logo).toMatch(/^data:image\/svg\+xml/)
  })

  it('rejects multiple HTML entries instead of publishing an incomplete embedded resource', async () => {
    const root = await fixture()
    await writeFile(join(root, 'second.html'), '<!doctype html><script type="module" src="./main.js"></script>')
    await expect(build({
      root,
      configFile: false,
      plugins: [singleHtml()],
      logLevel: 'silent',
      build: { rolldownOptions: { input: [join(root, 'index.html'), join(root, 'second.html')] } },
    })).rejects.toThrow(/multiple inputs|exactly one HTML entry/)
  })
})
