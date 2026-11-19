import { readAgentChatMarkdown } from './agentChatOutputIndex'
import { parseCodexText } from './codexTextFormat'

export type LongTextFormat = 'plain' | 'markdown' | 'codex'

const excerptLimit = 240
const excerptSourceLimit = 4096

export function exceedsReadingBudget(source: string, format: LongTextFormat, compact = false): boolean {
  const lineWidth = compact ? 48 : 64
  const lineBudget = compact ? 3 : 7
  return source.length > lineWidth * lineBudget
    || /\S{73}/u.test(source)
    || source.split(/\r\n?|\n/u).reduce((lines, line) => {
      if (format !== 'plain' && /^ {0,3}(?:`{3,}|~{3,})/u.test(line)) return lines
      return lines + Math.max(1, Math.ceil(line.trim().length / lineWidth))
    }, 0) > lineBudget
}

/** Uses one source-based reading budget and counts whitespace-delimited source words. */
export function getLongTextPreview(text: string, format: LongTextFormat = 'plain', visibleText?: string) {
  const source = text.trim()
  return {
    excerpt: getLongTextExcerpt(source, format, visibleText),
    isLong: exceedsReadingBudget(source, format),
    wordCount: countLongTextWords(source),
  }
}

/** Counts whitespace-delimited words in the captured source. */
export function countLongTextWords(text: string): number {
  const source = text.trim()
  return source ? source.split(/\s+/u).length : 0
}

export function getLongTextExcerpt(source: string, format: LongTextFormat, visibleText?: string) {
  const prefix = source.slice(0, excerptSourceLimit)
  const plain = (visibleText ?? (format === 'plain' ? prefix : markdownExcerpt(prefix, format === 'codex', prefix.length < source.length)))
    .replace(/\s+/gu, ' ').trim()
  if (plain.length <= excerptLimit) return plain

  const slice = plain.slice(0, excerptLimit).replace(/[\uD800-\uDBFF]$/u, '')
  const lastSpace = slice.lastIndexOf(' ')
  return (lastSpace > excerptLimit * .75 ? slice.slice(0, lastSpace) : slice) + '…'
}

function markdownExcerpt(source: string, attributed: boolean, clipped: boolean): string {
  if (!attributed) return readAgentChatMarkdown(source).visibleText

  // A bounded slice can end inside a block opener. Leave that partial source to the full reader.
  if (clipped) source = source.replace(/(?:^|\n) {0,3}::[^\n]*$/u, '')
  return parseCodexText(source).map((part) => {
    if (part.kind === 'comment') return part.attributes.title + ' ' + readAgentChatMarkdown(part.attributes.body, true).visibleText
    if (part.kind === 'thread') return 'Created chat'
    if (part.kind === 'writing') return [part.attributes.subject, readAgentChatMarkdown(part.text, true).visibleText].filter(Boolean).join(' ')
    return readAgentChatMarkdown(part.text, true).visibleText
  }).join(' ')
}
