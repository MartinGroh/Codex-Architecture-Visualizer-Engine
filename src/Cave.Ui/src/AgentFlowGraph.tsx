import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { Background, Controls, getViewportForBounds, Handle, Position, ReactFlow, useReactFlow, useStore, type NodeProps } from '@xyflow/react'
import { BookOpen, Brain, Check, CircleAlert, Clock3, Cog, Network, Pause, Pencil, ShieldCheck, X } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { AgentInspector } from './AgentInspector'
import { MainGoal } from './AgentWorkContext'
import { AgentModelLabel } from './AgentModel'
import { agentDisplayName, agentRoleLabel } from './agentIdentity'
import { agentFlowTopology, flowOperation, layoutAgentFlow, type AgentFlowLayout, type AgentFlowNode, type FlowOperation } from './agentFlowGraphModel'
import { observedTaskRoot, type MachineActivityLane, type MachineActivityProjectTimeline } from './machineActivityTimeline'
import './AgentFlowGraph.css'

const operationIcons = { Thinking: Brain, Reading: BookOpen, Editing: Pencil, Validating: ShieldCheck, Working: Cog, Completed: Check, Idle: Pause, Planned: Clock3 }
const nodeTypes = { hub: ProjectHub, agent: AgentCard }
const frame = { minZoom: 0.2, maxZoom: 1.25, padding: .08 }

function ProjectHub({ data }: NodeProps<AgentFlowNode>) {
  return <div className="flow2-hub">
    <Handle type="source" position={Position.Right} />
    <span className="flow2-hub__mark"><Network size={26} strokeWidth={1.75} aria-hidden="true" /></span>
    <div><strong title={data.name}>{data.name}</strong><small>Project hub</small></div>
    <Handle type="target" position={Position.Left} />
  </div>
}
function OperationLabel({ operation }: { operation: FlowOperation }) {
  const Icon = operationIcons[operation]
  return <span className={`flow2-operation flow2-operation-${operation.toLowerCase()}`}><Icon size={14} aria-hidden="true" />{operation}</span>
}
function AgentCard({ data }: NodeProps<AgentFlowNode>) {
  const lane = data.lane
  if (!lane) return null
  const operation = flowOperation(lane)
  const name = agentDisplayName(lane.agent)
  const role = agentRoleLabel(lane.agent)
  const summary = lane.agent.summary ?? lane.agent.lastObservedActivity ?? 'No work detail observed'
  return <div className={`flow2-node flow2-operation-${operation.toLowerCase()}`}>
    <Handle type="target" position={Position.Left} /><Handle type="source" position={Position.Right} />
    <div className="flow2-node__identity">
      <div className="flow2-node__portrait"><AgentAvatar phase={lane.agent.phase} active={lane.active} isSubagent={lane.agent.isSubagent} size={32} /></div>
      <div className="flow2-node__name"><strong title={name}>{name}</strong>{role !== name && <small title={role}>{role}</small>}</div>
    </div>
    <p title={summary}>{summary}</p>
    <footer><OperationLabel operation={operation} /><AgentModelLabel configuration={lane.agent.configuration} /></footer>
  </div>
}

function CanvasFraming() {
  const width = useStore((state) => state.width)
  const height = useStore((state) => state.height)
  const flow = useReactFlow<AgentFlowNode>()
  const lastSize = useRef<{ width: number; height: number } | null>(null)
  useLayoutEffect(() => {
    if (!flow.viewportInitialized || !width || !height) return
    const previous = lastSize.current
    lastSize.current = { width, height }
    if (!previous || previous.width === width && previous.height === height) return
    const camera = flow.getViewport()
    const bounds = flow.getNodesBounds(flow.getNodes())
    const fit = (w: number, h: number) => getViewportForBounds(bounds, w, h, frame.minZoom, frame.maxZoom, frame.padding)
    const before = fit(previous.width, previous.height)
    // Keep a fitted overview fitted. A hand-positioned view keeps the same world point under its center.
    const wasFitted = Math.abs(before.x - camera.x) < 1 && Math.abs(before.y - camera.y) < 1 && Math.abs(before.zoom - camera.zoom) < .001
    void flow.setViewport(wasFitted ? fit(width, height) : {
      x: camera.x + (width - previous.width) / 2,
      y: camera.y + (height - previous.height) / 2,
      zoom: camera.zoom,
    })
  }, [flow, width, height])
  return null
}

