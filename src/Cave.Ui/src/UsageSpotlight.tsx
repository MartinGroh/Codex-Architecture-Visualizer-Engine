import { Gauge } from 'lucide-react'
import type { CSSProperties } from 'react'
import type { CaveInfoSnapshot } from './types'
import { createUsagePresentation } from './usagePresentation'

export function UsageSpotlight({ info, error, onOpen }: {
  info: CaveInfoSnapshot | null
  error: string | null
  onOpen(): void
}) {
  const presentation = createUsagePresentation(info?.usage.rateLimits ?? [], new Date())
  return (
    <button
      className="usage-spotlight"
      type="button"
      aria-label={presentation === null
        ? 'Codex usage unavailable. Open usage details.'
        : `${presentation.remainingPercent}% Codex usage remaining. Open usage details.`}
      onClick={onOpen}
    >
      <Gauge size={16} aria-hidden="true" />
      {presentation === null ? (
        <span><strong>Usage</strong><small>{error === null ? 'Loading…' : 'Unavailable'}</small></span>
      ) : (
        <>
          <span><strong>{presentation.remainingPercent}% left</strong><small>{presentation.windowLabel}</small></span>
          <span className="usage-spotlight__burn"><strong>{presentation.burnLabel}</strong><small>{presentation.resetsLabel ?? 'Current pace'}</small></span>
          <i style={{ '--usage-used': `${100 - presentation.remainingPercent}%` } as CSSProperties} />
        </>
      )}
    </button>
  )
}
