export type ProbeLinkAddressMode = 'name' | 'ip'

export interface ProbeLinkAddress {
  hostName: string
  ipAddress: string | null
}

export const probeLinkAddressOptions: ReadonlyArray<{
  id: ProbeLinkAddressMode
  label: string
  description: string
}> = [
  {
    id: 'name',
    label: 'Machine name',
    description: 'Readable links that use local DNS or Tailscale name resolution.',
  },
  {
    id: 'ip',
    label: 'IP address',
    description: 'Direct links that use the address reported by each service probe.',
  },
]

const probeLinkAddressPreferenceKey = 'cave.probe-link-address'

/** Reads the browser-local address format used by every generated service-probe link. */
export function readProbeLinkAddressMode(): ProbeLinkAddressMode {
  try {
    return window.localStorage.getItem(probeLinkAddressPreferenceKey) === 'ip'
      ? 'ip'
      : 'name'
  } catch {
    return 'name'
  }
}

/** Persists the address format for the browser that follows the generated links. */
export function writeProbeLinkAddressMode(mode: ProbeLinkAddressMode): boolean {
  try {
    window.localStorage.setItem(probeLinkAddressPreferenceKey, mode)
    return true
  } catch {
    // Sandboxed embedded hosts can reject storage; callers must describe the state as session-only.
    return false
  }
}

/** Rewrites a probe URL through the one canonical host-address preference. */
export function buildServiceProbeUrl(
  url: string,
  address: ProbeLinkAddress,
  mode: ProbeLinkAddressMode,
): string | null {
  const host = mode === 'ip' ? address.ipAddress : address.hostName
  if (host === null || host.trim().length === 0) return null

  const result = new URL(url)
  result.hostname = host.trim()
  return result.toString()
}

export function isIpAddress(value: string): boolean {
  return /^(?:\d{1,3}\.){3}\d{1,3}$/.test(value)
    || value.includes(':')
}
