import { useMemo, useState } from 'react'
import {
  ArrowLeft,
  Check,
  ExternalLink,
  Network,
  RadioTower,
  ServerCog,
} from 'lucide-react'
import { MachinePageFrame } from './MachinePageFrame'
import {
  buildServiceProbeUrl,
  probeLinkAddressOptions,
  readProbeLinkAddressMode,
  writeProbeLinkAddressMode,
  type ProbeLinkAddressMode,
} from './probeLinkPreference'
import { useWorkspaceOverview } from './useWorkspaceOverview'

type SettingsSection = 'service-probes' | 'fleet-setup'
type SaveStatus = 'saved' | 'session-only' | null

export function SettingsPage() {
  const section = readSettingsSection(window.location.pathname)
  const { hostName } = useWorkspaceOverview()
  const [addressMode, setAddressMode] = useState<ProbeLinkAddressMode>(readProbeLinkAddressMode)
  const [saveStatus, setSaveStatus] = useState<SaveStatus>(null)

  const previewUrl = useMemo(() => {
    return buildServiceProbeUrl(
      new URL('/', window.location.href).toString(),
      {
        hostName: hostName ?? '',
        ipAddress: null,
      },
      addressMode,
    )
  }, [addressMode, hostName])

  const selectAddressMode = (mode: ProbeLinkAddressMode) => {
    setAddressMode(mode)
    setSaveStatus(writeProbeLinkAddressMode(mode) ? 'saved' : 'session-only')
  }

  return (
    <MachinePageFrame className="machine-settings-page">
      <section className="dashboard-content settings-content">
        <header className="settings-heading">
          <a href="/" className="settings-back"><ArrowLeft size={15} /> Projects</a>
          <h1>Machine settings</h1>
          <p>Configure how this browser works with the CAVE service and its monitored machines.</p>
        </header>

        <div className="settings-layout">
          <nav className="settings-navigation" aria-label="Settings sections">
            <a
              href="/settings/service-probes"
              className={section === 'service-probes' ? 'is-active' : ''}
              aria-current={section === 'service-probes' ? 'page' : undefined}
            >
              <RadioTower size={18} />
              <span><strong>Service probes</strong><small>Link and connection behavior</small></span>
            </a>
            <a
              href="/settings/fleet-setup"
              className={section === 'fleet-setup' ? 'is-active' : ''}
              aria-current={section === 'fleet-setup' ? 'page' : undefined}
            >
              <Network size={18} />
              <span><strong>Fleet setup</strong><small>Machine discovery and membership</small></span>
            </a>
          </nav>

          {section === 'service-probes'
            ? (
                <section className="settings-panel" aria-labelledby="service-probes-heading">
                  <div className="settings-panel__heading">
                    <span><ServerCog size={23} /></span>
                    <div>
                      <h2 id="service-probes-heading">Service probes</h2>
                      <p>Choose the address CAVE uses when it creates links to a probe dashboard or endpoint.</p>
                    </div>
                  </div>

                  <fieldset className="settings-fieldset">
                    <legend>Open probe links using</legend>
                    {probeLinkAddressOptions.map((option) => (
                      <label key={option.id} className={`settings-choice ${addressMode === option.id ? 'is-selected' : ''}`}>
                        <input
                          type="radio"
                          name="probe-link-address"
                          value={option.id}
                          checked={addressMode === option.id}
                          onChange={() => selectAddressMode(option.id)}
                        />
                        <span className="settings-choice__control" aria-hidden="true" />
                        <span>
                          <strong>{option.label}</strong>
                          <small>{option.description}</small>
                        </span>
                      </label>
                    ))}
                  </fieldset>

                  <div className="settings-preview" aria-live="polite">
                    <div>
                      <span>Address preview</span>
                      {previewUrl === null
                        ? (
                            <strong>
                              {addressMode === 'ip'
                                ? 'Waiting for a probe-reported IP address'
                                : 'Waiting for a probe-reported machine name'}
                            </strong>
                          )
                        : <a href={previewUrl}><span>{previewUrl}</span><ExternalLink size={15} /></a>}
                    </div>
                    <p>
                      {addressMode === 'name'
                        ? hostName
                          ? `Links use ${hostName}. This works best with local DNS or Tailscale DNS.`
                          : 'CAVE does not guess a machine name when the probe has not reported one.'
                        : 'Each link uses the IP address reported by that probe. CAVE does not guess an address when none is available.'}
                    </p>
                  </div>

                  <p className={`settings-saved ${saveStatus ? 'is-visible' : ''} ${saveStatus === 'session-only' ? 'is-warning' : ''}`} role="status">
                    <Check size={15} />
                    {saveStatus === 'session-only'
                      ? 'Browser storage is unavailable. This choice lasts for this page only.'
                      : 'Saved for this browser'}
                  </p>
                </section>
              )
            : (
                <section className="settings-panel" aria-labelledby="fleet-setup-heading">
                  <div className="settings-panel__heading">
                    <span><Network size={23} /></span>
                    <div>
                      <h2 id="fleet-setup-heading">Fleet setup</h2>
                      <p>Manage the machines that can report workspaces and service probes to this CAVE host.</p>
                    </div>
                  </div>
                  <div className="settings-empty-section">
                    <Network size={28} />
                    <div>
                      <strong>Fleet discovery is not configured in this build.</strong>
                      <p>The settings home is ready, but the current backend has no fleet registration contract yet. No synthetic machines or controls are shown.</p>
                    </div>
                  </div>
                  <a className="settings-inline-link" href="/settings/service-probes">
                    Configure service-probe links <ExternalLink size={14} />
                  </a>
                </section>
              )}
        </div>
      </section>
    </MachinePageFrame>
  )
}

function readSettingsSection(pathname: string): SettingsSection {
  return pathname.endsWith('/fleet-setup') ? 'fleet-setup' : 'service-probes'
}
