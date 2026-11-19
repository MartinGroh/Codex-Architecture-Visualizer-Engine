import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { Background, Controls, getNodesBounds, getViewportForBounds, Handle, Panel, Position, ReactFlow, useReactFlow, useStore, type NodeProps, type Rect } from '@xyflow/react'
import { BookOpen, Brain, Check, CircleAlert, Clock3, Cog, GitFork, GitMerge, ListTree, Minus, Network, Pause, Pencil, Plus, ShieldCheck, X } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { AgentInspector, type AgentInspectorTarget } from './AgentInspector'
import { AgentFlowBubble } from './AgentFlowBubble'
import { useAgentFlowBubbles } from './agentFlowBubbles'
import { useAgentChat } from './agentChat'
import { MainGoal } from './AgentWorkContext'
import { AgentModelLabel } from './AgentModel'
import { agentModelFamily } from './agentModelPresentation'
import { agentDisplayName, agentRoleLabel } from './agentIdentity'
import { agentFlowTopology, flowOperation, layoutAgentFlow, type AgentFlowLayout, type AgentFlowLayoutMode, type AgentFlowNode, type FlowOperation } from './agentFlowGraphModel'
import { observedTaskRoot, type MachineActivityLane, type MachineActivityProjectTimeline } from './machineActivityTimeline'
import './AgentFlowGraph.css'

const operationIcons = { Thinking: Brain, Reading: BookOpen, Editing: Pencil, Validating: ShieldCheck, Working: Cog, Completed: Check, Idle: Pause, Planned: Clock3 }
const nodeTypes = { hub: ProjectHub, agent: AgentCard, bubble: WorkBubble }
const frame = { minZoom: 0.2, maxZoom: 1.25, padding: .08 }
const layoutChoices = [
  { id: 'mindmap', label: 'Mind map', icon: GitFork },
  { id: 'tree', label: 'Org tree', icon: Network },
  { id: 'flow', label: 'Flow', icon: GitMerge },
] as const
const layoutSetting = 'cave.agentFlow.graphLayout'
const noLanes: MachineActivityLane[] = []

function NodeHandles({ sourcePosition, targetPosition, data, type }: NodeProps<AgentFlowNode>) {
  return <>
    {type !== 'bubble' && <Handle type="source" position={sourcePosition ?? Position.Right} />}
    {data.alternateSourcePosition && <Handle id="alternate" type="source" position={data.alternateSourcePosition} />}
    {type !== 'hub' && <Handle type="target" position={targetPosition ?? Position.Left} />}
  </>
}

function ProjectHub(props: NodeProps<AgentFlowNode>) {
  return <div className="flow2-hub">
    <NodeHandles {...props} />
    <span className="flow2-hub__mark"><Network size={26} strokeWidth={1.75} aria-hidden="true" /></span>
    <div><strong title={props.data.name}>{props.data.name}</strong><small>Project hub</small></div>
  </div>
}
function OperationLabel({ operation }: { operation: FlowOperation }) {
  const Icon = operationIcons[operation]
  return <span className={`flow2-operation flow2-operation-${operation.toLowerCase()}`}><Icon size={14} aria-hidden="true" />{operation}</span>
}
function AgentCard(props: NodeProps<AgentFlowNode>) {
  const lane = props.data.lane
  if (!lane) return null
  const operation = flowOperation(lane)
  const name = agentDisplayName(lane.agent)
  const role = agentRoleLabel(lane.agent)
  const summary = lane.agent.summary ?? lane.agent.lastObservedActivity ?? 'No work detail observed'
  const work = props.data.workBranch
  const FoldIcon = work?.expanded ? Minus : Plus
  return <div className={`flow2-node flow2-operation-${operation.toLowerCase()}`} data-main={!lane.agent.isSubagent || undefined}>
    <NodeHandles {...props} />
    <div className="flow2-node__identity">
      <div className="flow2-node__portrait"><AgentAvatar phase={lane.agent.phase} active={lane.active} isSubagent={lane.agent.isSubagent} size={32} /></div>
      <div className="flow2-node__name"><strong title={name}>{name}</strong>{role !== name && <small title={role}>{role}</small>}</div>
      {work && <button type="button" className="flow2-fold nodrag nopan" aria-expanded={work.expanded}
        aria-label={`${work.expanded ? 'Collapse' : 'Expand'} work for ${name}, ${role}`}
        title={`${work.expanded ? 'Collapse' : 'Expand'} ${work.count} work bubbles`}
        onKeyDown={(event) => event.stopPropagation()} onDoubleClick={(event) => event.stopPropagation()}
        onClick={(event) => { event.stopPropagation(); work.toggle(props.id) }}><FoldIcon size={13} />{work.count}</button>}
    </div>
    <p title={summary}>{summary}</p>
    <footer><OperationLabel operation={operation} /><AgentModelLabel configuration={lane.agent.configuration} /></footer>
  </div>
}

