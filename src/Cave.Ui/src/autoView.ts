import { projectArchitecture, type ProjectionLevel } from './projection'
import type {
  AgentNodeActivity,
  AgentActivityPhase,
  ArchitectureNode,
  ArchitectureSnapshot,
} from './types'

export interface AutoViewPlan {
  signature: string
  projectionLevel: ProjectionLevel
  activeNodeIds: ReadonlySet<string>
  expandedNodeIds: ReadonlySet<string>
  revealedNodeIds: ReadonlySet<string>
  focusNodeIds: ReadonlySet<string>
  contextNodeIds: ReadonlySet<string>
  fitNodeIds: ReadonlySet<string>
  dominantPhase: AgentActivityPhase | null
}

export type AutoViewStatus = 'Idle' | 'Settling' | 'Following' | 'PausedByUser'

export interface AutoViewState {
  status: AutoViewStatus
  committedPlan: AutoViewPlan | null
  candidatePlan: AutoViewPlan | null
  candidateSince: number | null
  committedAt: number | null
  instructionId: string | null
  cameraRevision: number
}

export interface AutoViewOptions {
  hideTests?: boolean
  hideExternalPackages?: boolean
  maxRevealedNodes?: number
  maxDetailedFocusNodes?: number
}

const defaultRevealBudget = 18
const defaultDetailedFocusBudget = 6
const minimumPlanDwellMs = 1_500

const phasePriority: Record<AgentActivityPhase, number> = {
  Thinking: 1,
  Reading: 2,
  Working: 3,
  Validating: 4,
  Editing: 5,
}

/** Creates a deterministic inactive controller state. */
export function createAutoViewState(): AutoViewState {
  return {
    status: 'Idle',
    committedPlan: null,
    candidatePlan: null,
    candidateSince: null,
    committedAt: null,
    instructionId: null,
    cameraRevision: 0,
  }
}

/**
 * Derives one evidence-backed Auto presentation plan from the canonical snapshot.
 * It never guesses a child for project-level or unmapped activity.
 */