/** Second, spatial flow presentation over the existing evidence-only timeline. */
export function AgentFlowGraph({ timeline, lanes, onOpenWall }: {
  timeline: MachineActivityProjectTimeline
  lanes: MachineActivityLane[]
  onOpenWall: (taskId: string) => void
}) {
  const [layout, setLayout] = useState<AgentFlowLayout | null>(null)
  const [layoutError, setLayoutError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const picker = useRef<HTMLSelectElement>(null)
  const [inspectorWidth, setInspectorWidth] = useState<number | null>(null)
  const topologyKey = JSON.stringify(agentFlowTopology(lanes))
  useEffect(() => {
    let disposed = false
    setLayoutError(null)
    void layoutAgentFlow(JSON.parse(topologyKey)).then((next) => { if (!disposed) setLayout(next) })
      .catch((error: unknown) => { if (!disposed) { setLayout(null); setLayoutError(error instanceof Error ? error.message : 'Layout unavailable') } })
    return () => { disposed = true }
  }, [topologyKey])
  const nodes = useMemo(() => (layout?.nodes ?? []).map((node): AgentFlowNode => {
    const lane = lanes.find((item) => item.agent.agentId === node.id)
    return { ...node, selected: node.id === selectedId, ariaRole: 'button',
      ariaLabel: lane ? `Inspect ${agentDisplayName(lane.agent)}, ${agentRoleLabel(lane.agent)}` : `Inspect ${timeline.workspace.name} project`,
      domAttributes: { 'aria-expanded': node.id === selectedId },
      data: { name: timeline.workspace.name, lane },
    }
  }), [layout, lanes, timeline.workspace.name, selectedId])
  const edges = useMemo(() => (layout?.edges ?? []).map((edge) => {
    if (selectedId !== edge.target && selectedId !== edge.source) return edge
    return { ...edge, className: edge.className + ' flow2-route--selected', zIndex: 1,
      markerEnd: typeof edge.markerEnd === 'object' ? { ...edge.markerEnd, color: 'var(--violet)' } : edge.markerEnd,
    }
  }), [layout, selectedId])
  const selected = lanes.find((lane) => lane.agent.agentId === selectedId)
  const taskId = selected ? observedTaskRoot(lanes, selected.agent.agentId)?.agent.publicId : null
  const closeAgent = () => {
    setSelectedId(null)
    picker.current?.focus()
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
  return <div className="flow2">
    <div className="flow2-toolbar">
      <button className="flow2-goal" type="button" aria-pressed={selectedId === 'project-hub'} onClick={() => setSelectedId(selectedId === 'project-hub' ? null : 'project-hub')}>
        <strong>Current work</strong> <span>{timeline.goal?.goal?.objective ?? timeline.prompt?.text ?? 'Observed task'}</span>
      </button>
      <select ref={picker} aria-label="Select an agent" value={selected?.agent.agentId ?? ''} onChange={(event) => setSelectedId(event.target.value || null)}>
        <option value="">Select an agent</option>
        {agentOptions}
      </select>
      <details className="flow2-legend-disclosure">
        <summary>Legend</summary>
        <div className="flow2-legend">{operations.map((operation) => {
          const Icon = operationIcons[operation]
          return <span key={operation} className={`flow2-legend-item flow2-operation-${operation.toLowerCase()}`}><Icon size={14} />{operation}</span>
        })}<div className="flow2-edge-key"><span>Project membership</span><span>Observed parent to subagent</span></div></div>
      </details>
    </div>
    {layoutError ? <p role="alert"><CircleAlert size={16} /> Flow layout unavailable: {layoutError}</p> : layout === null
      ? <p role="status">Arranging agent flow…</p>
      : <div className="flow2-workspace">
        <div className="flow2-canvas" aria-label={`${timeline.workspace.name} agent flow diagram`}
          onKeyDown={(event) => {
            const id = (event.target as HTMLElement).closest<HTMLElement>('.react-flow__node')?.dataset.id
            if (id && (event.key === 'Enter' || event.key === ' ')) {
              event.preventDefault()
              setSelectedId(id)
            }
          }}>
          <ReactFlow nodes={nodes} edges={edges} nodeTypes={nodeTypes}
            nodesDraggable={false} nodesConnectable={false} edgesFocusable={false}
            fitView fitViewOptions={frame} minZoom={frame.minZoom} maxZoom={1.5}
            onNodeClick={(_event, node) => setSelectedId(node.id)}
            onPaneClick={() => setSelectedId(null)} proOptions={{ hideAttribution: false }}>
            <CanvasFraming />
            <Background gap={20} size={1} /><Controls showInteractive={false} fitViewOptions={frame} />
          </ReactFlow>
        </div>
        {selectedId === 'project-hub' && <aside className="flow2-detail flow2-detail--project" aria-label="Project goal and prompt">
          <header><h3>Current work</h3><button type="button" onClick={() => setSelectedId(null)} aria-label="Close project details"><X size={16} /></button></header>
          <MainGoal goal={timeline.goal} sharingEnabled={timeline.sharingEnabled} />
          <h4>Project prompt</h4><p>{timeline.prompt?.text ?? 'Prompt text is not shared.'}</p>
        </aside>}
        {selected && <AgentInspector key={timeline.workspace.workspaceId} lane={selected} taskId={taskId}
          workspaceId={timeline.workspace.workspaceId} sharingEnabled={timeline.sharingEnabled}
          operation={<OperationLabel operation={flowOperation(selected)} />} agentOptions={agentOptions} onSelectAgent={setSelectedId}
          width={inspectorWidth} onResize={setInspectorWidth} onClose={closeAgent} onOpenWall={onOpenWall} />}
      </div>}
  </div>
}
