export type ArchitectureNodeKind =
  | 'ArchitectureGroup'
  | 'Project'
  | 'Namespace'
  | 'Class'
  | 'Interface'
  | 'AbstractClass'

export type ArchitectureRelationKind =
  | 'Contains'
  | 'DependsOn'
  | 'Inherits'
  | 'Implements'
  | 'ProjectReference'
  | 'HttpApi'

export type EvidenceConfidence =
  | 'Exact'
  | 'Inferred'
  | 'Heuristic'
  | 'AgentDeclared'

export type SnapshotSourceKind = 'Sample' | 'CodeGraph'

export interface SourceLocation {
  filePath: string
  startLine: number
  endLine: number
}

export interface ArchitectureNode {
  id: string
  kind: ArchitectureNodeKind
  name: string
  parentId: string | null
  qualifiedName: string | null
  description: string | null
  categoryId: string | null
  tags: string[]
  sourceLocations: SourceLocation[]
}

export interface ArchitectureRelation {
  id: string
  sourceId: string
  targetId: string
  kind: ArchitectureRelationKind
  weight: number
  evidenceCount: number
  confidence: EvidenceConfidence
}

export interface ArchitectureGraph {
  nodes: ArchitectureNode[]
  relations: ArchitectureRelation[]
}

export interface SnapshotMetadata {
  workspaceName: string
  providerId: string
  sourceKind: SnapshotSourceKind
  isLive: boolean
  generatedAtUtc: string
}

export type GitBaselineKind = 'Upstream' | 'Head' | 'Commit'
export type GitDeltaStatus = 'Ready' | 'Unavailable'
export type GitFileChangeKind = 'Added' | 'Modified' | 'Deleted' | 'Renamed' | 'Untracked'
export type GitStructuralChangeKind = 'Added' | 'Modified' | 'Deleted' | 'Mixed'

export interface GitBaseline {
  kind: GitBaselineKind
  reference: string
  resolvedSha: string
}

export interface GitWorktreeIdentity {
  headSha: string
  branch: string | null
}

export interface GitHunkDelta {
  oldStart: number
  oldCount: number
  newStart: number
  newCount: number
}

export interface GitFileDelta {
  filePath: string
  previousPath: string | null
  kind: GitFileChangeKind
  additions: number
  deletions: number
  isBinary: boolean
  hunks: GitHunkDelta[]
}

export interface GitNodeDelta {
  nodeId: string
  kind: GitStructuralChangeKind
  additions: number
  deletions: number
  changedFiles: number
  paths: string[]
}

export interface GitDeltaOverlay {
  status: GitDeltaStatus
  baseline: GitBaseline | null
  worktree: GitWorktreeIdentity | null
  files: GitFileDelta[]
  nodes: GitNodeDelta[]
  unmappedFiles: GitFileDelta[]
  error: string | null
}

export type AgentWorkState = 'Planned' | 'Active' | 'Idle' | 'Completed'
export type AgentActivityPhase = 'Thinking' | 'Reading' | 'Editing' | 'Validating' | 'Working'
export type AgentActivityEvidenceKind = 'Observed' | 'Declared'
export type AgentActivitySourceStatus = 'Unobserved' | 'Ready' | 'Degraded'

export interface AgentActivity {
  agentId: string
  agentType: string
  isSubagent: boolean
  state: AgentWorkState
  phase: AgentActivityPhase | null
  summary: string | null
  hasObservedActivity: boolean
  hasDeclaredScope: boolean
  startedAtUtc: string
  updatedAtUtc: string
}

export interface AgentNodeActivity {
  agentId: string
  nodeId: string
  evidence: AgentActivityEvidenceKind
  isDirect: boolean
  updatedAtUtc: string
  paths: string[]
}

export interface RecentAgentEdit {
  agentId: string
  filePath: string
  nodeIds: string[]
  observedAtUtc: string
}

export interface AgentInstructionMarker {
  id: string
  observedAtUtc: string
}

export interface AgentActivityOverlay {
  agents: AgentActivity[]
  nodes: AgentNodeActivity[]
  recentEdits: RecentAgentEdit[]
  latestInstruction: AgentInstructionMarker | null
  unmappedPaths: string[]
  error: string | null
  sourceStatus?: AgentActivitySourceStatus
}

export interface ArchitectureSnapshot {
  metadata: SnapshotMetadata
  graph: ArchitectureGraph
  git: GitDeltaOverlay
  activity: AgentActivityOverlay
  conversation: ConversationOverlay
}

export type ConversationSourceStatus = 'Disabled' | 'Ready' | 'Degraded'
export type ConversationRole = 'User' | 'Assistant'
export type ConversationMessageKind = 'Prompt' | 'Commentary' | 'Final'

export interface ConversationMessage {
  eventId: string
  sessionId: string | null
  turnId: string | null
  agentId: string | null
  agentType: string | null
  isSubagent: boolean
  role: ConversationRole
  kind: ConversationMessageKind
  text: string
  isTruncated: boolean
  isStreaming: boolean
  occurredAtUtc: string
}

export type ConversationControlState = 'Unavailable' | 'Ready' | 'Queued' | 'Running' | 'Failed'
export type ConversationDeliveryState = 'Queued' | 'Running' | 'Completed' | 'Failed'

export interface ConversationDelivery {
  messageId: string
  sessionId: string
  turnId: string | null
  state: ConversationDeliveryState
  queuedAtUtc: string
  updatedAtUtc: string
  error: string | null
}

export interface ConversationControl {
  sessionId: string | null
  turnId: string | null
  state: ConversationControlState
  canSend: boolean
  deliveries: ConversationDelivery[]
  error: string | null
}

export interface ConversationOverlay {
  sharingEnabled: boolean
  status: ConversationSourceStatus
  messages: ConversationMessage[]
  control: ConversationControl
  error: string | null
}

export interface LiveArchitectureSnapshot {
  version: number
  snapshot: ArchitectureSnapshot
  changedPaths: string[]
  activityChanged: boolean
  conversationChanged: boolean
  observedAtUtc: string
  refreshError: string | null
}

export interface CaveToolResult {
  subscriptionId: string
  workspaceRoot: string
  update: LiveArchitectureSnapshot
}

export interface WorkspaceOverview {
  workspaceId: string
  name: string
  rootPath: string
  isAvailable: boolean
  lastSeenAtUtc: string
  knownAgentCount: number
  activeAgentCount: number
  activeSubagentCount: number
  currentPhase: AgentActivityPhase | null
  activitySummary: string | null
  lastActivityAtUtc: string | null
  activityError: string | null
}

export interface WorkspaceOverviewSnapshot {
  hostName: string
  workspaces: WorkspaceOverview[]
  errors: string[]
}

export type CodexUsageStatus = 'Ready' | 'Unavailable'

export interface CodexUsageSummary {
  lifetimeTokens: number | null
  peakDailyTokens: number | null
  longestRunningTurnSeconds: number | null
  currentStreakDays: number | null
  longestStreakDays: number | null
}

export interface CodexDailyUsage {
  date: string
  tokens: number
}

export interface CodexRateLimitWindow {
  limitId: string
  limitName: string | null
  window: string
  usedPercent: number
  windowDurationMinutes: number | null
  resetsAtUtc: string | null
}

export interface CodexUsageSnapshot {
  status: CodexUsageStatus
  summary: CodexUsageSummary | null
  daily: CodexDailyUsage[]
  rateLimits: CodexRateLimitWindow[]
  retrievedAtUtc: string
  error: string | null
}

export interface CaveInfoSnapshot {
  viewerProtocolVersion: number
  usage: CodexUsageSnapshot
}