function WorkBubble(props: NodeProps<AgentFlowNode>) {
  const { bubble, artifactUrl } = props.data
  if (!bubble || !artifactUrl) return null
  return <><NodeHandles {...props} /><AgentFlowBubble bubble={bubble} artifactUrl={artifactUrl} /></>
}

interface BranchFocus { agentId: string }
function CanvasFraming({ bounds, arrangement, focus, nodes }: { bounds: Rect; arrangement: string; focus: BranchFocus | null; nodes: AgentFlowNode[] }) {
  const width = useStore((state) => state.width)
  const height = useStore((state) => state.height)
  const flow = useReactFlow<AgentFlowNode>()
  const flowElement = useStore((state) => state.domNode)
  const lastFrame = useRef<{ width: number; height: number; bounds: Rect; arrangement: string } | null>(null)
  const lastFocus = useRef<BranchFocus | null>(null)
  useLayoutEffect(() => {
    if (!flow.viewportInitialized || !width || !height) return
    const previous = lastFrame.current
    lastFrame.current = { width, height, bounds, arrangement }
    if (previous?.width === width && previous.height === height && previous.bounds === bounds && previous.arrangement === arrangement) return
    const fit = (area: Rect, w: number, h: number) => getViewportForBounds(area, w, h, frame.minZoom, frame.maxZoom, frame.padding)
    if (!previous || previous.arrangement !== arrangement) {
      void flow.setViewport(fit(bounds, width, height))
      return
    }
    const camera = flow.getViewport()
    const before = fit(previous.bounds, previous.width, previous.height)
    // Keep a fitted overview fitted. A hand-positioned view keeps the same world point under its center.
    const wasFitted = Math.abs(before.x - camera.x) < 1 && Math.abs(before.y - camera.y) < 1 && Math.abs(before.zoom - camera.zoom) < .001
    void flow.setViewport(wasFitted ? fit(bounds, width, height) : {
      x: camera.x + (width - previous.width) / 2,
      y: camera.y + (height - previous.height) / 2,
      zoom: camera.zoom,
    })
  }, [flow, width, height, bounds, arrangement])
  useLayoutEffect(() => {
    if (!focus || focus === lastFocus.current || !flow.viewportInitialized || !width || !height) return
    const branch = nodes.filter((node) => node.id === focus.agentId || node.data.bubble?.parent === focus.agentId)
    if (!branch.length) return
    lastFocus.current = focus
    void flow.setViewport(getViewportForBounds(getNodesBounds(branch), width, height, frame.minZoom, frame.maxZoom, frame.padding)).then(() => {
      const owner = Array.from(flowElement?.querySelectorAll<HTMLElement>('.react-flow__node-agent') ?? [])
        .find((node) => node.dataset.id === focus.agentId)
      owner?.focus({ preventScroll: true })
      flowElement?.scrollIntoView({ block: 'nearest' })
    })
  }, [flow, flowElement, width, height, nodes, focus])
  return null
}

