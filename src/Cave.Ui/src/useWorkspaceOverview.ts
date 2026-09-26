import { useCallback, useEffect, useState } from 'react'
import { readWorkspaceOverview } from './api'
import type { WorkspaceOverview } from './types'

const refreshIntervalMs = 2_000

export interface WorkspaceOverviewState {
  hostName: string | null
  workspaces: WorkspaceOverview[]
  diagnostics: string[]
  error: string | null
  isLoading: boolean
  refresh: (signal?: AbortSignal) => Promise<void>
}

/** Owns the machine-workspace catalog polling shared by the dashboard and project rail. */
export function useWorkspaceOverview(enabled = true): WorkspaceOverviewState {
  const [hostName, setHostName] = useState<string | null>(null)
  const [workspaces, setWorkspaces] = useState<WorkspaceOverview[]>([])
  const [diagnostics, setDiagnostics] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(enabled)

  const refresh = useCallback(async (signal?: AbortSignal) => {
    if (!enabled) return

    try {
      const snapshot = await readWorkspaceOverview(signal)
      setHostName(snapshot.hostName)
      setWorkspaces(snapshot.workspaces)
      setDiagnostics(snapshot.errors)
      setError(null)
    } catch (reason: unknown) {
      if (!signal?.aborted) {
        setError(reason instanceof Error ? reason.message : 'Unable to read the workspace catalog.')
      }
    } finally {
      if (!signal?.aborted) setIsLoading(false)
    }
  }, [enabled])

  useEffect(() => {
    if (!enabled) {
      setIsLoading(false)
      return undefined
    }

    const controller = new AbortController()
    void refresh(controller.signal)
    const interval = window.setInterval(() => void refresh(controller.signal), refreshIntervalMs)
    return () => {
      controller.abort()
      window.clearInterval(interval)
    }
  }, [enabled, refresh])

  return { hostName, workspaces, diagnostics, error, isLoading, refresh }
}
