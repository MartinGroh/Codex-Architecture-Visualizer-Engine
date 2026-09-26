import { describe, expect, it } from 'vitest'
import {
  missingDocumentationSummary,
  nodeDocumentationPreview,
  nodeDocumentationSummary,
} from './nodeDocumentation'

describe('nodeDocumentationSummary', () => {
  it('preserves the complete canonical node summary', () => {
    const summary = 'Explains the complete responsibility of this architecture node.'

    expect(nodeDocumentationSummary({ description: summary })).toBe(summary)
  })

  it('uses an honest empty state when no documentation is available', () => {
    expect(nodeDocumentationSummary({ description: '   ' })).toBe(missingDocumentationSummary)
    expect(nodeDocumentationSummary({ description: null })).toBe(missingDocumentationSummary)
  })

  it('omits the repetitive empty state from compact graph cards', () => {
    expect(nodeDocumentationPreview({ description: '   ' })).toBeNull()
    expect(nodeDocumentationPreview({ description: null })).toBeNull()
    expect(nodeDocumentationPreview({ description: 'No summary available.' })).toBeNull()
    expect(nodeDocumentationPreview({ description: missingDocumentationSummary })).toBeNull()
    expect(nodeDocumentationPreview({ description: 'Source-backed summary.' })).toBe('Source-backed summary.')
  })
})
