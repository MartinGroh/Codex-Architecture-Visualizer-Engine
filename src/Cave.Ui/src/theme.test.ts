import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  caveThemeOptions,
  readThemePreference,
  resolveTheme,
  setEmbeddedHostTheme,
  subscribeTheme,
  themeColorScheme,
  themeLabel,
  writeThemePreference,
} from './theme'

describe('theme resolution', () => {
  beforeEach(() => {
    const values = new Map<string, string>()
    Object.defineProperty(window, 'localStorage', {
      configurable: true,
      value: {
        get length() { return values.size },
        clear: () => values.clear(),
        getItem: (key: string) => values.get(key) ?? null,
        key: (index: number) => [...values.keys()][index] ?? null,
        removeItem: (key: string) => { values.delete(key) },
        setItem: (key: string, value: string) => { values.set(key, value) },
      } satisfies Storage,
    })
  })

  it('uses the Codex host theme when the MCP App exposes one', () => {
    expect(resolveTheme('light', true)).toEqual({ theme: 'light', source: 'Codex' })
    expect(resolveTheme('dark', false)).toEqual({ theme: 'code-dark', source: 'Codex' })
  })

  it('uses the browser color scheme when no host theme exists', () => {
    expect(resolveTheme(undefined, true)).toEqual({ theme: 'code-dark', source: 'System' })
    expect(resolveTheme('unsupported', false)).toEqual({ theme: 'light', source: 'System' })
  })

  it('lets a manual preference override both Codex and the browser', () => {
    expect(resolveTheme('dark', true, 'light')).toEqual({ theme: 'light', source: 'Manual' })
    expect(resolveTheme('light', false, 'neon-night')).toEqual({ theme: 'neon-night', source: 'Manual' })
    expect(resolveTheme('light', false, 'code-dark')).toEqual({ theme: 'code-dark', source: 'Manual' })
  })

  it('exposes one named definition for each selectable palette', () => {
    expect(caveThemeOptions).toEqual([
      { id: 'light', label: 'Light', colorScheme: 'light' },
      { id: 'neon-night', label: 'Neon Night', colorScheme: 'dark' },
      { id: 'code-dark', label: 'Code Dark', colorScheme: 'dark' },
    ])
    expect(themeLabel('code-dark')).toBe('Code Dark')
    expect(themeColorScheme('neon-night')).toBe('dark')
  })

  it('persists manual preferences while automatic mode remains the default', () => {
    expect(readThemePreference()).toBe('auto')

    writeThemePreference('neon-night')
    expect(readThemePreference()).toBe('neon-night')

    writeThemePreference('code-dark')
    expect(readThemePreference()).toBe('code-dark')

    writeThemePreference('auto')
    expect(readThemePreference()).toBe('auto')
    expect(window.localStorage.getItem('cave.theme-preference')).toBeNull()
  })

  it('migrates the legacy dark preference to Neon Night', () => {
    window.localStorage.setItem('cave.theme-preference', 'dark')

    expect(readThemePreference()).toBe('neon-night')
  })

  it('reacts when the MCP Apps host context changes its theme', () => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({
        matches: false,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }),
    })
    setEmbeddedHostTheme('light')
    const listener = vi.fn()
    const unsubscribe = subscribeTheme(listener)

    setEmbeddedHostTheme('dark')

    expect(listener).toHaveBeenCalledWith({ theme: 'code-dark', source: 'Codex' })
    unsubscribe()
    setEmbeddedHostTheme(undefined)
  })

  it('keeps a manual preference when the Codex host changes', () => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({
        matches: false,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }),
    })
    setEmbeddedHostTheme('light')
    const listener = vi.fn()
    const unsubscribe = subscribeTheme(listener, 'code-dark')

    setEmbeddedHostTheme('dark')

    expect(listener).toHaveBeenCalledWith({ theme: 'code-dark', source: 'Manual' })
    unsubscribe()
    setEmbeddedHostTheme(undefined)
  })
})
