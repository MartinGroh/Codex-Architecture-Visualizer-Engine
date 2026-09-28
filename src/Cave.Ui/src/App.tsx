import { AgentWorkContext } from './AgentWorkContext'
import { agentDisplayName } from './agentIdentity'
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  ReactFlow,
  applyEdgeChanges,
  applyNodeChanges,
  getViewportForBounds,
  type EdgeChange,
  type NodeChange,
  type ReactFlowInstance,
} from '@xyflow/react'
import {
  Activity,
  ArrowLeft,
  ArrowDownToLine,
  ArrowUpFromLine,
  BookOpen,
  Boxes,
  ChevronRight,
  ChevronsUp,
  CircleAlert,
  GitCompareArrows,
  Info as InfoIcon,
  Layers3,
  Moon,
  MessageCircle,
  PanelLeftClose,
  PanelLeftOpen,
  PackageOpen,
  Radio,
  RotateCcw,
  ScanSearch,
  Sparkles,
  Sun,
  TestTube2,
  Workflow,
  X,
} from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { CaveLogoMark } from './CaveLogoMark'
import {
  AgentActivityPlacement,
  type AgentActivityPlacementMode,
} from './AgentActivityPlacement'
import { ArchitectureNodeCard } from './ArchitectureNodeCard'
import { ArchitectureRouteEdge } from './ArchitectureRouteEdge'
import {
  connectArchitectureFeed,
  readCaveInfo,
  type ArchitectureFeedConnectionState,
  type CodexHostBridge,
} from './api'
import { ConversationPanel } from './ConversationPanel'
import { EngineeringWorkspace } from './EngineeringWorkspace'
import { InfoPanel } from './InfoPanel'
import {
  AskNodeDialog,
  NodeContextMenu,
  NodeMemoDialog,
  type NodeMemoState,
} from './NodeMemo'
import { buildNodeMemoPrompt } from './nodeConversation'
import {
  createAgentNodeDecorations,
  highlightConnectedEdges,
  type ArchitectureFlowEdge,
  type ArchitectureFlowNode,
  type ArchitectureNodeData,
} from './graphPresentation'
import { ElkLayoutEngine, type LayoutDensity } from './layout'
import { architectureNodeVisual } from './nodeVisual'
import {
  nodeReadabilityCssVariables,
  nodeReadabilityForViewport,
} from './nodeReadability'
import { nodeDocumentationSummary } from './nodeDocumentation'
import { focusActivitySnapshot } from './activityFocus'
import {
  MobileGraphScopeToggle,
  type MobileGraphScope,
} from './MobileGraphScopeToggle'
import { isTestProject } from './projectTypes'
import {
  collapseHierarchyExpansion,
  nextAutoProjectionLevel,
  createHierarchyPresentation,
  isExternalPackage,
  projectArchitecture,
  projectionLevelForZoom,
  projectionLevels,
  toggleHierarchyExpansion,
  type HierarchyExpansionState,
  type ProjectionLevel,
  type ProjectionMode,
} from './projection'
import {
  caveThemeOptions,
  readTheme,
  readThemePreference,
  subscribeTheme,
  themeColorScheme,
  themeLabel,
  writeThemePreference,
  type ThemePreference,
} from './theme'
import {
  applyWorkIndicatorCutoff,
  createWorkIndicatorContext,
  readWorkIndicatorState,
  reconcileWorkIndicatorState,
  resetWorkIndicators,
  setWorkIndicatorResetMode,
  workIndicatorResetOptions,
  writeWorkIndicatorState,
  type WorkIndicatorResetMode,
} from './workIndicators'
import type {
  ArchitectureNode,
  ArchitectureSnapshot,
  CaveInfoSnapshot,
  GitNodeDelta,
  LiveArchitectureSnapshot,
} from './types'
import { WorkspaceDashboard } from './WorkspaceDashboard'
import { MachineActivityPage } from './MachineActivityPage'
import { AgentFlowLightPage } from './AgentFlowLightPage'
import { SettingsPage } from './SettingsPage'
import { WorkspaceRailCatalog } from './WorkspaceRail'
import { UsageSpotlight } from './UsageSpotlight'
import {
  createAutoViewState,
  deriveAutoViewPlan,
  nextAutoViewTransitionAt,
  pauseAutoView,
  reconcileAutoViewState,
  resumeAutoView,
} from './autoView'
import './App.css'

const layoutEngine = new ElkLayoutEngine()
const nodeTypes = { architecture: ArchitectureNodeCard }
const edgeTypes = { architectureRoute: ArchitectureRouteEdge }
const graphFitOptions = { padding: 0.12, maxZoom: 0.78, duration: 240 }
const externalPackageFilterPreferenceKey = 'cave.hide-external-packages'
const legacyNuGetFilterPreferenceKey = 'cave.hide-nuget-packages'
const agentActivityPlacementPreferenceKey = 'cave.agent-activity-placement'
const layoutDensityPreferenceKey = 'cave.layout-density'
type GraphMode = 'Architecture' | 'Live' | 'Changes'
type WorkspaceSurface = 'graph' | 'engineering'

