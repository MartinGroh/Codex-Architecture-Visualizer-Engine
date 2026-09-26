import { describe, expect, it } from 'vitest'
import { longestBalancedNodeLabelRow, nodeLabelSegments } from './nodeLabel'

describe('nodeLabelSegments', () => {
  it('offers semantic wrap points for dotted identifiers', () => {
    expect(nodeLabelSegments('Cave.Infrastructure.CodeGraph'))
      .toEqual(['Cave.', 'Infrastructure.', 'CodeGraph'])
  })

  it('preserves path and compound-name separators', () => {
    expect(nodeLabelSegments('src/Cave.Ui\\App-main'))
      .toEqual(['src/', 'Cave.', 'Ui\\', 'App-', 'main'])
  })

  it('keeps an indivisible label intact for the CSS overflow fallback', () => {
    expect(nodeLabelSegments('Application')).toEqual(['Application'])
  })

  it('measures the best complete two-row split without changing the label', () => {
    expect(longestBalancedNodeLabelRow('HammerCache.Service')).toBe(12)
    expect(longestBalancedNodeLabelRow('Microsoft.Extensions.Hosting.WindowsServices')).toBe(23)
    expect(longestBalancedNodeLabelRow('Project with a descriptive name')).toBe(16)
    expect(longestBalancedNodeLabelRow('IndivisibleIdentifier')).toBe(11)
  })
})
