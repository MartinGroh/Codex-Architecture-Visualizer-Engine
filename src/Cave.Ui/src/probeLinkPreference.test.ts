import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  buildServiceProbeUrl,
  readProbeLinkAddressMode,
  writeProbeLinkAddressMode,
} from './probeLinkPreference'

const storage = new Map<string, string>()

beforeEach(() => {
  storage.clear()
  vi.stubGlobal('localStorage', {
    getItem: (key: string) => storage.get(key) ?? null,
    setItem: (key: string, value: string) => storage.set(key, value),
  })
})

describe('probe link preference', () => {
  it('defaults to machine names and persists an IP choice for this browser', () => {
    expect(readProbeLinkAddressMode()).toBe('name')

    expect(writeProbeLinkAddressMode('ip')).toBe(true)

    expect(readProbeLinkAddressMode()).toBe('ip')
  })

  it('reports when browser storage rejects the preference', () => {
    vi.stubGlobal('localStorage', {
      getItem: () => null,
      setItem: () => { throw new Error('storage denied') },
    })

    expect(writeProbeLinkAddressMode('ip')).toBe(false)
    expect(readProbeLinkAddressMode()).toBe('name')
  })

  it('rewrites a probe link with the selected address while preserving its endpoint', () => {
    const source = 'http://cave-box:5098/probes/result-service?view=health'
    const address = { hostName: 'NORWEGIANWOOD', ipAddress: '100.96.42.7' }

    expect(buildServiceProbeUrl(source, address, 'name'))
      .toBe('http://norwegianwood:5098/probes/result-service?view=health')
    expect(buildServiceProbeUrl(source, address, 'ip'))
      .toBe('http://100.96.42.7:5098/probes/result-service?view=health')
  })

  it('does not invent an IP address when a probe has not reported one', () => {
    expect(buildServiceProbeUrl(
      'http://cave-box:5098/probes/result-service',
      { hostName: 'NORWEGIANWOOD', ipAddress: null },
      'ip',
    )).toBeNull()
  })
})