function ArchitectureApp({
  browserWorkspaceId,
  isEmbedded,
}: {
  browserWorkspaceId: string | null
  isEmbedded: boolean
}) {
  const isMobileProjectView = useStandaloneMobileProjectView(isEmbedded)
  const [liveSnapshot, setLiveSnapshot] = useState<LiveArchitectureSnapshot | null>(null)
  const [nodes, setNodes] = useState<ArchitectureFlowNode[]>([])
  const [edges, setEdges] = useState<ArchitectureFlowEdge[]>([])
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [interactionError, setInteractionError] = useState<string | null>(null)
  const [hostBridge, setHostBridge] = useState<CodexHostBridge | null>(null)
  const [conversationOpen, setConversationOpen] = useState(false)
  const [infoOpen, setInfoOpen] = useState(false)
  const [info, setInfo] = useState<CaveInfoSnapshot | null>(null)
  const [infoError, setInfoError] = useState<string | null>(null)
  const [interactionBusy, setInteractionBusy] = useState(false)
  const [contextMenu, setContextMenu] = useState<{ nodeId: string; x: number; y: number } | null>(null)
  const [askNodeId, setAskNodeId] = useState<string | null>(null)
  const [nodeMemo, setNodeMemo] = useState<NodeMemoState | null>(null)
  const [feedConnection, setFeedConnection] = useState<ArchitectureFeedConnectionState>({
    status: 'Connecting',
    attempt: 0,
    retryInMs: null,
  })
  const [projectionMode, setProjectionMode] = useState<ProjectionMode>('Auto')
  const [projectionLevel, setProjectionLevel] = useState<ProjectionLevel>('Projects')
  const [graphMode, setGraphMode] = useState<GraphMode>('Live')
  const [workspaceSurface, setWorkspaceSurface] = useState<WorkspaceSurface>('graph')
  const [currentZoom, setCurrentZoom] = useState(0.5)
  const currentZoomRef = useRef(currentZoom)
  const [themePreference, setThemePreference] = useState<ThemePreference>(readThemePreference)
  const [themeState, setThemeState] = useState(() => readTheme(themePreference))
  const [detailPanelOpen, setDetailPanelOpen] = useState(true)
  const [navigationExpanded, setNavigationExpanded] = useState(false)
  const [agentActivityPlacement, setAgentActivityPlacement] = useState(
    readAgentActivityPlacement,
  )
  const [hideTests, setHideTests] = useState(true)
  const [hideExternalPackages, setHideExternalPackages] = useState(readHideExternalPackages)
  const [layoutDensity, setLayoutDensity] = useState<LayoutDensity>(readLayoutDensity)
  const [mobileGraphScope, setMobileGraphScope] = useState<MobileGraphScope>('Work')
  const [workIndicatorState, setWorkIndicatorState] = useState(readWorkIndicatorState)
  const [layoutRevision, setLayoutRevision] = useState(0)
  const [hierarchyExpansion, setHierarchyExpansion] = useState<HierarchyExpansionState>(
    collapseHierarchyExpansion,
  )
  const [autoViewState, setAutoViewState] = useState(createAutoViewState)
  const layoutVersion = useRef(0)
  const pendingFit = useRef(false)
  const previousDetailPanelOpen = useRef(detailPanelOpen)
  const previousNavigationExpanded = useRef(navigationExpanded)
  const previousAgentActivityPlacement = useRef(agentActivityPlacement)
  const flowInstance = useRef<ReactFlowInstance<ArchitectureFlowNode, ArchitectureFlowEdge> | null>(null)
  const canvasFrame = useRef<HTMLElement | null>(null)

  const updateNodeReadability = useCallback((zoom: number) => {
    currentZoomRef.current = zoom
    const frame = canvasFrame.current
    if (frame === null) return

    const variables = nodeReadabilityCssVariables(
      nodeReadabilityForViewport(zoom, {
        width: frame.clientWidth,
        height: frame.clientHeight,
      }),
    )
    for (const [name, value] of Object.entries(variables)) {
      frame.style.setProperty(name, value)
    }
  }, [])

  useEffect(() => {
    const refreshForViewport = () => updateNodeReadability(currentZoomRef.current)
    refreshForViewport()
    window.addEventListener('resize', refreshForViewport, { passive: true })
    return () => window.removeEventListener('resize', refreshForViewport)
  }, [updateNodeReadability])

  const scheduleFitView = useCallback((focusNodeIds?: ReadonlySet<string>) => {
    let settledFrame = 0
    let settleTimer = 0
    const renderedFrame = window.requestAnimationFrame(() => {
      settledFrame = window.requestAnimationFrame(() => {
        settleTimer = window.setTimeout(() => {
          const instance = flowInstance.current
          if (instance === null) return
          const focusNodes = focusNodeIds === undefined
            ? undefined
            : [...document.querySelectorAll<HTMLElement>('.canvas-frame .react-flow__node')]
                .filter((node) => focusNodeIds.has(node.dataset.id ?? ''))
          if (focusNodes !== undefined && focusNodes.length > 0) {
            const canvas = document.querySelector<HTMLElement>('.canvas-frame .react-flow')
            if (canvas === null) return
            const canvasRect = canvas.getBoundingClientRect()
            const currentViewport = instance.getViewport()
            const screenBounds = focusNodes.map((node) => node.getBoundingClientRect())
            const left = Math.min(...screenBounds.map((bounds) => bounds.left))
            const top = Math.min(...screenBounds.map((bounds) => bounds.top))
            const right = Math.max(...screenBounds.map((bounds) => bounds.right))
            const bottom = Math.max(...screenBounds.map((bounds) => bounds.bottom))
            const viewport = getViewportForBounds(
              {
                x: (left - canvasRect.left - currentViewport.x) / currentViewport.zoom,
                y: (top - canvasRect.top - currentViewport.y) / currentViewport.zoom,
                width: (right - left) / currentViewport.zoom,
                height: (bottom - top) / currentViewport.zoom,
              },
              canvas.clientWidth,
              canvas.clientHeight,
              0.1,
              graphFitOptions.maxZoom,
              graphFitOptions.padding,
            )
            void instance.setViewport(viewport, { duration: graphFitOptions.duration })
          } else {
            void instance.fitView(graphFitOptions)
          }
        }, 120)
      })
    })

    return () => {
      window.cancelAnimationFrame(renderedFrame)
      window.cancelAnimationFrame(settledFrame)
      window.clearTimeout(settleTimer)
    }
  }, [])

  useEffect(() => {
    let active = true
    let disconnect: (() => void) | null = null

    const applyUpdate = (update: LiveArchitectureSnapshot) => {
      if (!active) {
        return
      }

      setLiveSnapshot(update)
      setError(update.refreshError)
    }

    void connectArchitectureFeed(
      applyUpdate,
      (reason) => active && setError(reason.message),
      (state) => active && setFeedConnection(state),
      browserWorkspaceId ?? undefined,
      (bridge) => active && setHostBridge(bridge),
    ).then((cleanup) => {
      if (active) {
        disconnect = cleanup
      } else {
        cleanup()
      }
    }).catch((reason: unknown) => {
      if (active) {
        setError(reason instanceof Error ? reason.message : 'Unable to load CAVE.')
      }
    })

    return () => {
      active = false
      if (disconnect !== null) {
        disconnect()
      }
    }
  }, [browserWorkspaceId])

  useEffect(() => {
    const controller = new AbortController()
    const refresh = () => {
      void (hostBridge?.readInfo() ?? readCaveInfo(controller.signal))
        .then((next) => { setInfo(next); setInfoError(null) })
        .catch((reason: unknown) => {
          if (!controller.signal.aborted) setInfoError(
            reason instanceof Error ? reason.message : 'CAVE information is unavailable.',
          )
        })
    }
    refresh()
    const interval = window.setInterval(refresh, 60_000)
    return () => { controller.abort(); window.clearInterval(interval) }
  }, [hostBridge])

  const snapshot = liveSnapshot?.snapshot ?? null
  const workIndicatorContext = useMemo(
    () => snapshot === null || liveSnapshot === null
      ? null
      : createWorkIndicatorContext(snapshot, liveSnapshot.observedAtUtc),
    [liveSnapshot, snapshot],
  )
  const observedAutoViewPlan = useMemo(
    () => snapshot === null || projectionMode !== 'Auto' || graphMode !== 'Live'
      ? null
      : deriveAutoViewPlan(snapshot, { hideTests, hideExternalPackages }),
    [graphMode, hideExternalPackages, hideTests, projectionMode, snapshot],
  )
  const activeAutoViewPlan = projectionMode === 'Auto'
    && graphMode === 'Live'
    && autoViewState.status !== 'PausedByUser'
      ? autoViewState.committedPlan
      : null
  const presentationHidesTests = isMobileProjectView ? true : hideTests
  const presentationHidesExternalPackages = isMobileProjectView ? true : hideExternalPackages
  const mobileUsesFocusedWork = isMobileProjectView && mobileGraphScope === 'Work'
  const mobileAutoViewPlan = useMemo(
    () => snapshot === null || !mobileUsesFocusedWork
      ? null
      : deriveAutoViewPlan(snapshot, {
          hideTests: presentationHidesTests,
          hideExternalPackages: presentationHidesExternalPackages,
          maxRevealedNodes: 8,
        }),
    [mobileUsesFocusedWork, presentationHidesExternalPackages, presentationHidesTests, snapshot],
  )
  const presentationAutoViewPlan = mobileUsesFocusedWork
    ? mobileAutoViewPlan
    : isMobileProjectView
      ? null
      : activeAutoViewPlan
  const presentationProjectionLevel = isMobileProjectView && mobileGraphScope === 'Architecture'
    ? 'Projects'
    : presentationAutoViewPlan?.projectionLevel ?? projectionLevel
  const presentationExpansion = useMemo(
    () => presentationAutoViewPlan === null
      ? hierarchyExpansion
      : {
          expandedNodeIds: presentationAutoViewPlan.expandedNodeIds,
          focusNodeId: null,
        },
    [hierarchyExpansion, presentationAutoViewPlan],
  )
  const semanticProjectedSnapshot = useMemo(
    () => snapshot === null ? null : projectArchitecture(snapshot, presentationProjectionLevel, {
      hideTests: presentationHidesTests,
      hideExternalPackages: presentationHidesExternalPackages,
      expandedNodeIds: presentationExpansion.expandedNodeIds,
      revealedNodeIds: presentationAutoViewPlan?.revealedNodeIds,
    }),
    [
      presentationHidesExternalPackages,
      presentationHidesTests,
      presentationExpansion.expandedNodeIds,
      presentationAutoViewPlan?.revealedNodeIds,
      presentationProjectionLevel,
      snapshot,
    ],
  )
  const projectedSnapshot = useMemo(
    () => semanticProjectedSnapshot === null
      ? null
      : presentationAutoViewPlan !== null || mobileUsesFocusedWork
        ? focusActivitySnapshot(
            semanticProjectedSnapshot,
            presentationAutoViewPlan?.fitNodeIds ?? new Set(),
          )
        : semanticProjectedSnapshot,
    [mobileUsesFocusedWork, presentationAutoViewPlan, semanticProjectedSnapshot],
  )
  const indicatorSnapshot = useMemo(
    () => projectedSnapshot === null
      ? null
      : applyWorkIndicatorCutoff(projectedSnapshot, workIndicatorState.resetAtUtc),
    [projectedSnapshot, workIndicatorState.resetAtUtc],
  )
  const hierarchyPresentation = useMemo(
    () => snapshot === null || projectedSnapshot === null
      ? null
      : createHierarchyPresentation(
          snapshot,
          projectedSnapshot,
          presentationProjectionLevel,
          presentationExpansion,
          presentationAutoViewPlan?.focusNodeIds,
        ),
    [
      presentationExpansion,
      presentationAutoViewPlan?.focusNodeIds,
      presentationProjectionLevel,
      projectedSnapshot,
      snapshot,
    ],
  )
  const hierarchyFitNodeIds = useMemo(
    () => presentationAutoViewPlan !== null
      ? presentationAutoViewPlan.fitNodeIds
      : hierarchyPresentation === null || hierarchyExpansion.focusNodeId === null
        ? undefined
        : new Set(projectedSnapshot?.graph.nodes
            .filter((node) => !hierarchyPresentation.dimmedNodeIds.has(node.id))
            .map((node) => node.id) ?? []),
    [hierarchyExpansion.focusNodeId, hierarchyPresentation, presentationAutoViewPlan, projectedSnapshot],
  )
  const hierarchyFitNodeIdsRef = useRef(hierarchyFitNodeIds)
  hierarchyFitNodeIdsRef.current = hierarchyFitNodeIds

  const toggleNodeExpansion = useCallback((nodeId: string) => {
    if (snapshot === null) return
    pendingFit.current = true
    if (projectionMode === 'Auto') {
      setAutoViewState(pauseAutoView)
    }
    setHierarchyExpansion((current) => toggleHierarchyExpansion(
      snapshot,
      presentationAutoViewPlan === null
        ? current
        : { expandedNodeIds: presentationAutoViewPlan.expandedNodeIds, focusNodeId: null },
      nodeId,
    ))
  }, [presentationAutoViewPlan, projectionMode, snapshot])

  useEffect(() => {
    if (projectionMode !== 'Auto' || graphMode !== 'Live') return
    setAutoViewState((current) => reconcileAutoViewState(
      current,
      observedAutoViewPlan,
      snapshot?.activity.latestInstruction?.id ?? null,
      Date.now(),
    ))
  }, [graphMode, observedAutoViewPlan, projectionMode, snapshot?.activity.latestInstruction?.id])

  useEffect(() => {
    if (projectionMode !== 'Auto' || graphMode !== 'Live') return
    const transitionAt = nextAutoViewTransitionAt(autoViewState)
    if (transitionAt === null) return
    const timer = window.setTimeout(() => {
      setAutoViewState((current) => reconcileAutoViewState(
        current,
        current.candidatePlan,
        snapshot?.activity.latestInstruction?.id ?? null,
        Date.now(),
      ))
    }, Math.max(0, transitionAt - Date.now()))
    return () => window.clearTimeout(timer)
  }, [autoViewState, graphMode, projectionMode, snapshot?.activity.latestInstruction?.id])

  useEffect(() => {
    if (activeAutoViewPlan === null) return
    setProjectionLevel(activeAutoViewPlan.projectionLevel)
    pendingFit.current = true
  }, [activeAutoViewPlan, autoViewState.cameraRevision])

  useEffect(() => {
    if (!isMobileProjectView) return
    setGraphMode('Live')
    setProjectionMode('Auto')
    pendingFit.current = true
  }, [isMobileProjectView, mobileAutoViewPlan?.signature, mobileGraphScope])

  useEffect(() => {
    if (workIndicatorContext === null) return
    setWorkIndicatorState((current) => reconcileWorkIndicatorState(current, workIndicatorContext))
  }, [workIndicatorContext])

  useEffect(() => {
    writeWorkIndicatorState(workIndicatorState)
  }, [workIndicatorState])

  useEffect(() => {
    writeHideExternalPackages(hideExternalPackages)
  }, [hideExternalPackages])

  useEffect(() => {
    writeAgentActivityPlacement(agentActivityPlacement)
  }, [agentActivityPlacement])

  useEffect(() => {
    writeLayoutDensity(layoutDensity)
  }, [layoutDensity])

  useEffect(() => {
    if (indicatorSnapshot === null) {
      return
    }

    const requestedLayout = ++layoutVersion.current
    void layoutEngine.layout(indicatorSnapshot, presentationProjectionLevel, {
      hierarchy: hierarchyPresentation ?? undefined,
      onToggleExpansion: toggleNodeExpansion,
      density: isMobileProjectView || presentationAutoViewPlan !== null ? 'Tight' : layoutDensity,
    }).then((layout) => {
      if (requestedLayout !== layoutVersion.current) {
        return
      }

      setNodes(layout.nodes)
      setEdges(layout.edges)
      setLayoutRevision((current) => current + 1)
      setSelectedNodeId((current) =>
        indicatorSnapshot.graph.nodes.some((node) => node.id === current)
          ? current
          : preferredNode(indicatorSnapshot, presentationProjectionLevel)?.id ?? null,
      )
    })
  }, [
    hierarchyPresentation,
    indicatorSnapshot,
    isMobileProjectView,
    layoutDensity,
    presentationAutoViewPlan,
    presentationProjectionLevel,
    toggleNodeExpansion,
  ])

  useEffect(() => {
    if (!pendingFit.current) {
      return
    }

    pendingFit.current = false
    return scheduleFitView(hierarchyFitNodeIdsRef.current)
  }, [layoutRevision, scheduleFitView])

  useEffect(() => {
    if (previousDetailPanelOpen.current === detailPanelOpen) {
      return
    }

    previousDetailPanelOpen.current = detailPanelOpen
    return scheduleFitView()
  }, [detailPanelOpen, scheduleFitView])

  useEffect(() => {
    if (isEmbedded || previousNavigationExpanded.current === navigationExpanded) {
      return
    }

    previousNavigationExpanded.current = navigationExpanded
    return scheduleFitView()
  }, [isEmbedded, navigationExpanded, scheduleFitView])

  useEffect(() => {
    if (previousAgentActivityPlacement.current === agentActivityPlacement) {
      return
    }

    previousAgentActivityPlacement.current = agentActivityPlacement
    return scheduleFitView()
  }, [agentActivityPlacement, scheduleFitView])

  useEffect(() => {
    document.documentElement.dataset.theme = themeState.theme
    document.documentElement.style.colorScheme = themeColorScheme(themeState.theme)
  }, [themeState.theme])

  useEffect(() => {
    writeThemePreference(themePreference)
    setThemeState(readTheme(themePreference))
    return subscribeTheme(setThemeState, themePreference)
  }, [themePreference])

  const selectedNode = useMemo(
    () => projectedSnapshot?.graph.nodes.find((node) => node.id === selectedNodeId) ?? null,
    [projectedSnapshot, selectedNodeId],
  )
  const showDetailPanel = workspaceSurface === 'graph' && detailPanelOpen && !isMobileProjectView
  const displayedRelationCount = useMemo(
    () => edges.filter((edge) => edge.data?.relationKind !== undefined).length,
    [edges],
  )
  const displayedEdges = useMemo(
    () => highlightConnectedEdges(edges, showDetailPanel ? selectedNodeId : null),
    [edges, selectedNodeId, showDetailPanel],
  )
  const selectedGitDelta = useMemo(
    () => snapshot?.git.nodes.find((delta) => delta.nodeId === selectedNodeId) ?? null,
    [selectedNodeId, snapshot],
  )
  const contextNode = useMemo(
    () => snapshot?.graph.nodes.find((node) => node.id === contextMenu?.nodeId) ?? null,
    [contextMenu?.nodeId, snapshot],
  )
  const askNode = useMemo(
    () => snapshot?.graph.nodes.find((node) => node.id === askNodeId) ?? null,
    [askNodeId, snapshot],
  )
  const gitTotals = useMemo(
    () => snapshot?.git.files.reduce(
      (totals, file) => ({
        additions: totals.additions + file.additions,
        deletions: totals.deletions + file.deletions,
      }),
      { additions: 0, deletions: 0 },
    ) ?? { additions: 0, deletions: 0 },
    [snapshot],
  )
  const gitReady = snapshot?.git.status === 'Ready'
  const canAskNode = hostBridge?.canSample === true
    && (isEmbedded || (snapshot?.conversation.control.sessionId ?? null) !== null)
  const nodeQuestionHint = canAskNode
    ? 'Runs in a temporary side chat.'
    : 'Waiting for an exact Codex task binding.'
  const activeAgents = useMemo(
    () => snapshot?.activity.agents.filter((agent) => agent.state === 'Active') ?? [],
    [snapshot],
  )
  const testProjectCount = useMemo(
    () => snapshot?.graph.nodes.filter(isTestProject).length ?? 0,
    [snapshot],
  )
  const externalPackageCount = useMemo(
    () => snapshot?.graph.nodes.filter(isExternalPackage).length ?? 0,
    [snapshot],
  )
  const activeNodeCount = useMemo(() => {
    if (indicatorSnapshot === null) return 0
    return [...createAgentNodeDecorations(indicatorSnapshot).values()]
      .filter((decorations) => decorations.some((decoration) => decoration.state === 'Active'))
      .length
  }, [indicatorSnapshot])

  useEffect(() => {
    if (snapshot !== null && !gitReady && graphMode === 'Changes') {
      setGraphMode('Architecture')
    }
  }, [gitReady, graphMode, snapshot])

  const ThemeIcon = themeColorScheme(themeState.theme) === 'dark' ? Moon : Sun
  const automaticTheme = readTheme('auto')

  const selectGraphMode = (mode: GraphMode) => {
    setWorkspaceSurface('graph')
    setGraphMode(mode)
    scheduleFitView()
    if (mode === 'Live' && indicatorSnapshot !== null) {
      const activeNodeId = [...createAgentNodeDecorations(indicatorSnapshot)]
        .find(([, decorations]) => (
          decorations.some((decoration) => decoration.state === 'Active')
        ))?.[0]
      if (activeNodeId !== undefined) {
        setSelectedNodeId(activeNodeId)
      }
    }
  }

  const selectProjectionMode = (mode: ProjectionMode) => {
    setProjectionMode(mode)
    if (mode === 'Auto') {
      setAutoViewState(resumeAutoView)
    }
    const nextLevel = mode === 'Auto' ? projectionLevelForZoom(currentZoom) : mode
    if (nextLevel === projectionLevel) {
      scheduleFitView()
      return
    }

    pendingFit.current = true
    setProjectionLevel(nextLevel)
  }

  const toggleTests = () => {
    pendingFit.current = true
    setHideTests((current) => !current)
  }

  const toggleExternalPackages = () => {
    pendingFit.current = true
    setHideExternalPackages((current) => !current)
  }

  const selectLayoutDensity = (density: LayoutDensity) => {
    if (density === layoutDensity) return
    pendingFit.current = true
    setLayoutDensity(density)
  }

  const collapseAllHierarchy = () => {
    if (hierarchyExpansion.expandedNodeIds.size === 0) return
    pendingFit.current = true
    setHierarchyExpansion(collapseHierarchyExpansion())
  }

  const resetVisibleWorkIndicators = () => {
    const resetAtUtc = workIndicatorContext?.observedAtUtc ?? new Date().toISOString()
    setWorkIndicatorState((current) => resetWorkIndicators(
      current,
      workIndicatorContext,
      resetAtUtc,
    ))
  }

  const sendConversationMessage = async (text: string) => {
    if (hostBridge === null) return false
    setInteractionBusy(true)
    setInteractionError(null)
    try {
      await hostBridge.sendMessage(text, snapshot?.conversation.control.sessionId ?? undefined)
      return true
    } catch (reason: unknown) {
      setInteractionError(reason instanceof Error ? reason.message : 'The message could not be sent.')
      return false
    } finally {
      setInteractionBusy(false)
    }
  }

  const setConversationSharing = async (enabled: boolean) => {
    if (hostBridge === null) return
    setInteractionBusy(true)
    setInteractionError(null)
    try {
      await hostBridge.setConversationSharing(enabled)
    } catch (reason: unknown) {
      setInteractionError(reason instanceof Error ? reason.message : 'Conversation sharing could not be changed.')
    } finally {
      setInteractionBusy(false)
    }
  }

  const runNodeMemo = (node: ArchitectureNode, question: string, kind: NodeMemoState['kind']) => {
    setContextMenu(null)
    setAskNodeId(null)
    const id = `memo-${Date.now()}-${Math.random().toString(16).slice(2)}`
    setNodeMemo({
      id,
      nodeName: node.name,
      question,
      kind,
      status: 'Loading',
      text: null,
      error: null,
    })
    if (snapshot === null) {
      setNodeMemo((current) => current?.id === id
        ? { ...current, status: 'Error', error: 'The architecture snapshot is not available.' }
        : current)
      return
    }

    if (hostBridge?.canSample !== true) {
      setNodeMemo((current) => current?.id === id
        ? { ...current, status: 'Error', error: 'This CAVE host cannot create a temporary Codex side chat.' }
        : current)
      return
    }

    void hostBridge.sampleMemo(
      buildNodeMemoPrompt(node, snapshot, question),
      isEmbedded ? undefined : snapshot.conversation.control.sessionId ?? undefined,
    )
      .then((text) => setNodeMemo((current) => current?.id === id
        ? { ...current, status: 'Ready', text }
        : current))
      .catch((reason: unknown) => setNodeMemo((current) => current?.id === id
        ? {
            ...current,
            status: 'Error',
            error: reason instanceof Error ? reason.message : 'The isolated sub-chat failed.',
          }
        : current))
  }

  const changeWorkIndicatorResetMode = (mode: WorkIndicatorResetMode) => {
    setWorkIndicatorState((current) => setWorkIndicatorResetMode(
      current,
      mode,
      workIndicatorContext,
    ))
  }

  const changeMobileGraphScope = (scope: MobileGraphScope) => {
    setMobileGraphScope(scope)
    setContextMenu(null)
    pendingFit.current = true
  }

  if (error !== null && snapshot === null && feedConnection.status !== 'Reconnecting') {
    return <ErrorState message={error} />
  }

  const feedIsLive = feedConnection.status === 'Connected'
    && snapshot?.metadata.isLive
    && liveSnapshot?.refreshError === null
  const feedStatusTitle = feedConnection.status === 'Connected'
    ? (snapshot?.metadata.isLive ? 'Live semantic index' : 'Acceptance spike')
    : feedConnection.status === 'Reconnecting'
      ? 'Reconnecting to CAVE'
      : 'Connecting to CAVE'
  const feedStatusDetail = feedConnection.status === 'Reconnecting'
    ? `Attempt ${feedConnection.attempt} · retry in ${formatRetryDelay(feedConnection.retryInMs)}`
    : `${snapshot?.metadata.providerId ?? 'Waiting for local host'}${liveSnapshot ? ` · v${liveSnapshot.version}` : ''}`
  const effectiveAgentActivityPlacement = isMobileProjectView ? 'canvas' : agentActivityPlacement
  const liveActivitySpotlight = graphMode === 'Live' ? (
    <AgentActivityPlacement
      placement={effectiveAgentActivityPlacement}
      agents={activeAgents}
      activeNodeCount={activeNodeCount}
      recentEditCount={snapshot?.activity.recentEdits.length ?? 0}
      unmappedCount={snapshot?.activity.unmappedPaths.length ?? 0}
      isSelected
      prominent={isMobileProjectView}
      onSelect={() => selectGraphMode('Live')}
      onTogglePlacement={() => setAgentActivityPlacement((current) => (
        current === 'canvas' ? 'topbar' : 'canvas'
      ))}
    />
  ) : null

  return (
    <main
      className={`app-shell ${showDetailPanel ? 'app-shell--detail-open' : ''} ${isEmbedded ? 'app-shell--embedded' : ''} ${navigationExpanded ? 'app-shell--navigation-expanded' : ''} ${workspaceSurface === 'engineering' ? 'app-shell--engineering' : ''} ${isMobileProjectView ? 'app-shell--mobile-project' : ''} ${isMobileProjectView && mobileGraphScope === 'Architecture' ? 'app-shell--mobile-architecture' : ''}`}
      data-theme={themeState.theme}
    >
      {!isEmbedded && !isMobileProjectView && (
        <aside className={`navigation-rail ${navigationExpanded ? 'is-expanded' : ''}`}>
          <a
            className="brand-mark"
            href="/"
            aria-label="Open CAVE workspace overview"
            title="All projects"
          >
            <span className="brand-glyph"><CaveLogoMark size={22} /></span>
            <span>CAVE</span>
          </a>

          <nav aria-label="Workspace views">
            <button
              className={`rail-button ${workspaceSurface === 'graph' && graphMode === 'Architecture' ? 'is-active' : ''}`}
              type="button"
              aria-label="Architecture"
              aria-pressed={workspaceSurface === 'graph' && graphMode === 'Architecture'}
              onClick={() => selectGraphMode('Architecture')}
            >
              <ScanSearch size={19} />
              <span className="rail-button__label">Architecture</span>
            </button>
            <button
              className={`rail-button ${workspaceSurface === 'graph' && graphMode === 'Live' ? 'is-active' : ''}`}
              type="button"
              aria-label="Live activity"
              aria-pressed={workspaceSurface === 'graph' && graphMode === 'Live'}
              onClick={() => selectGraphMode('Live')}
            >
              <Activity size={19} />
              <span className="rail-button__label">Live activity</span>
            </button>
            <button
              className={`rail-button ${workspaceSurface === 'graph' && graphMode === 'Changes' ? 'is-active' : ''}`}
              type="button"
              aria-label="Git changes"
              aria-pressed={workspaceSurface === 'graph' && graphMode === 'Changes'}
              disabled={!gitReady}
              title={snapshot?.git.error ?? 'Show Git working-tree changes'}
              onClick={() => selectGraphMode('Changes')}
            >
              <GitCompareArrows size={19} />
              <span className="rail-button__label">Git changes</span>
            </button>
            <button
              className={`rail-button ${workspaceSurface === 'engineering' ? 'is-active' : ''}`}
              type="button"
              aria-label="Engineering workspace"
              aria-pressed={workspaceSurface === 'engineering'}
              onClick={() => setWorkspaceSurface('engineering')}
            >
              <Workflow size={19} />
              <span className="rail-button__label">Workspace</span>
            </button>
          </nav>

          {browserWorkspaceId !== null && (
            <WorkspaceRailCatalog
              currentWorkspaceId={browserWorkspaceId}
              expanded={navigationExpanded}
            />
          )}

          <button
            className="rail-button rail-button--bottom"
            type="button"
            aria-label={`${navigationExpanded ? 'Collapse' : 'Expand'} navigation`}
            aria-expanded={navigationExpanded}
            title={`${navigationExpanded ? 'Collapse' : 'Expand'} navigation`}
            onClick={() => setNavigationExpanded((current) => !current)}
          >
            {navigationExpanded ? <PanelLeftClose size={18} /> : <PanelLeftOpen size={18} />}
            <span className="rail-button__label">Collapse</span>
          </button>
        </aside>
      )}

      <section className="workspace">
        <header className={`topbar ${graphMode === 'Live' && effectiveAgentActivityPlacement === 'topbar' ? 'topbar--activity-promoted' : ''}`}>
          <div>
            {browserWorkspaceId !== null && (
              <a className="workspace-back" href="/">
                <ArrowLeft size={13} /> All projects
              </a>
            )}
            <div className="breadcrumbs">
              <span>Workspace</span><ChevronRight size={13} />
              <strong>{snapshot?.metadata.workspaceName ?? 'Indexing architecture'}</strong>
            </div>
            <h1>{workspaceSurface === 'engineering' ? 'Engineering workspace' : 'Architecture in motion'}</h1>
          </div>

          <div className="topbar-statuses">
            {effectiveAgentActivityPlacement === 'topbar' && liveActivitySpotlight}
            {isMobileProjectView && (
              <>
                <MobileGraphScopeToggle
                  scope={mobileGraphScope}
                  onChange={changeMobileGraphScope}
                />
                <button
                  className="mobile-engineering-toggle"
                  type="button"
                  aria-label={workspaceSurface === 'engineering'
                    ? 'Return to live architecture'
                    : 'Open engineering workspace'}
                  aria-pressed={workspaceSurface === 'engineering'}
                  title={workspaceSurface === 'engineering'
                    ? 'Return to live architecture'
                    : 'Open engineering workspace'}
                  onClick={() => {
                    if (workspaceSurface === 'engineering') {
                      selectGraphMode('Live')
                    } else {
                      setWorkspaceSurface('engineering')
                    }
                  }}
                >
                  {workspaceSurface === 'engineering' ? <Activity size={17} /> : <Workflow size={17} />}
                </button>
              </>
            )}
            <UsageSpotlight
              info={info}
              error={infoError}
              onOpen={() => {
                setContextMenu(null)
                setInfoOpen(true)
                setConversationOpen(false)
              }}
            />
            <button
              className={`topbar-action ${conversationOpen ? 'is-active' : ''}`}
              type="button"
              aria-label="Open Codex conversation"
              aria-pressed={conversationOpen}
              onClick={() => {
                setContextMenu(null)
                setConversationOpen((current) => !current)
                setInfoOpen(false)
              }}
            >
              <MessageCircle size={14} /> Chat
            </button>
            <button
              className={`topbar-action ${infoOpen ? 'is-active' : ''}`}
              type="button"
              aria-label="Open CAVE information"
              aria-pressed={infoOpen}
              onClick={() => {
                setContextMenu(null)
                setInfoOpen((current) => !current)
                setConversationOpen(false)
              }}
            >
              <InfoIcon size={14} /> Info
            </button>
            <label
              className="theme-control"
              title="Auto follows Codex when embedded and the system color scheme in a browser."
            >
              <ThemeIcon size={14} />
              <select
                aria-label={`Color theme. Currently ${themeState.source} ${themeLabel(themeState.theme)}.`}
                value={themePreference}
                onChange={(event) => setThemePreference(event.target.value as ThemePreference)}
              >
                <option value="auto">Auto · {themeLabel(automaticTheme.theme)}</option>
                {caveThemeOptions.map((theme) => (
                  <option key={theme.id} value={theme.id}>{theme.label}</option>
                ))}
              </select>
            </label>
            <div className="source-status" title={`${feedStatusTitle} · ${feedStatusDetail}`}>
              <span className={`status-light ${feedIsLive ? 'is-live' : ''} ${feedConnection.status === 'Reconnecting' ? 'is-reconnecting' : ''}`} />
              <div>
                <strong>{feedStatusTitle}</strong>
                <span>{feedStatusDetail}</span>
              </div>
              {snapshot?.metadata.sourceKind === 'Sample' && <span className="sample-pill">Sample data</span>}
            </div>
          </div>
        </header>

        <div className="modebar" aria-label="Graph mode">
          <button
            className={`mode-button ${workspaceSurface === 'graph' && graphMode === 'Architecture' ? 'is-active' : ''}`}
            type="button"
            aria-pressed={workspaceSurface === 'graph' && graphMode === 'Architecture'}
            onClick={() => selectGraphMode('Architecture')}
          >
            <Boxes size={15} /> Architecture
          </button>
          <button
            className={`mode-button ${workspaceSurface === 'graph' && graphMode === 'Live' ? 'is-active' : ''}`}
            type="button"
            aria-pressed={workspaceSurface === 'graph' && graphMode === 'Live'}
            title="Observed Codex activity and separately declared agent scope"
            onClick={() => selectGraphMode('Live')}
          >
            <Radio size={15} /> Live activity
          </button>
          <button
            className={`mode-button ${workspaceSurface === 'graph' && graphMode === 'Changes' ? 'is-active' : ''}`}
            type="button"
            aria-pressed={workspaceSurface === 'graph' && graphMode === 'Changes'}
            disabled={!gitReady}
            title={snapshot?.git.error ?? 'Compare the working tree with the configured Git baseline'}
            onClick={() => selectGraphMode('Changes')}
          >
            <GitCompareArrows size={15} /> Changes
          </button>
          <button
            className={`mode-button ${workspaceSurface === 'engineering' ? 'is-active' : ''}`}
            type="button"
            aria-pressed={workspaceSurface === 'engineering'}
            onClick={() => setWorkspaceSurface('engineering')}
          >
            <Workflow size={15} /> Workspace
          </button>
          {workspaceSurface === 'graph' && (
            <>
          <span className="mode-separator" />
          <div className="projection-controls" role="group" aria-label="Semantic zoom level">
            <Layers3 size={14} aria-hidden="true" />
            <button
              type="button"
              aria-pressed={projectionMode === 'Auto'}
              onClick={() => selectProjectionMode('Auto')}
            >
              Auto
            </button>
            {projectionLevels.map((level) => (
              <button
                key={level}
                type="button"
                aria-pressed={projectionMode === level}
                onClick={() => selectProjectionMode(level)}
              >
                {level}
              </button>
            ))}
            <button
              className="collapse-hierarchy"
              type="button"
              disabled={hierarchyExpansion.expandedNodeIds.size === 0}
              title={hierarchyExpansion.expandedNodeIds.size === 0
                ? 'Expand a project or namespace to enable collapse all'
                : 'Collapse every expanded project and namespace'}
              onClick={collapseAllHierarchy}
            >
              <ChevronsUp size={12} aria-hidden="true" />
              <span>Collapse all</span>
            </button>
          </div>
          <div className="layout-density-controls" role="group" aria-label="Graph layout density">
            <span>Layout</span>
            {(['Normal', 'Tight'] as const).map((density) => (
              <button
                key={density}
                type="button"
                aria-pressed={layoutDensity === density}
                title={`${density} graph spacing`}
                onClick={() => selectLayoutDensity(density)}
              >
                {density}
              </button>
            ))}
          </div>
          <button
            className={`test-filter ${hideTests ? 'is-active' : ''}`}
            type="button"
            aria-pressed={hideTests}
            title={hideTests
              ? 'Show test projects and their classes'
              : 'Hide test projects and their classes'}
            onClick={toggleTests}
          >
            <TestTube2 size={13} />
            <span>{hideTests ? 'Tests hidden' : 'Tests shown'}</span>
            <b>{testProjectCount}</b>
          </button>
          <button
            className={`package-filter ${hideExternalPackages ? 'is-active' : ''}`}
            type="button"
            aria-pressed={hideExternalPackages}
            title={hideExternalPackages
              ? 'Show read-only external package nodes'
              : 'Hide read-only external package nodes'}
            onClick={toggleExternalPackages}
          >
            <PackageOpen size={13} />
            <span>{hideExternalPackages ? 'External hidden' : 'External shown'}</span>
            <b>{externalPackageCount}</b>
          </button>
          {graphMode === 'Changes' ? (
            <>
              <span className="graph-stat"><b>{projectedSnapshot?.git.nodes.length ?? 0}</b> changed nodes · {snapshot?.git.files.length ?? 0} files</span>
              <span className="graph-stat git-stat"><b>+{gitTotals.additions}</b> <i>−{gitTotals.deletions}</i></span>
              {snapshot?.git.baseline && (
                <span className="baseline-pill" title={snapshot.git.baseline.resolvedSha}>
                  vs {snapshot.git.baseline.reference}
                </span>
              )}
            </>
          ) : graphMode === 'Live' ? (
            <>
              {snapshot?.activity.sourceStatus === 'Unobserved' && (
                <span
                  className="baseline-pill activity-source-warning"
                  title="CAVE has not received a Codex hook event for this workspace. In a new task, open /hooks and trust the installed CAVE hooks."
                >
                  <CircleAlert size={11} /> Hooks not observed
                </span>
              )}
              {snapshot?.activity.sourceStatus === 'Degraded' && (
                <span
                  className="baseline-pill activity-source-warning"
                  title={snapshot.activity.error ?? 'One or more CAVE activity events could not be read.'}
                >
                  <CircleAlert size={11} /> Activity degraded
                </span>
              )}
              <div className="work-indicator-controls" role="group" aria-label="Work indicator reset">
                <button
                  className="work-indicator-reset"
                  type="button"
                  title="Clear visible active and completed work markers without deleting activity history"
                  onClick={resetVisibleWorkIndicators}
                >
                  <RotateCcw size={12} aria-hidden="true" /> Reset indicators
                </button>
                <label className="work-indicator-setting">
                  <span>Auto reset</span>
                  <select
                    aria-label="Automatically reset work indicators"
                    value={workIndicatorState.mode}
                    onChange={(event) => changeWorkIndicatorResetMode(
                      event.target.value as WorkIndicatorResetMode,
                    )}
                  >
                    {workIndicatorResetOptions.map((option) => (
                      <option key={option.value} value={option.value}>{option.label}</option>
                    ))}
                  </select>
                </label>
              </div>
            </>
          ) : (
            <>
              <span className="graph-stat"><b>{projectionLevel}</b> · {projectedSnapshot?.graph.nodes.length ?? 0} nodes</span>
              <span className="graph-stat"><b>{displayedRelationCount}</b> routes · {projectedSnapshot?.graph.relations.length ?? 0} relations</span>
            </>
          )}
          {liveSnapshot?.activityChanged && graphMode === 'Live' ? (
            <span className="live-change-pill">Activity updated</span>
          ) : liveSnapshot && liveSnapshot.changedPaths.length > 0 ? (
            <span className="live-change-pill">Updated {liveSnapshot.changedPaths.length} file{liveSnapshot.changedPaths.length === 1 ? '' : 's'}</span>
          ) : null}
            </>
          )}
        </div>

        {workspaceSurface === 'engineering' ? (
          <EngineeringWorkspace
            client={hostBridge}
            workspaceName={snapshot?.metadata.workspaceName ?? 'Engineering workspace'}
            onOpenArchitectureNode={(architectureNodeId) => {
              setSelectedNodeId(architectureNodeId)
              setDetailPanelOpen(true)
              selectGraphMode('Architecture')
            }}
          />
        ) : (
          <>
            {mobileUsesFocusedWork && liveActivitySpotlight !== null && (
              <section className="agent-activity-stage agent-activity-stage--mobile" aria-label="Current live agent activity">
                {liveActivitySpotlight}
              </section>
            )}

            <div className="architecture-work-surface">
            {snapshot !== null && graphMode !== 'Changes' && (
              <AgentWorkContext conversation={snapshot.conversation} agents={activeAgents} />
            )}
            <div className={`canvas-layout ${showDetailPanel ? 'canvas-layout--detail-open' : ''}`}>
          <section
            ref={canvasFrame}
            className={`canvas-frame ${graphMode === 'Changes' ? 'canvas-frame--changes' : ''} ${graphMode === 'Live' ? 'canvas-frame--live' : ''} ${graphMode === 'Live' ? `canvas-frame--activity-${effectiveAgentActivityPlacement}` : ''}`}
            aria-label={graphMode === 'Changes' ? 'Git changes graph' : graphMode === 'Live' ? 'Live agent activity graph' : 'Architecture graph'}
          >
            {!isMobileProjectView && effectiveAgentActivityPlacement === 'canvas' && liveActivitySpotlight !== null && (
              <section className="agent-activity-stage" aria-label="Current live agent activity">
                {liveActivitySpotlight}
              </section>
            )}
          {snapshot === null ? (
            <LoadingState connection={feedConnection} />
          ) : mobileUsesFocusedWork && nodes.length === 0 ? (
            <MobileWorkEmptyState
              activeAgentCount={activeAgents.length}
              unmappedCount={snapshot.activity.unmappedPaths.length}
            />
          ) : nodes.length === 0 ? (
            <LoadingState connection={feedConnection} />
          ) : (
            <ReactFlow
              nodes={nodes}
              edges={displayedEdges}
              nodeTypes={nodeTypes}
              edgeTypes={edgeTypes}
              onInit={(instance) => {
                flowInstance.current = instance
                updateNodeReadability(instance.getZoom())
              }}
              onNodesChange={(changes: NodeChange<ArchitectureFlowNode>[]) =>
                setNodes((current) => applyNodeChanges(changes, current))
              }
              onEdgesChange={(changes: EdgeChange<ArchitectureFlowEdge>[]) =>
                setEdges((current) => applyEdgeChanges(changes, current))
              }
              onNodeClick={(_, node) => {
                setContextMenu(null)
                setSelectedNodeId(node.id)
                if (!isMobileProjectView) setDetailPanelOpen(true)
              }}
              onNodeContextMenu={(event, node) => {
                event.preventDefault()
                setSelectedNodeId(node.id)
                setContextMenu({ nodeId: node.id, x: event.clientX, y: event.clientY })
              }}
              onPaneClick={() => setContextMenu(null)}
              onMoveStart={(event) => {
                if (event !== null && projectionMode === 'Auto') {
                  setAutoViewState(pauseAutoView)
                }
              }}
              onMove={(_, viewport) => updateNodeReadability(viewport.zoom)}
              onMoveEnd={(_, viewport) => {
                updateNodeReadability(viewport.zoom)
                setCurrentZoom(viewport.zoom)
                if (
                  projectionMode === 'Auto'
                  && autoViewState.committedPlan === null
                  && hierarchyExpansion.focusNodeId === null
                ) {
                  setProjectionLevel((current) => nextAutoProjectionLevel(viewport.zoom, current))
                }
              }}
              fitView
              fitViewOptions={{ padding: 0.12, maxZoom: 0.72 }}
              minZoom={0.1}
              maxZoom={1.8}
              proOptions={{ hideAttribution: false }}
            >
              <Background variant={BackgroundVariant.Dots} gap={24} size={1} />
              <MiniMap
                className="cave-minimap"
                nodeColor={(node) => miniMapNodeColor(node.data as ArchitectureNodeData, graphMode)}
                maskColor="var(--minimap-mask)"
              />
              <Controls className="cave-controls" position="top-left" showInteractive={false} />
            </ReactFlow>
          )}

          {contextMenu !== null && contextNode !== null && (
            <NodeContextMenu
              node={contextNode}
              x={contextMenu.x}
              y={contextMenu.y}
              canAsk={canAskNode}
              executionHint={nodeQuestionHint}
              onClose={() => setContextMenu(null)}
              onExplain={() => runNodeMemo(
                contextNode,
                `Explain ${contextNode.name} and why it matters in this architecture.`,
                'Explain',
              )}
              onAsk={() => {
                setAskNodeId(contextNode.id)
                setContextMenu(null)
              }}
            />
          )}
          <div className="canvas-caption">
            {graphMode === 'Changes' ? (
              <>
                <span><GitCompareArrows size={13} /> working tree vs {snapshot?.git.baseline?.reference}</span>
                <span><i className="legend-swatch legend-swatch--added" /> added</span>
                <span><i className="legend-swatch legend-swatch--modified" /> modified</span>
                <span><i className="legend-swatch legend-swatch--deleted" /> deleted</span>
                <span><b className="legend-addition">+ additions</b> <i className="legend-deletion">− deletions</i></span>
                {(snapshot?.git.unmappedFiles.length ?? 0) > 0 && (
                  <span><CircleAlert size={13} /> {snapshot?.git.unmappedFiles.length} unmapped</span>
                )}
              </>
            ) : graphMode === 'Live' ? (
              <>
                <span><AgentAvatar phase="Working" active size={16} /> main agent working</span>
                <span><AgentAvatar phase="Working" active isSubagent size={16} /> subagent working</span>
                <span><i className="legend-swatch legend-swatch--worked" /> last box worked on</span>
                <span><i className="legend-line legend-line--activity-input" /> inputs → active work</span>
                <span><i className="legend-line legend-line--activity-output" /> active work → outputs</span>
                <span><CircleAlert size={13} /> animated dependencies, not runtime traffic</span>
              </>
            ) : (
              <>
                <span><Layers3 size={13} /> {projectionLevel} projection</span>
                <span><i className="legend-line legend-line--solid" /> consumer → provider</span>
                <span><i className="legend-line legend-line--inheritance" /> inheritance</span>
                <span><i className="legend-line legend-line--implementation" /> implementation</span>
                <span><i className="legend-line legend-line--http-api" /> inferred HTTP / SSE contract</span>
                <span><i className="legend-line legend-line--soft" /> parent → child ownership</span>
                <span><CircleAlert size={13} /> static dependencies, not runtime flow</span>
              </>
            )}
          </div>
          {feedConnection.status === 'Reconnecting' ? (
            <div className="refresh-error refresh-error--reconnecting">
              CAVE disconnected. Reattaching automatically in {formatRetryDelay(feedConnection.retryInMs)}.
              {snapshot !== null && ' The last snapshot remains visible.'}
            </div>
          ) : error !== null ? (
            <div className="refresh-error">Live refresh failed: {error}</div>
          ) : null}
          </section>

          {showDetailPanel && (
            <DetailPanel
              node={selectedNode}
              snapshot={indicatorSnapshot ?? projectedSnapshot ?? snapshot}
              graphMode={graphMode}
              gitDelta={selectedGitDelta}
              onClose={() => setDetailPanelOpen(false)}
            />
          )}
            </div>
            </div>
          </>
        )}
        {interactionError !== null && (
          <div className="interaction-error" role="alert">
            {interactionError}
            <button type="button" aria-label="Dismiss interaction error" onClick={() => setInteractionError(null)}><X size={12} /></button>
          </div>
        )}
        {conversationOpen && snapshot !== null && (
          <ConversationPanel
            conversation={snapshot.conversation}
            canSend={hostBridge?.canSendMessage === true
              && (isEmbedded || snapshot.conversation.control.canSend)}
            canManageSharing={hostBridge !== null}
            busy={interactionBusy}
            onClose={() => setConversationOpen(false)}
            onSend={sendConversationMessage}
            onSetSharing={setConversationSharing}
          />
        )}
        {infoOpen && <InfoPanel info={info} error={infoError} onClose={() => setInfoOpen(false)} />}
        {askNode !== null && (
          <AskNodeDialog
            node={askNode}
            submitLabel="Run in temporary side chat"
            onCancel={() => setAskNodeId(null)}
            onAsk={(question) => runNodeMemo(askNode, question, 'Ask')}
          />
        )}
        {nodeMemo !== null && (
          <NodeMemoDialog memo={nodeMemo} onClose={() => setNodeMemo(null)} />
        )}
      </section>
    </main>
  )
}

