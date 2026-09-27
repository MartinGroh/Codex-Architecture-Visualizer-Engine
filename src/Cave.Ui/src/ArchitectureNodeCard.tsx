import { agentDisplayName } from './agentIdentity'
import { Fragment } from 'react'
import {
  BookOpen,
  Brain,
  ChevronRight,
  CircleCheckBig,
  LockKeyhole,
  Pencil,
  Wrench,
  type LucideIcon,
} from 'lucide-react'
import { Handle, Position, type NodeProps } from '@xyflow/react'
import { AgentAvatar } from './AgentAvatar'
import type { ArchitectureFlowNode } from './graphPresentation'
import { nodeDocumentationPreview, nodeDocumentationSummary } from './nodeDocumentation'
import { nodeLabelSegments } from './nodeLabel'
import { architectureNodeVisual } from './nodeVisual'
import { isExternalPackage } from './projection'
import type { AgentActivityPhase } from './types'

const phaseIcons: Record<AgentActivityPhase, LucideIcon> = {
  Thinking: Brain,
  Reading: BookOpen,
  Editing: Pencil,
  Validating: CircleCheckBig,
  Working: Wrench,
}

export function ArchitectureNodeCard({
  data,
  selected,
}: NodeProps<ArchitectureFlowNode>) {
  const node = data.architecture
  const gitDelta = data.gitDelta
  const visual = architectureNodeVisual(node)
  const externalPackage = isExternalPackage(node)
  const Icon = visual.Icon
  const description = nodeDocumentationSummary(node)
  const descriptionPreview = nodeDocumentationPreview(node)
  const activeMainAgent = data.agentDecorations.some((decoration) => (
    decoration.state === 'Active' && !decoration.agent.isSubagent
  ))
  const activeSubagent = data.agentDecorations.some((decoration) => (
    decoration.state === 'Active' && decoration.agent.isSubagent
  ))
  const workedMainAgent = data.agentDecorations.some((decoration) => (
    decoration.state === 'Worked' && !decoration.agent.isSubagent
  ))
  const workedSubagent = data.agentDecorations.some((decoration) => (
    decoration.state === 'Worked' && decoration.agent.isSubagent
  ))
  const activeDecorations = data.agentDecorations.filter((decoration) => decoration.state === 'Active')
  const hasDeclaredScope = data.activities.some((activity) => activity.evidence === 'Declared')
  const hasRecentEdit = data.recentEdits.length > 0
  const hierarchy = data.hierarchy
  const changeLabel = gitDelta?.kind === 'Added'
    ? 'New'
    : gitDelta?.kind === 'Deleted'
      ? 'Deleted'
      : gitDelta?.kind === 'Mixed'
        ? 'Mixed'
        : 'Modified'

  return (
    <article
      className={`architecture-node architecture-node--${node.kind.toLowerCase()} ${selected ? 'is-selected' : ''} ${gitDelta === null ? 'is-unchanged' : `is-changed git-kind--${gitDelta.kind.toLowerCase()}`} ${activeMainAgent ? 'has-active-main-agent' : ''} ${activeSubagent ? 'has-active-subagent' : ''} ${workedMainAgent ? 'has-worked-main-agent' : ''} ${workedSubagent ? 'has-worked-subagent' : ''} ${hasRecentEdit ? 'has-residual-edit' : workedMainAgent || workedSubagent ? 'has-residual-observed' : ''} ${hasDeclaredScope ? 'has-declared-scope' : ''} ${hasRecentEdit ? 'has-recent-edit' : ''} ${hierarchy.isNested ? 'is-hierarchy-nested' : ''} ${hierarchy.isDimmed ? 'is-focus-dimmed' : ''} ${externalPackage ? 'is-external-package' : ''}`}
      title={description}
    >
      <Handle type="target" position={Position.Left} />
      <header>
        <span className={`node-icon ${visual.className}`} aria-hidden="true">
          <Icon size={19} strokeWidth={1.8} />
        </span>
        <span className="node-kind">{visual.label}</span>
        {externalPackage && (
          <span className="node-readonly" aria-label="Read-only external package" title="Read-only external package">
            <LockKeyhole size={10} strokeWidth={2.1} />
            read only
          </span>
        )}
        {gitDelta !== null && <span className="change-badge">{changeLabel}</span>}
        <span className={`evidence-dot evidence-dot--${node.categoryId ?? 'default'}`} />
        {hierarchy.canExpand && (
          <button
            className="hierarchy-toggle"
            type="button"
            aria-label={`${hierarchy.isExpanded ? 'Collapse' : 'Expand'} ${node.name} ${node.kind === 'Project' ? 'namespaces' : 'classes'}`}
            aria-expanded={hierarchy.isExpanded}
            title={`${hierarchy.isExpanded ? 'Hide' : 'Show'} ${node.kind === 'Project' ? 'namespaces in this project' : 'classes in this namespace'}`}
            onClick={(event) => {
              event.stopPropagation()
              hierarchy.onToggleExpansion?.(node.id)
            }}
          >
            <ChevronRight size={14} strokeWidth={2} />
          </button>
        )}
      </header>
      {activeDecorations.length > 0 && (
        <div className="agent-badges" aria-label={`${activeDecorations.length} active agent${activeDecorations.length === 1 ? '' : 's'} associated with this node`}>
          {activeDecorations.map((decoration) => {
            const agentName = agentDisplayName(decoration.agent)
            const phase = decoration.state === 'Active' ? decoration.agent.phase : null
            const PhaseIcon = phase === null ? null : phaseIcons[phase]
            return (
              <span
                key={decoration.agent.agentId}
                className={`agent-badge ${decoration.agent.isSubagent ? 'agent-badge--subagent' : 'agent-badge--main'} ${decoration.state === 'Active' ? 'is-active' : 'is-worked'} ${decoration.evidence === 'Declared' ? 'is-declared' : 'is-observed'}${phase === null ? '' : ` phase-${phase.toLowerCase()}`}`}
                title={`${agentName} · ${phase ?? (decoration.state === 'Active' ? 'Working' : 'Work finished or idle')} · ${decoration.evidence} evidence${decoration.agent.summary === null ? '' : ` · ${decoration.agent.summary}`}`}
              >
                <span className="agent-badge__icon" aria-hidden="true">
                  <AgentAvatar
                    phase={phase}
                    active={decoration.state === 'Active'}
                    isSubagent={decoration.agent.isSubagent}
                    size={decoration.state === 'Active' ? 28 : 17}
                  />
                </span>
                <span className="agent-badge__copy">
                  <span className="agent-badge__name">{agentName}</span>
                  <span className="agent-badge__phase">
                    {PhaseIcon !== null && <PhaseIcon size={11} strokeWidth={2.2} aria-hidden="true" />}
                    {phase ?? (decoration.state === 'Active' ? 'Working' : 'Done')}
                  </span>
                </span>
              </span>
            )
          })}
        </div>
      )}
      {(hasRecentEdit || workedMainAgent || workedSubagent) && activeDecorations.length === 0 && (
        <div className="node-activity-history" role="tooltip">
          <strong>Activity since last reset</strong>
          <ul>
            {data.recentEdits.slice(-4).reverse().map((edit) => (
              <li key={`${edit.agentId}:${edit.filePath}:${edit.observedAtUtc}`}>
                <b>Edited</b> {edit.filePath}
              </li>
            ))}
            {data.activities
              .filter((activity) => !data.recentEdits.some((edit) => edit.agentId === activity.agentId && edit.nodeIds.includes(node.id)))
              .slice(-4)
              .reverse()
              .map((activity) => (
                <li key={`${activity.agentId}:${activity.updatedAtUtc}:${activity.evidence}`}>
                  <b>{activity.evidence === 'Declared' ? 'Declared' : 'Observed'}</b> {activity.paths[0] ?? activity.evidence}
                </li>
              ))}
          </ul>
        </div>
      )}
      <strong className="node-title" title={node.name}>
        <span className="node-title__adaptive">
          {nodeLabelSegments(node.name).map((segment, index, segments) => (
            <Fragment key={`${index}:${segment}`}>
              {segment}
              {index < segments.length - 1 && <wbr />}
            </Fragment>
          ))}
        </span>
      </strong>
      {descriptionPreview !== null && <p>{descriptionPreview}</p>}
      <footer>
        <span className="node-category">{node.categoryId ?? 'unclassified'}</span>
        {gitDelta !== null && (
          <span className="node-delta" aria-label={`${gitDelta.additions} additions and ${gitDelta.deletions} deletions`}>
            <b>+{gitDelta.additions}</b> <i>−{gitDelta.deletions}</i>
          </span>
        )}
        <span className="node-signal">{externalPackage ? 'external' : 'exact'}</span>
      </footer>
      <Handle type="source" position={Position.Right} />
    </article>
  )
}
