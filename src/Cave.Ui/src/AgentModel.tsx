import { Earth, Galaxy, HelpCircle, Moon, Sun } from 'lucide-react'
import type { AgentModelConfiguration } from './types'
import './AgentModel.css'

import { agentModelFamily, configurationLabel, effortLabel, effortShort, modelDisplayName } from './agentModelPresentation'

export function ModelFamilyIcon({ model, size = 14 }: { model: string | null | undefined; size?: number }) {
  const family = agentModelFamily(model)
  const Icon = { luna: Moon, terra: Earth, sol: Sun, astra: Galaxy, unknown: HelpCircle }[family]
  return <Icon size={size} strokeWidth={1.8} aria-hidden="true" data-model-family={family} />
}

/** The same exact-version and effort copy accompanies icons throughout the agent views. */
export function AgentModelLabel({ configuration }: { configuration: AgentModelConfiguration | null | undefined }) {
  const family = agentModelFamily(configuration?.model)
  return <span className={`agent-model-label model-family-${family}`} title={configurationLabel(configuration)}>
    <ModelFamilyIcon model={configuration?.model} />
    <span>{modelDisplayName(configuration?.model)}</span>
    <abbr title={`Configured thinking effort: ${effortLabel(configuration?.reasoningEffort)}`}>{effortShort(configuration?.reasoningEffort)}</abbr>
  </span>
}
