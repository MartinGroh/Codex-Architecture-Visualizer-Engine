export const caveThemes = {
  light: { label: 'Light', colorScheme: 'light' },
  'neon-night': { label: 'Neon Night', colorScheme: 'dark' },
  'code-dark': { label: 'Code Dark', colorScheme: 'dark' },
} as const satisfies Record<string, { label: string; colorScheme: 'light' | 'dark' }>

export type CaveTheme = keyof typeof caveThemes
export type ThemePreference = 'auto' | CaveTheme
export type ThemeSource = 'Codex' | 'System' | 'Manual'

export const caveThemeOptions = (Object.keys(caveThemes) as CaveTheme[]).map((id) => ({
  id,
  ...caveThemes[id],
}))

const themePreferenceKey = 'cave.theme-preference'
const automaticDarkTheme: CaveTheme = 'code-dark'
const legacyDarkTheme: CaveTheme = 'neon-night'
const embeddedThemeListeners = new Set<() => void>()
let embeddedHostTheme: unknown

export interface ThemeState {
  theme: CaveTheme
  source: ThemeSource
}

/** Returns the browser color scheme represented by a CAVE palette. */
export function themeColorScheme(theme: CaveTheme): 'light' | 'dark' {
  return caveThemes[theme].colorScheme
}

/** Returns the user-facing name for a CAVE palette. */
export function themeLabel(theme: CaveTheme): string {
  return caveThemes[theme].label
}

/** Resolves an explicit preference before the Codex host or browser color scheme. */
export function resolveTheme(
  hostTheme: unknown,
  prefersDark: boolean,
  preference: ThemePreference = 'auto',
): ThemeState {
  if (preference !== 'auto') {
    return { theme: preference, source: 'Manual' }
  }

  if (hostTheme === 'light' || hostTheme === 'dark') {
    return {
      theme: hostTheme === 'dark' ? automaticDarkTheme : 'light',
      source: 'Codex',
    }
  }

  return { theme: prefersDark ? automaticDarkTheme : 'light', source: 'System' }
}

/** Reads the locally persisted theme preference, defaulting to automatic host resolution. */
export function readThemePreference(): ThemePreference {
  try {
    const stored = window.localStorage.getItem(themePreferenceKey)
    // Existing releases persisted "dark" for the palette now named Neon Night.
    if (stored === 'dark') return legacyDarkTheme
    return isCaveTheme(stored) ? stored : 'auto'
  } catch {
    return 'auto'
  }
}

/** Persists a manual theme preference; automatic mode remains the storage-free default. */
export function writeThemePreference(preference: ThemePreference): void {
  try {
    if (preference === 'auto') {
      window.localStorage.removeItem(themePreferenceKey)
    } else {
      window.localStorage.setItem(themePreferenceKey, preference)
    }
  } catch {
    // Storage can be unavailable in a sandboxed MCP App; the in-memory choice still applies.
  }
}

function isCaveTheme(value: unknown): value is CaveTheme {
  return typeof value === 'string'
    && Object.prototype.hasOwnProperty.call(caveThemes, value)
}

/** Reads the current theme from the explicit preference, MCP App host, or browser fallback. */
export function readTheme(preference: ThemePreference = readThemePreference()): ThemeState {
  return resolveTheme(
    embeddedHostTheme,
    window.matchMedia('(prefers-color-scheme: dark)').matches,
    preference,
  )
}

/** Applies theme context received through the standard MCP Apps host bridge. */
export function setEmbeddedHostTheme(theme: unknown): void {
  embeddedHostTheme = theme
  for (const listener of embeddedThemeListeners) listener()
}

/** Subscribes to both MCP Apps host-context changes and browser color-scheme changes. */
export function subscribeTheme(
  listener: (state: ThemeState) => void,
  preference: ThemePreference = 'auto',
): () => void {
  const colorScheme = window.matchMedia('(prefers-color-scheme: dark)')
  const refresh = () => listener(readTheme(preference))

  embeddedThemeListeners.add(refresh)
  colorScheme.addEventListener('change', refresh)

  return () => {
    embeddedThemeListeners.delete(refresh)
    colorScheme.removeEventListener('change', refresh)
  }
}
