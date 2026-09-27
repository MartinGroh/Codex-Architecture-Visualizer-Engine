import { BarChart3, Info, X } from 'lucide-react'
import type { CaveInfoSnapshot } from './types'

const number = new Intl.NumberFormat()

export function InfoPanel({
  info,
  error,
  onClose,
}: {
  info: CaveInfoSnapshot | null
  error: string | null
  onClose(): void
}) {
  const usage = info?.usage
  return (
    <aside className="info-panel" aria-label="CAVE information">
      <header>
        <span><Info size={15} /> Information</span>
        <button type="button" aria-label="Close information" onClick={onClose}><X size={15} /></button>
      </header>
      <section>
        <span className="panel-eyebrow">Runtime</span>
        <h2>CAVE</h2>
        <p>Viewer protocol {info?.viewerProtocolVersion ?? '…'} · embedded MCP Apps and browser viewer</p>
      </section>
      <section>
        <span className="panel-eyebrow"><BarChart3 size={12} /> Codex usage</span>
        {error !== null ? (
          <p className="panel-error">{error}</p>
        ) : usage === undefined ? (
          <p>Loading account usage…</p>
        ) : usage.status === 'Unavailable' ? (
          <p className="panel-error">{usage.error ?? 'Codex account usage is unavailable.'}</p>
        ) : (
          <>
            {usage.error !== null && <p className="panel-error" role="status">{usage.error}</p>}
            {usage.ordinaryUsageAllowed === false && <p>Included Codex usage is currently blocked.</p>}
            {usage.rateLimits.length > 0 && <dl className="usage-grid" aria-label="Account quota windows">
              {usage.rateLimits.map((window) => <div key={`${window.limitId}:${window.window}`}>
                <dt>{window.limitName ?? window.limitId} · {window.window}{window.windowDurationMinutes === null ? '' : ` · ${window.windowDurationMinutes} minutes`}</dt>
                <dd>{window.usedPercent === null ? 'Unavailable' : `${window.usedPercent}% used`}</dd>
              </div>)}
            </dl>}
            {usage.summary === null ? <p>Token activity is unavailable.</p> : <>
            <dl className="usage-grid">
              <div><dt>Lifetime tokens</dt><dd>{formatCount(usage.summary.lifetimeTokens)}</dd></div>
              <div><dt>Peak day</dt><dd>{formatCount(usage.summary.peakDailyTokens)}</dd></div>
              <div><dt>Current streak</dt><dd>{formatCount(usage.summary.currentStreakDays, ' days')}</dd></div>
              <div><dt>Longest streak</dt><dd>{formatCount(usage.summary.longestStreakDays, ' days')}</dd></div>
              <div><dt>Longest turn</dt><dd>{formatDuration(usage.summary.longestRunningTurnSeconds)}</dd></div>
            </dl>
            </>}
            <div className="usage-days">
              {usage.daily.slice(-14).map((day) => {
                const peak = Math.max(1, ...usage.daily.slice(-14).map((item) => item.tokens))
                return (
                  <span key={day.date} title={`${day.date}: ${number.format(day.tokens)} tokens`}>
                    <i style={{ height: `${Math.max(4, day.tokens / peak * 100)}%` }} />
                  </span>
                )
              })}
            </div>
            <small>Recent daily usage · refreshed {new Date(usage.retrievedAtUtc).toLocaleTimeString()}</small>
          </>
        )}
      </section>
    </aside>
  )
}

function formatCount(value: number | null, suffix = ''): string {
  return value === null ? 'Unavailable' : `${number.format(value)}${suffix}`
}

function formatDuration(seconds: number | null): string {
  if (seconds === null) return 'Unavailable'
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  return `${minutes}m ${seconds % 60}s`
}