function preferredNode(
  snapshot: ArchitectureSnapshot,
  level: ProjectionLevel,
): ArchitectureNode | undefined {
  const preferredKind: ArchitectureNode['kind'] = {
    System: 'ArchitectureGroup',
    Projects: 'Project',
    Namespaces: 'Namespace',
    Classes: 'Class',
  }[level] as ArchitectureNode['kind']

  return snapshot.graph.nodes.find((node) => node.kind === preferredKind)
    ?? snapshot.graph.nodes[0]
}

function DetailPanel({
  node,
  snapshot,
  graphMode,
  gitDelta,
  onClose,
}: {
  node: ArchitectureNode | null
  snapshot: ArchitectureSnapshot | null
  graphMode: GraphMode
  gitDelta: GitNodeDelta | null
  onClose: () => void
}) {
  const incomingRelations = snapshot?.graph.relations.filter((edge) => edge.targetId === node?.id) ?? []
  const outgoingRelations = snapshot?.graph.relations.filter((edge) => edge.sourceId === node?.id) ?? []
  const incoming = incomingRelations.length
  const outgoing = outgoingRelations.length
  const children = snapshot?.graph.nodes.filter((candidate) => candidate.parentId === node?.id).length ?? 0
  const nodeActivities = snapshot?.activity.nodes.filter((activity) => activity.nodeId === node?.id) ?? []
  const nodeAgentIds = new Set(nodeActivities.map((activity) => activity.agentId))
  const nodeAgents = snapshot?.activity.agents.filter((agent) => nodeAgentIds.has(agent.agentId)) ?? []
  const nodesById = new Map(snapshot?.graph.nodes.map((candidate) => [candidate.id, candidate]) ?? [])
  const inputs = [...new Map(outgoingRelations
    .map((relation) => nodesById.get(relation.targetId))
    .filter((candidate) => candidate !== undefined)
    .map((candidate) => [candidate.id, candidate])).values()]
  const outputs = [...new Map(incomingRelations
    .map((relation) => nodesById.get(relation.sourceId))
    .filter((candidate) => candidate !== undefined)
    .map((candidate) => [candidate.id, candidate])).values()]
  const visual = node === null ? null : architectureNodeVisual(node)
  const DetailIcon = visual?.Icon ?? Boxes
  const documentation = node === null ? null : nodeDocumentationSummary(node)

  return (
    <aside className="detail-panel">
      <div className="detail-header">
        <div className="detail-kicker"><Sparkles size={14} /> Selection</div>
        <button className="detail-close" type="button" aria-label="Close selection details" onClick={onClose}>
          <X size={15} />
        </button>
      </div>
      {node === null ? (
        <p className="detail-empty">Select an architecture node to inspect its evidence.</p>
      ) : (
        <>
          <div className={`detail-icon detail-icon--${node.categoryId ?? 'default'} ${visual?.className ?? ''}`}>
            <DetailIcon size={22} />
          </div>
          <span className="detail-type">{visual?.label}</span>
          <h2>{node.name}</h2>
          <section className={`detail-documentation ${node.description?.trim() ? '' : 'is-empty'}`}>
            <header><BookOpen size={13} /> Documentation</header>
            <p>{documentation}</p>
          </section>

          {graphMode === 'Changes' ? (
            <dl className="metric-grid metric-grid--git">
              <div><dt>Files</dt><dd>{gitDelta?.changedFiles ?? 0}</dd></div>
              <div><dt>Additions</dt><dd className="metric-addition">+{gitDelta?.additions ?? 0}</dd></div>
              <div><dt>Deletions</dt><dd className="metric-deletion">−{gitDelta?.deletions ?? 0}</dd></div>
            </dl>
          ) : graphMode === 'Live' ? (
            <dl className="metric-grid">
              <div><dt>Agents</dt><dd>{nodeAgents.length}</dd></div>
              <div><dt>Inputs</dt><dd>{outgoing}</dd></div>
              <div><dt>Outputs</dt><dd>{incoming}</dd></div>
            </dl>
          ) : (
            <dl className="metric-grid">
              <div><dt>Children</dt><dd>{children}</dd></div>
              <div><dt>Incoming</dt><dd>{incoming}</dd></div>
              <div><dt>Outgoing</dt><dd>{outgoing}</dd></div>
            </dl>
          )}

          {graphMode === 'Changes' && (
            <div className={`git-evidence-card ${gitDelta === null ? 'is-empty' : `git-kind--${gitDelta.kind.toLowerCase()}`}`}>
              <header><GitCompareArrows size={13} /> Git evidence</header>
              {gitDelta === null ? (
                <p>No working-tree changes map to this node.</p>
              ) : (
                <>
                  <strong>{gitDelta.kind} · {gitDelta.changedFiles} changed file{gitDelta.changedFiles === 1 ? '' : 's'}</strong>
                  <span className="git-baseline-detail">
                    Baseline {snapshot?.git.baseline?.reference} · {snapshot?.git.baseline?.resolvedSha.slice(0, 8)}
                  </span>
                  <ul>
                    {gitDelta.paths.slice(0, 6).map((path) => <li key={path} title={path}>{path}</li>)}
                  </ul>
                  {gitDelta.paths.length > 6 && <small>+{gitDelta.paths.length - 6} more files</small>}
                </>
              )}
            </div>
          )}

          {graphMode === 'Live' && (
            <>
              <div className="activity-evidence-card">
                <header><Activity size={13} /> Agent activity</header>
                {nodeAgents.length === 0 ? (
                  <p>No observed activity or declared scope maps to this node.</p>
                ) : (
                  <ul className="agent-activity-list">
                    {nodeAgents.map((agent) => (
                      <li key={agent.agentId}>
                        <AgentAvatar
                          phase={agent.phase}
                          active={agent.state === 'Active'}
                          isSubagent={agent.isSubagent}
                          size={30}
                          className="activity-agent-avatar"
                        />
                        <span>
                          <strong>
                            {agentDisplayName(agent)}
                            <i>{agent.state}{agent.state === 'Active' && agent.phase !== null ? ` · ${agent.phase}` : ''}</i>
                          </strong>
                          <small>{agent.summary ?? 'No work summary supplied.'}</small>
                          <em>{agent.hasObservedActivity ? 'Observed session activity' : ''}{agent.hasObservedActivity && agent.hasDeclaredScope ? ' · ' : ''}{agent.hasDeclaredScope ? 'Agent-declared scope' : ''}</em>
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
              </div>

              <div className="dependency-flow-card">
                <header><ArrowDownToLine size={13} /> Inputs · consumes</header>
                <DependencyList nodes={inputs} empty="No outgoing semantic dependencies at this projection." />
                <header><ArrowUpFromLine size={13} /> Outputs · used by</header>
                <DependencyList nodes={outputs} empty="No incoming semantic dependents at this projection." />
                <p>Static semantic dependency evidence; animation shows relevance to active work, not runtime traffic.</p>
              </div>
            </>
          )}

          <div className="evidence-card">
            <header><span className="evidence-status" /> Evidence</header>
            <strong>{snapshot?.metadata.sourceKind === 'CodeGraph' ? 'Live CodeGraph evidence' : 'Explicit sample evidence'}</strong>
            <p>{snapshot?.metadata.sourceKind === 'CodeGraph' ? 'Projected from indexed declarations and semantic relations in the current workspace.' : 'Acceptance-spike data; it is not presented as a live workspace observation.'}</p>
          </div>

          {graphMode !== 'Live' && (
            <div className="intent-placeholder">
              <span>Recorded intent</span>
              <p>No agent-authored rationale has been recorded for this node.</p>
            </div>
          )}
        </>
      )}
    </aside>
  )
}

function DependencyList({
  nodes,
  empty,
}: {
  nodes: ArchitectureNode[]
  empty: string
}) {
  if (nodes.length === 0) {
    return <span className="dependency-empty">{empty}</span>
  }

  return (
    <ul className="dependency-list">
      {nodes.slice(0, 6).map((node) => <li key={node.id}>{node.name}</li>)}
      {nodes.length > 6 && <li>+{nodes.length - 6} more</li>}
    </ul>
  )
}

function miniMapNodeColor(data: ArchitectureNodeData, graphMode: GraphMode): string {
  if (graphMode === 'Architecture') {
    return data.architecture.kind === 'ArchitectureGroup' ? '#826cf6' : '#27b7aa'
  }

  if (graphMode === 'Live') {
    const activeDecoration = data.agentDecorations.find((decoration) => decoration.state === 'Active')
    if (activeDecoration !== undefined) {
      return activeDecoration.agent.isSubagent ? '#826cf6' : '#27b7aa'
    }

    const workedDecoration = data.agentDecorations.find((decoration) => decoration.state === 'Worked')
    return workedDecoration === undefined
      ? '#8b909d'
      : workedDecoration.agent.isSubagent ? '#afa5f7' : '#82c8c1'
  }

  switch (data.gitDelta?.kind) {
    case 'Added': return '#27b7aa'
    case 'Deleted': return '#f28a69'
    case 'Modified': return '#826cf6'
    case 'Mixed': return '#d7913b'
    default: return '#8b909d'
  }
}

function LoadingState({ connection }: { connection: ArchitectureFeedConnectionState }) {
  const message = connection.status === 'Reconnecting'
    ? `Reattaching automatically · attempt ${connection.attempt}`
    : 'Waiting for the local CAVE host'
  return (
    <div className="loading-state">
      <span className="loading-orbit"><Boxes size={22} /></span>
      <strong>{connection.status === 'Reconnecting' ? 'CAVE connection lost' : 'Projecting architecture'}</strong>
      <span>{message}</span>
    </div>
  )
}

function MobileWorkEmptyState({
  activeAgentCount,
  unmappedCount,
}: {
  activeAgentCount: number
  unmappedCount: number
}) {
  const hasActiveAgent = activeAgentCount > 0
  return (
    <div className="mobile-work-empty">
      <AgentAvatar phase={hasActiveAgent ? 'Working' : null} active={hasActiveAgent} size={48} />
      <strong>{hasActiveAgent ? 'Active work is not mapped yet' : 'Waiting for active work'}</strong>
      <span>
        {hasActiveAgent
          ? `${activeAgentCount} agent${activeAgentCount === 1 ? '' : 's'} active${unmappedCount > 0 ? ` · ${unmappedCount} paths still unmapped` : ''}. The focused graph will appear as soon as CAVE locates the work.`
          : 'The mobile canvas intentionally shows only the current work area. It will populate with the active box and its immediate context.'}
      </span>
    </div>
  )
}

function useStandaloneMobileProjectView(isEmbedded: boolean): boolean {
  const query = '(max-width: 780px)'
  const read = () => !isEmbedded
    && typeof window !== 'undefined'
    && window.matchMedia(query).matches
  const [matches, setMatches] = useState(read)

  useEffect(() => {
    if (isEmbedded || typeof window === 'undefined') {
      setMatches(false)
      return
    }

    const media = window.matchMedia(query)
    const update = () => setMatches(media.matches)
    update()
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [isEmbedded])

  return matches
}

function formatRetryDelay(retryInMs: number): string {
  return retryInMs < 1_000 ? `${retryInMs} ms` : `${retryInMs / 1_000} s`
}

function readHideExternalPackages(): boolean {
  try {
    const current = window.localStorage.getItem(externalPackageFilterPreferenceKey)
    return current === null
      ? window.localStorage.getItem(legacyNuGetFilterPreferenceKey) === 'true'
      : current === 'true'
  } catch {
    return false
  }
}

function writeHideExternalPackages(hidePackages: boolean): void {
  try {
    window.localStorage.setItem(externalPackageFilterPreferenceKey, String(hidePackages))
  } catch {
    // Storage can be unavailable in sandboxed embedded hosts; session state still works.
  }
}

function readLayoutDensity(): LayoutDensity {
  try {
    return window.localStorage.getItem(layoutDensityPreferenceKey) === 'Tight'
      ? 'Tight'
      : 'Normal'
  } catch {
    return 'Normal'
  }
}

function writeLayoutDensity(density: LayoutDensity): void {
  try {
    window.localStorage.setItem(layoutDensityPreferenceKey, density)
  } catch {
    // Storage can be unavailable in sandboxed embedded hosts; session state still works.
  }
}

function readAgentActivityPlacement(): AgentActivityPlacementMode {
  try {
    return window.localStorage.getItem(agentActivityPlacementPreferenceKey) === 'topbar'
      ? 'topbar'
      : 'canvas'
  } catch {
    return 'canvas'
  }
}

function writeAgentActivityPlacement(placement: AgentActivityPlacementMode): void {
  try {
    window.localStorage.setItem(agentActivityPlacementPreferenceKey, placement)
  } catch {
    // Storage can be unavailable in sandboxed embedded hosts; session state still works.
  }
}

function ErrorState({ message }: { message: string }) {
  return (
    <main className="error-state">
      <CircleAlert size={32} />
      <h1>CAVE could not load the architecture snapshot.</h1>
      <p>{message}</p>
      <code>dotnet run --project src/Cave.Host</code>
    </main>
  )
}

function App() {
  const isEmbedded = window.parent !== window
  const selectedWorkspaceId = new URLSearchParams(window.location.search).get('workspace')?.trim() || null
  if (!isEmbedded && window.location.pathname === '/activity/light') {
    return <AgentFlowLightPage />
  }
  if (!isEmbedded && selectedWorkspaceId === null && window.location.pathname.startsWith('/settings')) {
    return <SettingsPage />
  }
  if (!isEmbedded && selectedWorkspaceId === null && window.location.pathname === '/activity') {
    return <MachineActivityPage />
  }

  return isEmbedded || selectedWorkspaceId !== null
    ? (
        <ArchitectureApp
          browserWorkspaceId={isEmbedded ? null : selectedWorkspaceId}
          isEmbedded={isEmbedded}
        />
      )
    : <WorkspaceDashboard />
}

export default App
