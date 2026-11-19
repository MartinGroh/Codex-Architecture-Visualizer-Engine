import { useCallback, useEffect, useRef, useState } from 'react'
import { readWorkspaceOverview } from './api'
import type { WorkspaceOverview } from './types'

const activeRefreshMs = 2_000
const idleRefreshMs = 15_000
const hiddenRefreshMs = 60_000

export interface WorkspaceOverviewState {
  hostName: string | null
  workspaces: WorkspaceOverview[]
  diagnostics: string[]
  error: string | null
  isLoading: boolean
  refresh: (signal?: AbortSignal) => Promise<void>
}

/** Owns non-overlapping, activity-aware catalog polling shared by machine views. */
export function useWorkspaceOverview(enabled = true): WorkspaceOverviewState {
  const [hostName, setHostName] = useState<string | null>(null)
  const [workspaces, setWorkspaces] = useState<WorkspaceOverview[]>([])
  const [diagnostics, setDiagnostics] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [isLoading, setIsLoading] = useState(enabled)
  const active = useRef(false)
  const inFlight = useRef<{ promise: Promise<void>; signal?: AbortSignal } | null>(null)

  const refresh = useCallback((signal?: AbortSignal): Promise<void> => {
    if (!enabled || signal?.aborted) return Promise.resolve()
    if (inFlight.current !== null && !inFlight.current.signal?.aborted) return inFlight.current.promise
    const request = (async () => {
      try {
        const snapshot = await readWorkspaceOverview(signal)
        if (signal?.aborted) return
        active.current = snapshot.workspaces.some((workspace) => workspace.activeAgentCount > 0)
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
    })()
    inFlight.current = { promise: request, signal }
    void request.finally(() => { if (inFlight.current?.promise === request) inFlight.current = null })
    return request
  }, [enabled])

  useEffect(() => {
    if (!enabled) {
      setIsLoading(false)
      return undefined
    }

    const controller = new AbortController()
    let timer: number | undefined
    let running = false
    let wakePending = false
    const tick = async () => {
      if (controller.signal.aborted) return
      if (running) { wakePending = true; return }
      running = true
      await refresh(controller.signal)
      running = false
      if (controller.signal.aborted) return
      const delay = wakePending ? 0 : document.hidden ? hiddenRefreshMs : active.current ? activeRefreshMs : idleRefreshMs
      wakePending = false
      timer = window.setTimeout(() => void tick(), delay)
    }
    const wake = () => {
      if (document.hidden) return
      window.clearTimeout(timer)
      void tick()
    }
    void tick()
    window.addEventListener('focus', wake)
    document.addEventListener('visibilitychange', wake)
    return () => {
      controller.abort()
      window.clearTimeout(timer)
      window.removeEventListener('focus', wake)
      document.removeEventListener('visibilitychange', wake)
    }
  }, [enabled, refresh])

  return { hostName, workspaces, diagnostics, error, isLoading, refresh }
}
