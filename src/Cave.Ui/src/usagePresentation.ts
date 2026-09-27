import type { CodexRateLimitWindow } from './types'

export interface UsagePresentation {
  remainingPercent: number
  burnLabel: string
  windowLabel: string
  resetsLabel: string | null
  limitLabel: string
  ordinaryUsageAllowed: boolean | null
}

export function createUsagePresentation(
  windows: CodexRateLimitWindow[],
  now: Date,
  ordinaryUsageAllowed: boolean | null = null,
): UsagePresentation | null {
  // Both included-quota windows constrain the account. Other metered buckets
  // cannot stand in for the Codex quota, and percentages do not prove access.
  const window = windows.filter((item) => item.limitId === 'codex')
    .sort((left, right) => right.usedPercent - left.usedPercent
      || left.window.localeCompare(right.window))[0]
  if (window === undefined) return null
  const used = Math.max(0, Math.min(100, window.usedPercent))
  const durationMinutes = window.windowDurationMinutes
  const resetMs = window.resetsAtUtc === null ? null : new Date(window.resetsAtUtc).getTime()
  const remainingMinutes = resetMs === null ? null : Math.max(0, (resetMs - now.getTime()) / 60_000)
  const elapsedMinutes = durationMinutes === null || remainingMinutes === null
    ? null
    : Math.max(1, durationMinutes - remainingMinutes)
  const useDays = (durationMinutes ?? 0) > 1_440
  const elapsedUnits = elapsedMinutes === null ? null : elapsedMinutes / (useDays ? 1_440 : 60)
  const burn = elapsedUnits === null ? null : used / Math.max(elapsedUnits, 1 / 60)
  return {
    remainingPercent: Math.round(100 - used),
    burnLabel: burn === null
      ? `${used.toFixed(0)}% used`
      : `Avg ${burn < 10 ? burn.toFixed(1) : burn.toFixed(0)}%/${useDays ? 'day' : 'hr'}`,
    windowLabel: durationMinutes === null ? 'Current window' : formatWindow(durationMinutes),
    limitLabel: `${window.limitName ?? 'Codex'} ${window.window}`,
    ordinaryUsageAllowed,
    resetsLabel: resetMs === null
      ? null
      : `Resets ${new Date(resetMs).toLocaleString([], { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })}`,
  }
}

function formatWindow(minutes: number): string {
  if (minutes % 10_080 === 0) return `${minutes / 10_080} week window`
  if (minutes % 1_440 === 0) return `${minutes / 1_440} day window`
  if (minutes % 60 === 0) return `${minutes / 60} hour window`
  return `${minutes} min window`
}