export function deriveAutoViewPlan(
  snapshot: ArchitectureSnapshot,
  options: AutoViewOptions = {},
): AutoViewPlan | null {
  const nodesById = new Map(snapshot.graph.nodes.map((node) => [node.id, node]))
  const allowedProjectIds = new Set(projectArchitecture(snapshot, 'Projects', {
    hideTests: options.hideTests,
    hideExternalPackages: options.hideExternalPackages,
  }).graph.nodes.filter((node) => node.kind === 'Project').map((node) => node.id))
  const isAllowed = (node: ArchitectureNode): boolean => {
    const project = ancestorOrSelfOfKind(node, 'Project', nodesById)
    return project === undefined || allowedProjectIds.has(project.id)
  }

  const activeAgents = snapshot.activity.agents.filter((agent) => agent.state === 'Active')
  const activeAgentIds = new Set(activeAgents.map((agent) => agent.agentId))
  const latestActivity = latestActivityBatch(snapshot.activity.nodes, activeAgentIds)
  const activeNodes = [...new Set(latestActivity.map((activity) => activity.nodeId))]
    .map((nodeId) => nodesById.get(nodeId))
    .filter((node): node is ArchitectureNode => node !== undefined && isAllowed(node))
    .sort((left, right) => left.id.localeCompare(right.id))

  if (activeNodes.length === 0) return null

  const activeNodeIds = new Set(activeNodes.map((node) => node.id))
  const projectRepresentatives = uniqueNodes(activeNodes.map((node) => (
    ancestorOrSelfOfKind(node, 'Project', nodesById) ?? node
  )))
  const detailedFocusBudget = Math.max(
    1,
    Math.floor(options.maxDetailedFocusNodes ?? defaultDetailedFocusBudget),
  )
  const focusCandidates = projectRepresentatives.length > 1 || activeNodes.length > detailedFocusBudget
    ? projectRepresentatives
    : activeNodes
  const onlyArchitectureGroups = focusCandidates.every((node) => node.kind === 'ArchitectureGroup')
  const projectionLevel: ProjectionLevel = onlyArchitectureGroups ? 'System' : 'Projects'
  const expandedNodeIds = new Set<string>()
  const revealedNodeIds = new Set<string>()
  const focusNodeIds = new Set<string>()
  const contextNodeIds = new Set<string>()
  const revealBudget = Math.max(0, Math.floor(options.maxRevealedNodes ?? defaultRevealBudget))

  const addPath = (nodeId: string, optional: boolean): boolean => {
    const path = visiblePath(nodeId, projectionLevel, nodesById)
    const additionalReveals = path.filter((node) => (
      !isBaseVisible(node, projectionLevel) && !revealedNodeIds.has(node.id)
    ))
    if (optional && revealedNodeIds.size + additionalReveals.length > revealBudget) {
      return false
    }

    for (const node of path) {
      focusNodeIds.add(node.id)
      if (!isBaseVisible(node, projectionLevel)) {
        revealedNodeIds.add(node.id)
      }
    }
    for (let index = 0; index < path.length - 1; index += 1) {
      const node = path[index]
      if (node.kind === 'Project' || node.kind === 'Namespace') {
        expandedNodeIds.add(node.id)
      }
    }
    return true
  }

  for (const node of focusCandidates) {
    addPath(node.id, false)
  }

  const activeBranchIds = new Set([...focusNodeIds].filter((nodeId) => {
    const node = nodesById.get(nodeId)
    return node?.kind === 'Project' || node?.kind === 'Namespace'
  }))
  if (focusCandidates === activeNodes) {
    const instructionAt = parseTimestamp(snapshot.activity.latestInstruction?.observedAtUtc)
    const recentContextNodeIds = snapshot.activity.recentEdits
      .filter((edit) => {
        if (instructionAt === null) return true
        const editAt = parseTimestamp(edit.observedAtUtc)
        return editAt !== null && editAt >= instructionAt
      })
      .sort((left, right) => (
        right.observedAtUtc.localeCompare(left.observedAtUtc)
        || left.filePath.localeCompare(right.filePath)
      ))
      .flatMap((edit) => edit.nodeIds)

    for (const nodeId of recentContextNodeIds) {
      if (focusNodeIds.has(nodeId)) continue
      const node = nodesById.get(nodeId)
      if (node === undefined || !isAllowed(node) || !hasAncestorIn(node, activeBranchIds, nodesById)) {
        continue
      }
      if (addPath(nodeId, true)) {
        contextNodeIds.add(nodeId)
      }
    }
  }

  const activeActivityAgentIds = new Set(latestActivity.map((activity) => activity.agentId))
  const dominantPhase = activeAgents
    .filter((agent) => activeActivityAgentIds.has(agent.agentId))
    .map((agent) => agent.phase)
    .filter((phase): phase is AgentActivityPhase => phase !== null)
    .sort((left, right) => phasePriority[right] - phasePriority[left])[0] ?? null
  const fitNodeIds = new Set([...focusNodeIds, ...contextNodeIds])
  const instructionId = snapshot.activity.latestInstruction?.id ?? null
  const signature = JSON.stringify({
    instructionId,
    projectionLevel,
    activeNodeIds: sorted(activeNodeIds),
    expandedNodeIds: sorted(expandedNodeIds),
    revealedNodeIds: sorted(revealedNodeIds),
    focusNodeIds: sorted(focusNodeIds),
    contextNodeIds: sorted(contextNodeIds),
  })

  return {
    signature,
    projectionLevel,
    activeNodeIds,
    expandedNodeIds,
    revealedNodeIds,
    focusNodeIds,
    contextNodeIds,
    fitNodeIds,
    dominantPhase,
  }
}

/** Reconciles observed plans without committing short-lived activity churn. */
export function reconcileAutoViewState(
  state: AutoViewState,
  plan: AutoViewPlan | null,
  instructionId: string | null,
  now: number,
): AutoViewState {
  const instructionChanged = state.instructionId !== instructionId
  const current = instructionChanged && state.status === 'PausedByUser'
    ? { ...state, status: state.committedPlan === null ? 'Idle' as const : 'Following' as const }
    : state

  if (!instructionChanged && current.status === 'PausedByUser') return current
  if (plan === null) {
    return {
      ...current,
      status: current.committedPlan === null ? 'Idle' : 'Following',
      candidatePlan: null,
      candidateSince: null,
      instructionId,
    }
  }
  if (current.committedPlan?.signature === plan.signature) {
    return {
      ...current,
      status: 'Following',
      candidatePlan: null,
      candidateSince: null,
      instructionId,
    }
  }

  const candidateChanged = current.candidatePlan?.signature !== plan.signature
  const candidateSince = candidateChanged ? now : current.candidateSince ?? now
  const settleAt = candidateSince + autoViewSettleDelayMs(plan.dominantPhase)
  const dwellAt = plan.dominantPhase === 'Editing' || current.committedAt === null
    ? now
    : current.committedAt + minimumPlanDwellMs
  if (now < Math.max(settleAt, dwellAt)) {
    return {
      ...current,
      status: 'Settling',
      candidatePlan: plan,
      candidateSince,
      instructionId,
    }
  }

  return {
    ...current,
    status: 'Following',
    committedPlan: plan,
    candidatePlan: null,
    candidateSince: null,
    committedAt: now,
    instructionId,
    cameraRevision: current.cameraRevision + 1,
  }
}

/** Pauses camera following without discarding the last stable plan. */
export function pauseAutoView(state: AutoViewState): AutoViewState {
  return {
    ...state,
    status: 'PausedByUser',
    candidatePlan: null,
    candidateSince: null,
  }
}

