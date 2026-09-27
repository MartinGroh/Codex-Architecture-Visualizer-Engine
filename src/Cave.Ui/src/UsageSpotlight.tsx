import { Gauge } from 'lucide-react'
import type { CSSProperties } from 'react'
import type { CaveInfoSnapshot } from './types'
import { createUsagePresentation } from './usagePresentation'

export function UsageSpotlight({ info, error, onOpen }: {
  info: CaveInfoSnapshot | null
  error: string | null
  onOpen(): void
}) {
  const usage = info?.usage
  const presentation = createUsagePresentation(usage?.rateLimits ?? [], new Date(), usage?.ordinaryUsageAllowed ?? null)
  const loading = info === null && error === null
  const diagnostic = error ?? usage?.error ?? null
  const blocked = usage?.ordinaryUsageAllowed === false
  return (
    <button
      className="usage-spotlight"
      type="button"
      aria-label={blocked ? 'Codex included usage blocked. Open usage details.' : presentation === null
        ? 'Codex usage unavailable. Open usage details.'
        : `${presentation.remainingPercent}% ${presentation.limitLabel} quota remaining. Open usage details.`}
      title={diagnostic ?? undefined}
      onClick={onOpen}
    >
      <Gauge size={16} aria-hidden="true" />
      {presentation === null ? (
        <span><strong>{blocked ? 'Included usage blocked' : 'Usage'}</strong><small>{loading ? 'Loading…' : 'Quota unavailable'}</small></span>
      ) : (
        <>
          <span><strong>{blocked ? 'Included usage blocked' : `${presentation.remainingPercent}% left`}</strong><small>{presentation.limitLabel} · {presentation.windowLabel}</small></span>
          <span className="usage-spotlight__burn"><strong>{error !== null ? 'Refresh unavailable' : presentation.burnLabel}</strong><small>{presentation.resetsLabel ?? 'Reset time unavailable'}</small></span>
          <i style={{ '--usage-used': `${100 - presentation.remainingPercent}%` } as CSSProperties} />
        </>
      )}
    </button>
  )
}
