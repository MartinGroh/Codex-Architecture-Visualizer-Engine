const labelSeparators = new Set(['.', '/', '\\', ':', '-', ' ', '\t'])

/** Splits a code-oriented label at meaningful separators without changing its text. */
export function nodeLabelSegments(label: string): string[] {
  const segments: string[] = []
  let segmentStart = 0

  for (let index = 0; index < label.length; index += 1) {
    if (!labelSeparators.has(label[index])) continue
    segments.push(label.slice(segmentStart, index + 1))
    segmentStart = index + 1
  }

  if (segmentStart < label.length) {
    segments.push(label.slice(segmentStart))
  }

  return segments.length > 0 ? segments : [label]
}

/** Returns the longest row produced by the best two-row, separator-aware split. */
export function longestBalancedNodeLabelRow(label: string): number {
  const segments = nodeLabelSegments(label)
  if (segments.length === 1) {
    return Math.ceil(label.length / 2)
  }

  let leftLength = 0
  let longestRow = label.length
  for (let index = 0; index < segments.length - 1; index += 1) {
    leftLength += segments[index].length
    longestRow = Math.min(longestRow, Math.max(leftLength, label.length - leftLength))
  }

  return longestRow
}