/** Second, spatial flow presentation over the existing evidence-only timeline. */
export function AgentFlowGraph({ timeline, lanes, focused, onOpenWall }: {
  timeline: MachineActivityProjectTimeline
  lanes: MachineActivityLane[]
  focused: boolean
  onOpenWall: (taskId: string) => void
}) {
  const [layout, setLayout] = useState<(AgentFlowLayout & { arrangement: string; mode: AgentFlowLayoutMode; topologyKey: string }) | null>(null)
  const [layoutError, setLayoutError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [target, setTarget] = useState<AgentInspectorTarget>()
  const [mode, setMode] = useState<AgentFlowLayoutMode>(() => {
    try { return layoutChoices.find((choice) => choice.id === localStorage.getItem(layoutSetting))?.id ?? 'mindmap' }
    catch { return 'mindmap' }
  })
  const [workOverride, setWorkOverride] = useState<boolean>()
  const showWork = workOverride ?? focused
  const [folded, setFolded] = useState<ReadonlySet<string>>(() => new Set())
  const [focus, setFocus] = useState<BranchFocus | null>(null)
  const toggleBranch = useCallback((id: string) => setFolded((previous) => {
    const next = new Set(previous)
    if (next.has(id)) next.delete(id)
    else next.add(id)
    return next
  }), [])
  const [followingTask, setFollowingTask] = useState<string | null>(null)
  const picker = useRef<HTMLSelectElement>(null)
  const [inspectorWidth, setInspectorWidth] = useState<number | null>(null)
  const selected = lanes.find((lane) => lane.agent.agentId === selectedId)
  const selectedTask = selected ? observedTaskRoot(lanes, selected.agent.agentId)?.agent.publicId ?? null : null
  const roots = lanes.filter(({ agent }) => agent.hasObservedActivity && !agent.isSubagent && agent.publicId)
  const workTask = roots.find(({ agent }) => agent.publicId === followingTask)?.agent.publicId
    ?? (roots.length === 1 ? roots[0].agent.publicId : null)
  // One task feed serves the inspector and every visible work bubble in this graph.
  const taskId = selected ? selectedTask : showWork ? workTask : null
  const chat = useAgentChat(timeline.workspace.workspaceId, taskId, timeline.sharingEnabled)
  const bubbles = useAgentFlowBubbles(showWork ? lanes : noLanes, chat, taskId)
  const bubbleById = useMemo(() => new Map(bubbles.filter(({ parent }) => !folded.has(parent)).map((bubble) => [bubble.id, bubble])), [bubbles, folded])
  const counts = useMemo(() => {
    const totals = new Map<string, number>()
    for (const { parent } of bubbles) totals.set(parent, (totals.get(parent) ?? 0) + 1)
    return totals
  }, [bubbles])
  const topologyKey = JSON.stringify([agentFlowTopology(lanes), Array.from(bubbleById.values(), ({ id, parent, kind }) => ({ id, parent, kind }))])
  const arrangement = mode + (showWork ? '-work' : '-agents')
  useEffect(() => {
    let disposed = false
    setLayoutError(null)
    const [agents, leaves] = JSON.parse(topologyKey)
    void layoutAgentFlow(agents, mode, leaves).then((next) => { if (!disposed) setLayout({ ...next, arrangement, mode, topologyKey }) })
      .catch((error: unknown) => { if (!disposed) { setLayout(null); setLayoutError(error instanceof Error ? error.message : 'Layout unavailable') } })
    return () => { disposed = true }
  }, [topologyKey, mode, arrangement])
  const bounds = useMemo(() => getNodesBounds(layout?.nodes ?? []), [layout])
  const nodes = useMemo(() => {
    const agents = new Map(lanes.map((lane) => [lane.agent.agentId, lane]))
    return (layout?.nodes ?? []).flatMap((node): AgentFlowNode[] => {
      const bubble = bubbleById.get(node.id)
      const lane = agents.get(bubble?.parent ?? node.id)
      // An async layout may finish after sharing changes. Geometry must never retain revoked content.
      if ((node.type === 'bubble' && !bubble) || (node.type === 'agent' && !lane)) return []
      const media = bubble?.kind === 'image' || bubble?.kind === 'diagram'
      const owner = lane ? agentDisplayName(lane.agent) : timeline.workspace.name
      const count = counts.get(node.id)
      return [{ ...node, selected: node.id === selectedId, ariaRole: media || count ? 'group' : 'button',
        ariaLabel: bubble ? `${owner}, ${bubble.label}: ${bubble.text}` : lane ? `Inspect ${owner}, ${agentRoleLabel(lane.agent)}` : `Inspect ${owner} project`,
        domAttributes: bubble ? undefined : { 'aria-expanded': node.id === selectedId },
        data: { ...node.data, name: timeline.workspace.name, lane, bubble, artifactUrl: chat.artifactUrl,
          workBranch: count ? { count, expanded: !folded.has(node.id), toggle: toggleBranch } : undefined },
      }]
    })
  }, [layout, lanes, timeline.workspace.name, selectedId, bubbleById, chat.artifactUrl, counts, folded, toggleBranch])
  const edges = useMemo(() => {
    const visible = new Map(nodes.map((node) => [node.id, node]))
    return (layout?.edges ?? []).filter((edge) => visible.has(edge.source) && visible.has(edge.target)).map((edge) => {
      const highlighted = selectedId === edge.target || selectedId === edge.source
      const family = agentModelFamily(visible.get(edge.target)?.data.lane?.agent.configuration?.model)
      return { ...edge, className: `${edge.className} model-family-${family}${highlighted ? ' flow2-route--selected' : ''}`,
        zIndex: highlighted ? 1 : 0,
        markerEnd: typeof edge.markerEnd === 'object' ? { ...edge.markerEnd, color: 'context-stroke' } : edge.markerEnd }
    })
  }, [layout, selectedId, nodes])
  function inspect(id: string | null) {
    const bubble = id ? bubbleById.get(id) : undefined
    const agentId = bubble?.parent ?? id
    setSelectedId(agentId)
    setTarget(bubble ? { agentId: bubble.parent, section: bubble.messageId ? 'Conversation' : 'Activity',
      message: bubble.messageId ? { messageId: bubble.messageId, periodId: chat.periodId, sharing: chat.sharing } : undefined } : undefined)
    if (agentId && agentId !== 'project-hub') {
      const root = observedTaskRoot(lanes, agentId)
      if (root) setFollowingTask(root.agent.publicId ?? null)
    }
  }
  function chooseMode(next: AgentFlowLayoutMode) {
    setMode(next)
    try { localStorage.setItem(layoutSetting, next) } catch { /* The current view still accepts the choice when browser storage is restricted. */ }
  }
  const closeAgent = () => {
    setSelectedId(null)
    picker.current?.focus()
  }
  function focusBranch() {
    if (!selected) return
    const agentId = selected.agent.agentId
    setWorkOverride(true)
    if (folded.has(agentId)) toggleBranch(agentId)
    setFocus({ agentId })
    closeAgent()
  }
  const labels = lanes.map(({ agent }) => {
    const name = agentDisplayName(agent)
    const role = agentRoleLabel(agent)
    return name === role ? name : `${name} · ${role}`
  })
  const agentOptions = lanes.map(({ agent }, index) => {
    const label = labels[index]
    const repeated = labels.indexOf(label) !== labels.lastIndexOf(label)
    return <option key={agent.agentId} value={agent.agentId}>{repeated ? `${label} · ${index + 1}` : label}</option>
  })
  const operations: FlowOperation[] = ['Thinking', 'Reading', 'Editing', 'Validating', 'Working', 'Idle', 'Completed']
  return <div className="flow2" data-layout={layout?.mode ?? mode}>
    <div className="flow2-toolbar">
      <button className="flow2-goal" type="button" aria-pressed={selectedId === 'project-hub'} onClick={() => setSelectedId(selectedId === 'project-hub' ? null : 'project-hub')}>
        <strong>Current work</strong> <span>{timeline.goal?.goal?.objective ?? timeline.prompt?.text ?? 'Observed task'}</span>
      </button>
      <select ref={picker} aria-label="Select an agent" value={selected?.agent.agentId ?? ''} onChange={(event) => inspect(event.target.value || null)}>
        <option value="">Select an agent</option>
        {agentOptions}
      </select>
      <details className="flow2-legend-disclosure">
        <summary>Legend</summary>
        <div className="flow2-legend">{operations.map((operation) => {
          const Icon = operationIcons[operation]
          return <span key={operation} className={`flow2-legend-item flow2-operation-${operation.toLowerCase()}`}><Icon size={14} />{operation}</span>
        })}<div className="flow2-edge-key"><span>Project membership</span><span>Observed parent to subagent</span>
          {showWork && <span>Agent work or published output</span>}</div></div>
      </details>
    </div>
    {layoutError ? <p role="alert"><CircleAlert size={16} /> Flow layout unavailable: {layoutError}</p> : layout === null
      ? <p role="status">Arranging agent flow…</p>
      : <div className="flow2-workspace">
        <div className="flow2-canvas" aria-label={`${timeline.workspace.name} agent flow diagram`}
          onKeyDown={(event) => {
            const id = (event.target as HTMLElement).matches('.react-flow__node') ? (event.target as HTMLElement).dataset.id : undefined
            if (id && (event.key === 'Enter' || event.key === ' ')) {
              event.preventDefault()
              inspect(id)
            }
          }}>
          <ReactFlow nodes={nodes} edges={edges} nodeTypes={nodeTypes}
            nodesDraggable={false} nodesConnectable={false} edgesFocusable={false}
            fitView fitViewOptions={frame} minZoom={frame.minZoom} maxZoom={1.5}
            onNodeClick={(_event, node) => inspect(node.id)}
            onPaneClick={() => inspect(null)} proOptions={{ hideAttribution: false }}>
            <CanvasFraming bounds={bounds} arrangement={layout.arrangement} nodes={nodes} focus={layout.topologyKey === topologyKey ? focus : null} />
            <Panel position="top-left" className="flow2-map-tools">
              <div role="group" aria-label="Graph layout" className="flow2-layout-choices">
                {layoutChoices.map(({ id, label, icon: Icon }) => <button key={id} type="button"
                  aria-pressed={id === mode} onClick={() => chooseMode(id)}><Icon size={15} aria-hidden="true" />{label}</button>)}
              </div>
              <button type="button" className="flow2-work-toggle" aria-pressed={showWork}
                onClick={() => setWorkOverride(!showWork)}><ListTree size={16} aria-hidden="true" />Work bubbles</button>
              {showWork && roots.length > 1 && <select aria-label="Task for published outputs" value={taskId ?? ''} onChange={(event) => {
                setFollowingTask(event.target.value || null)
                inspect(null)
              }}>
                <option value="">Choose task for outputs</option>
                {roots.map(({ agent }) => <option key={agent.agentId} value={agent.publicId!}>
                  {agentDisplayName(agent)} · {agent.summary || new Date(agent.startedAtUtc ?? agent.updatedAtUtc).toLocaleTimeString()}
                </option>)}
              </select>}
            </Panel>
            <Background gap={20} size={1} /><Controls showInteractive={false} fitViewOptions={frame} />
          </ReactFlow>
        </div>
        {selectedId === 'project-hub' && <aside className="flow2-detail flow2-detail--project" aria-label="Project goal and prompt">
          <header><h3>Current work</h3><button type="button" onClick={() => setSelectedId(null)} aria-label="Close project details"><X size={16} /></button></header>
          <MainGoal goal={timeline.goal} sharingEnabled={timeline.sharingEnabled} />
          <h4>Project prompt</h4><p>{timeline.prompt?.text ?? 'Prompt text is not shared.'}</p>
        </aside>}
        {selected && <AgentInspector key={timeline.workspace.workspaceId} lane={selected} taskId={selectedTask} chat={chat} target={target}
          workspaceId={timeline.workspace.workspaceId} sharingEnabled={timeline.sharingEnabled}
          operation={<OperationLabel operation={flowOperation(selected)} />} agentOptions={agentOptions} onSelectAgent={inspect}
          width={inspectorWidth} onResize={setInspectorWidth} onClose={closeAgent} onFocusBranch={focusBranch} onOpenWall={onOpenWall} />}
      </div>}
  </div>
}
