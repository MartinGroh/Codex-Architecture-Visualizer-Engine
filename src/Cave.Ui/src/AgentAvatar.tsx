import type { CSSProperties } from 'react'
import type { AgentActivityPhase } from './types'

export interface AgentAvatarProps {
  phase: AgentActivityPhase | null
  active: boolean
  isSubagent?: boolean
  size?: number
  className?: string
}

/** Renders the canonical phase-aware agent glyph used by every live-activity surface. */
export function AgentAvatar({
  phase,
  active,
  isSubagent = false,
  size = 28,
  className = '',
}: AgentAvatarProps) {
  const visiblePhase = active ? phase ?? 'Working' : null
  const phaseToken = visiblePhase?.toLowerCase() ?? 'idle'
  const style = { '--agent-avatar-size': `${size}px` } as CSSProperties

  return (
    <span
      className={`agent-avatar agent-avatar--${phaseToken} ${active ? 'is-active' : 'is-idle'} ${isSubagent ? 'is-subagent' : 'is-main'} ${className}`.trim()}
      data-agent-phase={visiblePhase ?? 'Idle'}
      data-agent-role={isSubagent ? 'Subagent' : 'Main agent'}
      style={style}
      aria-hidden="true"
    >
      <svg
        viewBox="0 0 36 36"
        focusable="false"
        data-agent-avatar-silhouette={isSubagent ? 'helper-drone' : 'main-robot'}
      >
        <g className="agent-avatar__head">
          {isSubagent ? (
            <>
              <path className="agent-avatar__antenna" d="M13.2 8.3 10.5 4.5M22.8 8.3l2.7-3.8" />
              <circle className="agent-avatar__antenna-tip" cx="9.9" cy="3.7" r="1.25" />
              <circle className="agent-avatar__antenna-tip" cx="26.1" cy="3.7" r="1.25" />
              <path
                className="agent-avatar__face"
                d="m18 7.2 10.7 5.6v9.9L18 28.6 7.3 22.7v-9.9Z"
              />
              <path className="agent-avatar__ear" d="M7.3 14.2H5.1l-1.5 3.4L5.1 21h2.2M28.7 14.2h2.2l1.5 3.4-1.5 3.4h-2.2" />
            </>
          ) : (
            <>
              <path className="agent-avatar__antenna" d="M18 7V4" />
              <circle className="agent-avatar__antenna-tip" cx="18" cy="3.2" r="1.4" />
              <rect className="agent-avatar__face" x="6.5" y="8" width="23" height="19" rx="6" />
              <path className="agent-avatar__ear" d="M6.5 14H4.8v7h1.7M29.5 14h1.7v7h-1.7" />
            </>
          )}
          <g className="agent-avatar__eyes">
            <circle cx="13.5" cy="17" r="1.65" />
            <circle cx="22.5" cy="17" r="1.65" />
          </g>
          {visiblePhase === 'Reading' && (
            <g className="agent-avatar__glasses" data-agent-avatar-accessory="glasses">
              <rect x="9.3" y="13.4" width="8" height="6.9" rx="2.6" />
              <rect x="18.7" y="13.4" width="8" height="6.9" rx="2.6" />
              <path d="M17.3 16.2h1.4" />
            </g>
          )}
          <path className="agent-avatar__mouth" d="M14 22.3h8" />
        </g>
        {isSubagent ? (
          <>
            <path className="agent-avatar__body" d="M8.8 30c2.6-1.2 5.7-1.8 9.2-1.8s6.6.6 9.2 1.8l-2.5 2.6H11.3z" />
            <path className="agent-avatar__drone-skid" d="M13.2 34h9.6" />
          </>
        ) : (
          <path className="agent-avatar__body" d="M11 29.2c1.8-1.4 4.2-2.2 7-2.2s5.2.8 7 2.2v3.3H11z" />
        )}
      </svg>

      {visiblePhase === 'Thinking' && (
        <span className="agent-avatar__thoughts" data-agent-avatar-accessory="thoughts">
          <i /><i /><i />
        </span>
      )}
      {visiblePhase === 'Validating' && (
        <span className="agent-avatar__check" data-agent-avatar-accessory="check">✓</span>
      )}
      <span className="agent-avatar__role" />
    </span>
  )
}
