import type { ArchitectureSnapshot } from './types'

export type WorkIndicatorResetMode = 'Never' | 'GitChange' | 'NewInstruction'

export interface WorkIndicatorState {
  mode: WorkIndicatorResetMode
  resetAtUtc: string | null
  lastGitIdentity: string | null
  lastInstructionId: string | null
}

export interface WorkIndicatorContext {
  gitIdentity: string | null
  instructionId: string | null
  instructionAtUtc: string | null
  observedAtUtc: string
}

export const workIndicatorResetOptions: ReadonlyArray<{
  value: WorkIndicatorResetMode
  label: string
}> = [
  { value: 'Never', label: 'Never' },
  { value: 'GitChange', label: 'On commit / new branch' },
  { value: 'NewInstruction', label: 'New prompt / instruction' },
]

const storageKey = 'cave.work-indicators.v1'
const defaultState: WorkIndicatorState = {
  mode: 'NewInstruction',
  resetAtUtc: null,
  lastGitIdentity: null,
  lastInstructionId: null,
}

export function readWorkIndicatorState(): WorkIndicatorState {
  try {
    const raw = window.localStorage.getItem(storageKey)
    if (raw === null) return defaultState

    const candidate = JSON.parse(raw) as Partial<WorkIndicatorState>
    return {
      mode: isResetMode(candidate.mode) ? candidate.mode : defaultState.mode,
      resetAtUtc: nullableString(candidate.resetAtUtc),
      lastGitIdentity: nullableString(candidate.lastGitIdentity),
      lastInstructionId: nullableString(candidate.lastInstructionId),
    }
  } catch {
    return defaultState
  }
}

export function writeWorkIndicatorState(state: WorkIndicatorState): void {
  try {
    window.localStorage.setItem(storageKey, JSON.stringify(state))
  } catch {
    // Browser storage can be unavailable in a restricted embedded host.
  }
}

export function createWorkIndicatorContext(
  snapshot: ArchitectureSnapshot,
  observedAtUtc: string,
): WorkIndicatorContext {
  const worktree = snapshot.git.worktree
  const instruction = snapshot.activity.latestInstruction
  return {
    gitIdentity: worktree === null
      ? null
      : `${worktree.branch ?? '(detached)'}\u0000${worktree.headSha}`,
    instructionId: instruction?.id ?? null,
    instructionAtUtc: instruction?.observedAtUtc ?? null,
    observedAtUtc,
  }
}

export function reconcileWorkIndicatorState(
  state: WorkIndicatorState,
  context: WorkIndicatorContext,
): WorkIndicatorState {
  let resetAtUtc = state.resetAtUtc
  if (state.mode === 'GitChange'
    && state.lastGitIdentity !== null
    && context.gitIdentity !== null
    && state.lastGitIdentity !== context.gitIdentity) {
    resetAtUtc = context.observedAtUtc
  }

  if (state.mode === 'NewInstruction' && context.instructionId !== null) {
    if (state.lastInstructionId !== null && state.lastInstructionId !== context.instructionId) {
      resetAtUtc = context.instructionAtUtc ?? context.observedAtUtc
    } else if (state.lastInstructionId === null && resetAtUtc === null) {
      resetAtUtc = context.instructionAtUtc ?? context.observedAtUtc
    }
  }

  const next: WorkIndicatorState = {
    ...state,
    resetAtUtc,
    lastGitIdentity: context.gitIdentity ?? state.lastGitIdentity,
    lastInstructionId: context.instructionId ?? state.lastInstructionId,
  }
  return statesEqual(state, next) ? state : next
}

export function setWorkIndicatorResetMode(
  state: WorkIndicatorState,
  mode: WorkIndicatorResetMode,
  context: WorkIndicatorContext | null,
): WorkIndicatorState {
  return {
    ...state,
    mode,
    lastGitIdentity: context?.gitIdentity ?? state.lastGitIdentity,
    lastInstructionId: context?.instructionId ?? state.lastInstructionId,
  }
}

export function resetWorkIndicators(
  state: WorkIndicatorState,
  context: WorkIndicatorContext | null,
  resetAtUtc: string,
): WorkIndicatorState {
  return {
    ...state,
    resetAtUtc,
    lastGitIdentity: context?.gitIdentity ?? state.lastGitIdentity,
    lastInstructionId: context?.instructionId ?? state.lastInstructionId,
  }
}

export function applyWorkIndicatorCutoff(
  snapshot: ArchitectureSnapshot,
  resetAtUtc: string | null,
): ArchitectureSnapshot {
  if (resetAtUtc === null) return snapshot

  const cutoff = Date.parse(resetAtUtc)
  if (Number.isNaN(cutoff)) return snapshot

  const nodes = snapshot.activity.nodes.filter((activity) => {
    const updatedAt = Date.parse(activity.updatedAtUtc)
    return Number.isNaN(updatedAt) || updatedAt >= cutoff
  })
  if (nodes.length === snapshot.activity.nodes.length) return snapshot

  return {
    ...snapshot,
    activity: {
      ...snapshot.activity,
      nodes,
    },
  }
}

function isResetMode(value: unknown): value is WorkIndicatorResetMode {
  return value === 'Never' || value === 'GitChange' || value === 'NewInstruction'
}

function nullableString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null
}

function statesEqual(left: WorkIndicatorState, right: WorkIndicatorState): boolean {
  return left.mode === right.mode
    && left.resetAtUtc === right.resetAtUtc
    && left.lastGitIdentity === right.lastGitIdentity
    && left.lastInstructionId === right.lastInstructionId
}
