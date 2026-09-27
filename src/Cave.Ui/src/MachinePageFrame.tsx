import { useEffect, useState, type ReactNode } from 'react'
import { Moon, Settings, Sun } from 'lucide-react'
import { readCaveInfo } from './api'
import { CaveLogoMark } from './CaveLogoMark'
import { InfoPanel } from './InfoPanel'
import type { CaveInfoSnapshot } from './types'
import { UsageSpotlight } from './UsageSpotlight'
import {
  caveThemeOptions,
  readTheme,
  readThemePreference,
  subscribeTheme,
  themeColorScheme,
  themeLabel,
  writeThemePreference,
  type ThemePreference,
} from './theme'

interface MachinePageFrameProps {
  className: string
  children: ReactNode
}

/** Owns the shared machine-page brand and browser theme presentation. */
export function MachinePageFrame({ className, children }: MachinePageFrameProps) {
  const [themePreference, setThemePreference] = useState<ThemePreference>(readThemePreference)
  const [themeState, setThemeState] = useState(() => readTheme(themePreference))
  const [info, setInfo] = useState<CaveInfoSnapshot | null>(null)
  const [infoError, setInfoError] = useState<string | null>(null)
  const [infoOpen, setInfoOpen] = useState(false)
  const automaticTheme = readTheme('auto')

  useEffect(() => {
    writeThemePreference(themePreference)
    setThemeState(readTheme(themePreference))
    return subscribeTheme(setThemeState, themePreference)
  }, [themePreference])

  useEffect(() => {
    const controller = new AbortController()
    let reading = false
    const refresh = () => {
      if (reading || controller.signal.aborted) return
      reading = true
      void readCaveInfo(controller.signal)
        .then((next) => {
          if (!controller.signal.aborted) {
            setInfo(next)
            setInfoError(null)
          }
        })
        .catch((reason: unknown) => {
          if (!controller.signal.aborted) {
            setInfoError(reason instanceof Error ? reason.message : 'Codex usage is unavailable.')
          }
        })
        .finally(() => { reading = false })
    }
    refresh()
    const interval = window.setInterval(refresh, 60_000)
    return () => { controller.abort(); window.clearInterval(interval) }
  }, [])

  return (
    <main className={className} data-theme={themeState.theme}>
      <header className="dashboard-topbar">
        <a className="dashboard-brand" href="/" aria-label="Open CAVE workspace overview">
          <span className="brand-glyph"><CaveLogoMark size={24} /></span>
          <span><strong>CAVE</strong><small>Machine workspace map</small></span>
        </a>
        <div className="dashboard-topbar__actions">
          <UsageSpotlight info={info} error={infoError} onOpen={() => setInfoOpen(true)} />
          <a
            className={`dashboard-settings-link ${window.location.pathname.startsWith('/settings') ? 'is-active' : ''}`}
            href="/settings/service-probes"
            aria-label="Open machine settings"
            aria-current={window.location.pathname.startsWith('/settings') ? 'page' : undefined}
          >
            <Settings size={16} /> <span>Settings</span>
          </a>
          <label className="theme-control" title="Auto follows the system color scheme in a browser.">
            {themeColorScheme(themeState.theme) === 'dark' ? <Moon size={14} /> : <Sun size={14} />}
            <select
              aria-label={`Color theme. Currently ${themeState.source} ${themeLabel(themeState.theme)}.`}
              value={themePreference}
              onChange={(event) => setThemePreference(event.target.value as ThemePreference)}
            >
              <option value="auto">Auto · {themeLabel(automaticTheme.theme)}</option>
              {caveThemeOptions.map((theme) => (
                <option key={theme.id} value={theme.id}>{theme.label}</option>
              ))}
            </select>
          </label>
        </div>
      </header>
      {children}
      {infoOpen && <InfoPanel info={info} error={infoError} onClose={() => setInfoOpen(false)} />}
    </main>
  )
}