/** Resumes the last stable plan; the next observation may replace it. */
export function resumeAutoView(state: AutoViewState): AutoViewState {
  return {
    ...state,
    status: state.committedPlan === null ? 'Idle' : 'Following',
    candidatePlan: null,
    candidateSince: null,
    cameraRevision: state.committedPlan === null
      ? state.cameraRevision
      : state.cameraRevision + 1,
  }
}

/** Returns the earliest time at which the current candidate may be reconsidered. */
export function nextAutoViewTransitionAt(state: AutoViewState): number | null {
  if (state.candidatePlan === null || state.candidateSince === null) return null
  const settleAt = state.candidateSince + autoViewSettleDelayMs(state.candidatePlan.dominantPhase)
  if (state.candidatePlan.dominantPhase === 'Editing' || state.committedAt === null) {
    return settleAt
  }
  return Math.max(settleAt, state.committedAt + minimumPlanDwellMs)
}

export function autoViewSettleDelayMs(phase: AgentActivityPhase | null): number {
  switch (phase) {
    case 'Editing': return 300
    case 'Validating': return 500
    case 'Reading':
    case 'Thinking':
    case 'Working':
    case null:
      return 850
  }
}

function visiblePath(
  nodeId: string,
  level: ProjectionLevel,
  nodesById: ReadonlyMap<string, ArchitectureNode>,
): ArchitectureNode[] {
  const path: ArchitectureNode[] = []
  let candidate = nodesById.get(nodeId)
  while (candidate !== undefined) {
    if (isBaseVisible(candidate, level) || candidate.kind !== 'ArchitectureGroup' || level === 'System') {
      path.push(candidate)
    }
    candidate = candidate.parentId === null ? undefined : nodesById.get(candidate.parentId)
  }
  return path.reverse()
}

function isBaseVisible(node: ArchitectureNode, level: ProjectionLevel): boolean {
  switch (level) {
    case 'System': return node.kind === 'ArchitectureGroup'
    case 'Projects': return node.kind === 'Project'
    case 'Namespaces': return node.kind === 'Project' || node.kind === 'Namespace'
    case 'Classes': return node.kind !== 'ArchitectureGroup' && node.kind !== 'Project'
  }
}

function ancestorOrSelfOfKind(
  node: ArchitectureNode,
  kind: ArchitectureNode['kind'],
  nodesById: ReadonlyMap<string, ArchitectureNode>,
): ArchitectureNode | undefined {
  let candidate: ArchitectureNode | undefined = node
  while (candidate !== undefined) {
    if (candidate.kind === kind) return candidate
    candidate = candidate.parentId === null ? undefined : nodesById.get(candidate.parentId)
  }
  return undefined
}

function hasAncestorIn(
  node: ArchitectureNode,
  ancestorIds: ReadonlySet<string>,
  nodesById: ReadonlyMap<string, ArchitectureNode>,
): boolean {
  let candidate: ArchitectureNode | undefined = node
  while (candidate !== undefined) {
    if (ancestorIds.has(candidate.id)) return true
    candidate = candidate.parentId === null ? undefined : nodesById.get(candidate.parentId)
  }
  return false
}

function parseTimestamp(value: string | undefined): number | null {
  if (value === undefined) return null
  const parsed = Date.parse(value)
  return Number.isNaN(parsed) ? null : parsed
}

/**
 * The overlay intentionally retains a task's history. Auto follows only the newest mapped
 * activity batch per active agent so old reads/edits cannot keep expanding the live canvas.
 */
function latestActivityBatch(
  activities: AgentNodeActivity[],
  activeAgentIds: ReadonlySet<string>,
): AgentNodeActivity[] {
  const latestAtByAgent = new Map<string, number>()
  for (const activity of activities) {
    if (!activeAgentIds.has(activity.agentId)) continue
    const observedAt = parseTimestamp(activity.updatedAtUtc)
    if (observedAt === null) continue
    latestAtByAgent.set(
      activity.agentId,
      Math.max(latestAtByAgent.get(activity.agentId) ?? Number.NEGATIVE_INFINITY, observedAt),
    )
  }

  return activities.filter((activity) => {
    const latestAt = latestAtByAgent.get(activity.agentId)
    const observedAt = parseTimestamp(activity.updatedAtUtc)
    return latestAt !== undefined && observedAt === latestAt
  })
}

function uniqueNodes(nodes: ArchitectureNode[]): ArchitectureNode[] {
  return [...new Map(nodes.map((node) => [node.id, node])).values()]
    .sort((left, right) => left.id.localeCompare(right.id))
}

function sorted(values: ReadonlySet<string>): string[] {
  return [...values].sort((left, right) => left.localeCompare(right))
}
