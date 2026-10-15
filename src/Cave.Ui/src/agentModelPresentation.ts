import type { AgentModelConfiguration } from './types'

export type AgentModelFamily = 'luna' | 'terra' | 'sol' | 'astra' | 'unknown'

/** Family is a model-identifier token, never an agent name or a subjective intelligence score. */
export function agentModelFamily(model: string | null | undefined): AgentModelFamily {
  return model?.toLowerCase().match(/(?:^|[-_\s])(luna|terra|sol|astra)(?:$|[-_\s])/)?.[1] as AgentModelFamily | undefined ?? 'unknown'
}

const efforts: Record<string, { label: string; short: string }> = {
  none: { label: 'None', short: '0' }, minimal: { label: 'Minimal', short: 'MIN' },
  low: { label: 'Low', short: 'L' }, medium: { label: 'Medium', short: 'M' },
  high: { label: 'High', short: 'H' }, xhigh: { label: 'Extra high', short: 'XH' },
  max: { label: 'Max', short: 'MAX' }, ultra: { label: 'Ultra', short: 'U' },
}

export function effortLabel(effort: string | null | undefined): string {
  return effort ? efforts[effort.toLowerCase()]?.label ?? effort : 'Unknown'
}

export function effortShort(effort: string | null | undefined): string {
  return effort ? efforts[effort.toLowerCase()]?.short ?? '?' : '?'
}

export function configurationLabel(configuration: AgentModelConfiguration | null | undefined): string {
  return `Configured model: ${configuration?.model ?? 'Unknown'}. Thinking effort: ${effortLabel(configuration?.reasoningEffort)}`
}

/** Formats recognized source identifiers without hiding their exact value in the tooltip. */
export function modelDisplayName(model: string | null | undefined): string {
  const match = model?.match(/^gpt-([\d.]+)-(luna|terra|sol|astra)$/i)
  return match ? `${match[2][0].toUpperCase()}${match[2].slice(1)} ${match[1]}` : model ?? 'Model unknown'
}
