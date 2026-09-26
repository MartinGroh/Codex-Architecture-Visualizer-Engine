import { Boxes, Focus } from 'lucide-react'

export type MobileGraphScope = 'Work' | 'Architecture'

interface MobileGraphScopeToggleProps {
  scope: MobileGraphScope
  onChange: (scope: MobileGraphScope) => void
}

/** Switches the standalone phone canvas between the active branch and the full project map. */
export function MobileGraphScopeToggle({ scope, onChange }: MobileGraphScopeToggleProps) {
  const showsWork = scope === 'Work'
  const label = showsWork ? 'Show full architecture' : 'Show active work'

  return (
    <button
      className="mobile-graph-scope-toggle"
      type="button"
      aria-label={label}
      aria-pressed={!showsWork}
      title={label}
      onClick={() => onChange(showsWork ? 'Architecture' : 'Work')}
    >
      {showsWork ? <Boxes size={16} /> : <Focus size={16} />}
      <span>{showsWork ? 'Map' : 'Work'}</span>
    </button>
  )
}
