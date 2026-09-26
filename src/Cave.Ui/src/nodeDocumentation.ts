import type { ArchitectureNode } from './types'

export const missingDocumentationSummary = 'No documentation summary is available for this node.'

/** Returns the canonical node documentation or an explicit, non-invented empty-state message. */
export function nodeDocumentationSummary(
  node: Pick<ArchitectureNode, 'description'>,
): string {
  return node.description?.trim() || missingDocumentationSummary
}

/** Returns source-backed card copy without repeating the detail panel's missing-documentation message. */
export function nodeDocumentationPreview(
  node: Pick<ArchitectureNode, 'description'>,
): string | null {
  const summary = node.description?.trim()
  if (!summary
      || summary === missingDocumentationSummary
      || summary.toLowerCase() === 'no summary available.') {
    return null
  }

  return summary
}
