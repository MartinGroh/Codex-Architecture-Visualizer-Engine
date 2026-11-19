import { readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { gzipSync } from 'node:zlib'
import type { Plugin } from 'vite'

const moduleId = 'virtual:cave-mermaid-gzip'
const resolvedId = `\0${moduleId}`

/** Embeds the upstream self-contained Mermaid renderer without expanding the main HTML on disk. */
export function mermaidBundle(): Plugin {
  return {
    name: 'cave-mermaid-bundle',
    resolveId: (id) => id === moduleId ? resolvedId : null,
    async load(id) {
      if (id !== resolvedId) return null
      const bundle = createRequire(import.meta.url).resolve('mermaid/dist/mermaid.min.js')
      this.addWatchFile(bundle)
      const compressed = gzipSync(await readFile(bundle), { level: 9 }).toString('base64')
      return `export default ${JSON.stringify(compressed)};`
    },
  }
}
