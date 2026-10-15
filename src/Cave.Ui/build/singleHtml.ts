import type { Plugin } from 'vite'

/** Produces the self-contained HTML shared by the HTTP host and embedded MCP resource. */
export function singleHtml(): Plugin {
  return {
    name: 'cave-single-html',
    apply: 'build',
    enforce: 'post',
    config: () => ({
      base: './',
      build: {
        assetsInlineLimit: () => true,
        cssCodeSplit: false,
        modulePreload: false,
        rolldownOptions: { output: { codeSplitting: false } },
      },
    }),
    generateBundle: {
      // CONSTRAINT: Vite must finish its loader references before scripts move into HTML;
      // earlier inlining leaves unresolved preload placeholders in the embedded resource.
      order: 'post',
      handler(_options, bundle) {
        const documents = Object.values(bundle).filter(
          (item) => item.type === 'asset' && item.fileName.endsWith('.html'),
        )
        if (documents.length !== 1 || documents[0].type !== 'asset') {
          this.error('CAVE requires exactly one HTML entry for its embedded resource.')
        }

        const document = documents[0]
        let html = typeof document.source === 'string'
          ? document.source
          : new TextDecoder().decode(document.source)

        // CONSTRAINT: the MCP host serves only this HTML resource. Splitting or leaving an
        // emitted asset external must fail the build rather than produce a browser-only UI.
        // No glob matching is needed (GHSA-vfj7-8cjw-p6xm has no patched braces release).
        for (const item of Object.values(bundle)) {
          if (item === document) continue
          const fileName = item.fileName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
          const reference = `(?:\\./|/)?${fileName}`
          let tag: RegExp
          let replacement: string
          if (item.type === 'chunk') {
            // Rolldown retains self references for dynamic imports already folded into this chunk.
            const externalImports = [...item.imports, ...item.dynamicImports].filter((name) => name !== item.fileName)
            if (externalImports.length > 0) {
              this.error(`CAVE cannot embed external JavaScript imports: ${externalImports.join(', ')}`)
            }
            tag = new RegExp(`<script\\b[^>]*\\bsrc=["']${reference}["'][^>]*>\\s*</script>`, 'gi')
            const code = item.code.replace(/<(\/?script|!--)/gi, '\\u003c$1')
            replacement = `<script type="module">${code}</script>`
          } else if (item.fileName.endsWith('.css')) {
            tag = new RegExp(`<link\\b[^>]*\\bhref=["']${reference}["'][^>]*>`, 'gi')
            const css = typeof item.source === 'string' ? item.source : new TextDecoder().decode(item.source)
            replacement = `<style>${css.replace(/<\/style/gi, '\\3c /style')}</style>`
          } else {
            this.error(`CAVE cannot embed an unexpected emitted asset: ${item.fileName}`)
          }

          let references = 0
          html = html.replace(tag, () => {
            references++
            return replacement
          })
          if (references !== 1) {
            this.error(`CAVE expected one HTML reference to ${item.fileName}, found ${references}.`)
          }
          delete bundle[item.fileName]
        }

        document.source = html
      },
    },
  }
}
