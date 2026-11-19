import { useEffect, useMemo, useState } from 'react'
import { Background, Controls, Handle, Position, ReactFlow, type NodeProps } from '@xyflow/react'
import { BookOpen, Brain, Check, CircleAlert, Clock3, Cog, Network, Pause, Pencil, ShieldCheck, X } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { MainGoal } from './AgentWorkContext'
import { AgentModelLabel } from './AgentModel'
import { agentDisplayName, agentFocusLabel } from './agentIdentity'
import { agentFlowTopology, flowOperation, layoutAgentFlow, type AgentFlowLayout, type AgentFlowNode, type FlowOperation } from './agentFlowGraphModel'
import { visibleLaneMilestones, type MachineActivityLane, type MachineActivityProjectTimeline } from './machineActivityTimeline'
import './AgentFlowGraph.css'

const operationIcons = { Thinking: Brain, Reading: BookOpen, Editing: Pencil, Validating: ShieldCheck, Working: Cog, Completed: Check, Idle: Pause, Planned: Clock3 }
const nodeTypes = { hub: ProjectHub, agent: AgentCard }

function ProjectHub({ data }: NodeProps<AgentFlowNode>) {
  return <div className="flow2-hub"><Handle type="source" position={Position.Right} /><Network size={25} />
    <strong>{data.name}</strong><span>Project hub</span><Handle type="target" position={Position.Left} /></div>
}
function AgentCard({ data }: NodeProps<AgentFlowNode>) {
  const lane = data.lane
  if (!lane) return null
  const operation = flowOperation(lane)
  const Icon = operationIcons[operation]
  return <div className={`flow2-node flow2-operation-${operation.toLowerCase()}`}>
    <Handle type="target" position={Position.Left} /><Handle type="source" position={Position.Right} />
    <div className="flow2-node__identity"><AgentAvatar phase={lane.agent.phase} active={lane.active} isSubagent={lane.agent.isSubagent} configuration={lane.agent.configuration ?? null} size={32} />
      <div><strong>{agentDisplayName(lane.agent)}</strong><span>{lane.agent.isSubagent ? 'Subagent' : 'Main agent'}</span></div></div>
    <span className="flow2-operation"><Icon size={14} />{operation}</span>
    <p>{lane.agent.summary ?? lane.agent.lastObservedActivity ?? 'No work detail observed'}</p>
    <AgentModelLabel configuration={lane.agent.configuration} />
  </div>
}

/** Second, spatial flow presentation over the existing evidence-only timeline. */
export function AgentFlowGraph({ timeline, lanes }: { timeline: MachineActivityProjectTimeline; lanes: MachineActivityLane[] }) {
  const [layout, setLayout] = useState<AgentFlowLayout | null>(null)
  const [layoutError, setLayoutError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const topologyKey = JSON.stringify(agentFlowTopology(lanes))
  useEffect(() => {
    let disposed = false
    setLayoutError(null)
    void layoutAgentFlow(JSON.parse(topologyKey)).then((next) => { if (!disposed) setLayout(next) })
      .catch((error: unknown) => { if (!disposed) { setLayout(null); setLayoutError(error instanceof Error ? error.message : 'Layout unavailable') } })
    return () => { disposed = true }
  }, [topologyKey])
  const nodes = useMemo(() => (layout?.nodes ?? []).map((node) => ({ ...node,
    selected: node.id === selectedId,
    data: { name: timeline.workspace.name, lane: lanes.find((lane) => lane.agent.agentId === node.id) },
  })), [layout, lanes, timeline.workspace.name, selectedId])
  const selected = lanes.find((lane) => lane.agent.agentId === selectedId)
  const operations: FlowOperation[] = ['Thinking', 'Reading', 'Editing', 'Validating', 'Working', 'Idle', 'Completed']
  return <div className="flow2">
    <div className="flow2-toolbar">
      <button className="flow2-goal" type="button" aria-pressed={selectedId === 'project-hub'} onClick={() => setSelectedId(selectedId === 'project-hub' ? null : 'project-hub')}>
        <strong>Current work</strong> <span>{timeline.goal?.goal?.objective ?? timeline.prompt?.text ?? 'Observed task'}</span>
      </button>
      <select aria-label="Select an agent" value={selected?.agent.agentId ?? ''} onChange={(event) => setSelectedId(event.target.value || null)}>
        <option value="">Select an agent</option>
        {lanes.map(({ agent }) => <option key={agent.agentId} value={agent.agentId}>{agentDisplayName(agent)} · {agent.isSubagent ? 'Subagent' : 'Main agent'}</option>)}
      </select>
      <details className="flow2-legend-disclosure">
        <summary>Legend</summary>
        <div className="flow2-legend">{operations.map((operation) => {
          const Icon = operationIcons[operation]
          return <span key={operation} className={`flow2-legend-item flow2-operation-${operation.toLowerCase()}`}><i /><Icon size={13} />{operation}</span>
        })}<span className="flow2-edge-key">Dashed links show project membership. Solid links show observed parents.</span></div>
      </details>
    </div>
    {layoutError ? <p role="alert"><CircleAlert size={16} /> Flow layout unavailable: {layoutError}</p> : layout === null
      ? <p role="status">Arranging agent flow…</p>
      : <div className="flow2-workspace">
        <div className="flow2-canvas" aria-label={`${timeline.workspace.name} agent flow diagram`}>
          <ReactFlow key={topologyKey} nodes={nodes} edges={layout.edges} nodeTypes={nodeTypes}
            nodesDraggable={false} nodesConnectable={false} edgesFocusable={false}
            fitView fitViewOptions={{ padding: 0.07, maxZoom: 1 }} minZoom={0.2} maxZoom={1.5}
            onNodeClick={(_event, node) => setSelectedId(node.id)}
            onPaneClick={() => setSelectedId(null)} proOptions={{ hideAttribution: false }}>
            <Background gap={24} size={1} /><Controls showInteractive={false} />
          </ReactFlow>
        </div>
        {selectedId === 'project-hub' && <aside className="flow2-detail" aria-label="Project goal and prompt">
          <header><h3>Current work</h3><button type="button" onClick={() => setSelectedId(null)} aria-label="Close project details"><X size={16} /></button></header>
          <MainGoal goal={timeline.goal} sharingEnabled={timeline.sharingEnabled} />
          <h4>Project prompt</h4><p>{timeline.prompt?.text ?? 'Prompt text is not shared.'}</p>
        </aside>}
        {selected && <aside className="flow2-detail" aria-label="Selected agent details">
          <header><h3>{agentDisplayName(selected.agent)}</h3><button type="button" onClick={() => setSelectedId(null)} aria-label="Close agent details"><X size={16} /></button></header>
          <AgentModelLabel configuration={selected.agent.configuration} />
          <h4>{agentFocusLabel(selected.agent)}</h4><p>{selected.agent.summary ?? 'No declared focus.'}</p>
          <h4>Observed work</h4><ol>{visibleLaneMilestones(selected, selected.active).map((item) => <li key={item.id}>
            <strong>{item.label}</strong><p>{item.detail}</p><time>{new Date(item.observedAtUtc).toLocaleTimeString()}</time>
          </li>)}</ol>
          {selected.agent.publicId && <a href={`/activity?workspace=${encodeURIComponent(timeline.workspace.workspaceId)}&agent=${encodeURIComponent(selected.agent.publicId)}`}>Open full timeline</a>}
        </aside>}
      </div>}
  </div>
}
