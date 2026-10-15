import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AgentStatisticsPanel } from './AgentStatisticsPanel'
import type { AgentStatisticsSnapshot, AgentStatisticsTotals } from './agentStatistics'

afterEach(cleanup)

const totals: AgentStatisticsTotals = {
  agentCount: 2, subagentCount: 1, taskCount: 1, observedMilliseconds: 60_000, completedMilliseconds: 120_000,
  inputTokens: null, cachedInputTokens: null, outputTokens: null, totalTokens: null,
  reportingAgents: 0, tokenCoverageComplete: false, staleUsageAgents: 0, durationCoverageComplete: false,
}
function snapshot(): AgentStatisticsSnapshot {
  return { schemaVersion: 1, scope: 'Overall', isSynthetic: true, trackingSinceUtc: '2026-10-15T10:00:00Z', updatedAtUtc: '2026-10-15T10:01:00Z', totals,
    groups: [], models: [], efforts: [], projects: [], tasks: [], sharedAgentCount: 0, error: null,
    families: [{ id: 'sol', label: 'Sol', model: 'sol', reasoningEffort: null, totals }] }
}

describe('scoped statistics', () => {
  it('shows missing usage and partial coverage without manufacturing a percentage or zero token count', async () => {
    const reader = vi.fn().mockResolvedValue(snapshot())
    render(<AgentStatisticsPanel title="Project statistics" reader={reader} defaultOpen />)
    await waitFor(() => expect(screen.getByText('Sol')).toBeTruthy())
    expect(screen.getByText(/Task token counts are unavailable/)).toBeTruthy()
    expect(screen.getByText('Synthetic demo statistics.')).toBeTruthy()
    expect(screen.getByRole('table').textContent).toContain('Unavailable')
    expect(screen.getByRole('table').textContent).not.toContain('0.0%')
    fireEvent.change(screen.getByLabelText('Compare'), { target: { value: 'observedMilliseconds' } })
    expect(screen.getByRole('table').textContent).toContain('1m 0s')
    expect(screen.getByRole('table').textContent).toContain('100.0%')
  })

  it('preserves genuine reported zero values while reporting stale usage', async () => {
    const data = snapshot()
    data.totals = { ...totals, inputTokens: 0, outputTokens: 0, totalTokens: 0, cachedInputTokens: 0, reportingAgents: 1, staleUsageAgents: 1 }
    data.families[0].totals = data.totals
    render(<AgentStatisticsPanel reader={vi.fn().mockResolvedValue(data)} defaultOpen />)
    await waitFor(() => expect(screen.getByText('Sol')).toBeTruthy())
    expect(screen.getByRole('table').textContent).toContain('0')
    expect(screen.getByText(/retained, stale usage/)).toBeTruthy()
    expect(screen.queryByText(/Task token counts are unavailable/)).toBeNull()
  })

  it('does not collect or request data while the disclosure is closed', () => {
    const reader = vi.fn().mockResolvedValue(snapshot())
    render(<AgentStatisticsPanel reader={reader} />)
    expect(reader).not.toHaveBeenCalled()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('reports a failed read explicitly and clears its old comparison', async () => {
    render(<AgentStatisticsPanel reader={vi.fn().mockRejectedValue(new Error('Source unavailable'))} defaultOpen />)
    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('Source unavailable'))
    expect(screen.queryByRole('table')).toBeNull()
  })
})
