import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { readCaveInfo } from './api'
import { SettingsPage } from './SettingsPage'
import { useWorkspaceOverview } from './useWorkspaceOverview'

vi.mock('./api', () => ({ readCaveInfo: vi.fn() }))
vi.mock('./useWorkspaceOverview', () => ({ useWorkspaceOverview: vi.fn() }))

const storage = new Map<string, string>()

beforeEach(() => {
  storage.clear()
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
  })
  vi.stubGlobal('matchMedia', vi.fn().mockReturnValue({
    matches: false,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
  }))
  vi.mocked(readCaveInfo).mockResolvedValue({
    viewerProtocolVersion: 1,
    usage: {
      status: 'Unavailable', summary: null, daily: [], rateLimits: [],
      retrievedAtUtc: '2026-08-26T10:00:00Z', error: 'Not available in test.',
    },
  })
  vi.mocked(useWorkspaceOverview).mockReturnValue({
    hostName: 'NORWEGIANWOOD',
    workspaces: [],
    diagnostics: [],
    error: null,
    isLoading: false,
    refresh: vi.fn(),
  })
  window.history.replaceState({}, '', '/settings/service-probes')
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('SettingsPage', () => {
  it('saves the probe-link address choice and exposes the settings navigation', () => {
    render(<SettingsPage />)

    const machineName = screen.getByRole('radio', { name: /Machine name/i }) as HTMLInputElement
    const ipAddress = screen.getByRole('radio', { name: /IP address/i }) as HTMLInputElement
    expect(machineName.checked).toBe(true)
    expect(screen.getByRole('link', { name: /Service probes/i }).getAttribute('aria-current')).toBe('page')
    expect(screen.getByRole('link', { name: /Fleet setup/i }).getAttribute('href')).toBe('/settings/fleet-setup')
    expect(screen.getByRole('link', { name: 'Open machine settings' }).getAttribute('aria-current')).toBe('page')
    expect(screen.getByRole('link', { name: /http:\/\/norwegianwood/i })).toBeTruthy()

    fireEvent.click(ipAddress)

    expect(ipAddress.checked).toBe(true)
    expect(storage.get('cave.probe-link-address')).toBe('ip')
    expect(screen.getByText('Saved for this browser')).toBeTruthy()
    expect(screen.getByText('Waiting for a probe-reported IP address')).toBeTruthy()
  })

  it('shows an honest unavailable state for fleet setup', () => {
    window.history.replaceState({}, '', '/settings/fleet-setup')

    render(<SettingsPage />)

    expect(screen.getByRole('heading', { name: 'Fleet setup' })).toBeTruthy()
    expect(screen.getByText('Fleet discovery is not configured in this build.')).toBeTruthy()
    expect(screen.getByRole('link', { name: /Fleet setup/i }).getAttribute('aria-current')).toBe('page')
  })

  it('does not invent a machine name before workspace discovery reports one', () => {
    vi.mocked(useWorkspaceOverview).mockReturnValue({
      hostName: null,
      workspaces: [],
      diagnostics: [],
      error: null,
      isLoading: true,
      refresh: vi.fn(),
    })

    render(<SettingsPage />)

    expect(screen.getByText('Waiting for a probe-reported machine name')).toBeTruthy()
    expect(screen.queryByRole('link', { name: /cave-host/i })).toBeNull()
  })

  it('discloses when the preference can only be kept for the current page', () => {
    vi.stubGlobal('localStorage', {
      getItem: () => null,
      setItem: () => { throw new Error('storage denied') },
    })

    render(<SettingsPage />)
    fireEvent.click(screen.getByRole('radio', { name: /IP address/i }))

    expect(screen.getByText('Browser storage is unavailable. This choice lasts for this page only.')).toBeTruthy()
  })
})
