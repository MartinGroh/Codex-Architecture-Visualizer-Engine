import { useId, useLayoutEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { Activity, ArrowUpRight, ChevronDown, Images, LayoutGrid, MessagesSquare, X } from 'lucide-react'
import { AgentAvatar } from './AgentAvatar'
import { AgentActivityEntry, AgentChatFeed } from './AgentChatFeed'
import { AgentChatOutputs } from './AgentChatOutputs'
import { useAgentChatOutputs } from './agentChatOutputIndex'
import { AgentModelLabel } from './AgentModel'
import { agentChatNotice, sharedAgentMessages, useAgentChat, type AgentChatSession } from './agentChat'
import { agentDisplayName, agentFocusLabel, agentRoleLabel } from './agentIdentity'
import { agentHistoryMilestones, type MachineActivityLane } from './machineActivityTimeline'
import './AgentInspector.css'

const sections = [
  { name: 'Conversation', icon: MessagesSquare },
  { name: 'Outputs', icon: Images },
  { name: 'Activity', icon: Activity },
] as const
type InspectorSection = typeof sections[number]['name']

interface InspectorProps {
  lane: MachineActivityLane
  workspaceId: string
  taskId?: string | null
  sharingEnabled: boolean
  operation: ReactNode
  agentOptions: ReactNode[]
  onSelectAgent: (id: string) => void
  width: number | null
  onResize: (width: number) => void
  onClose: () => void
  onOpenWall: (taskId: string) => void
}

export function AgentInspector({ lane, workspaceId, taskId, sharingEnabled, operation, agentOptions, onSelectAgent, width, onResize, onClose, onOpenWall }: InspectorProps) {
  const chat = useAgentChat(workspaceId, taskId, sharingEnabled)
  const [section, setSection] = useState<InspectorSection>(sharingEnabled && taskId ? 'Conversation' : 'Activity')
  const id = useId()
  const dialog = useRef<HTMLDialogElement>(null)
  const drag = useRef<{ x: number; width: number } | null>(null)
  const [available, setAvailable] = useState(1200)
  const maxWidth = Math.max(310, Math.min(880, Math.floor(available * .56)))
  const preferred = width ?? Math.min(680, Math.max(440, Math.round(available * .3)))
  const panelWidth = Math.min(maxWidth, Math.max(310, preferred))
  const resize = (next: number) => onResize(Math.min(maxWidth, Math.max(310, next)))
  const name = agentDisplayName(lane.agent)
  const role = agentRoleLabel(lane.agent)

  useLayoutEffect(() => {
    const panel = dialog.current!
    const container = panel.parentElement!
    const measure = () => setAvailable(container.clientWidth)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(container)
    const narrow = window.matchMedia('(max-width: 900px)')
    const present = () => {
      // Reopen the preview last so it keeps its place above the inspector after device rotation.
      const preview = panel.querySelector<HTMLDialogElement>('dialog[open]')
      preview?.close()
      if (panel.open) panel.close()
      if (narrow.matches) panel.showModal()
      else panel.show()
      if (preview) preview.showModal()
      else panel.querySelector<HTMLElement>('[role="tab"][aria-selected="true"]')?.focus({ preventScroll: true })
    }
    present()
    narrow.addEventListener('change', present)
    return () => {
      narrow.removeEventListener('change', present)
      observer.disconnect()
      if (panel.open) panel.close()
    }
  }, [])

  const close = () => {
    dialog.current?.close()
    onClose()
  }

  return <>
    <div className="flow2-resize" role="separator" tabIndex={0} aria-label="Resize agent inspector"
      aria-controls={id} aria-orientation="vertical" aria-valuemin={310} aria-valuemax={maxWidth} aria-valuenow={panelWidth}
      aria-valuetext={panelWidth + ' pixels'}
      onPointerDown={(event) => {
        if (event.button !== 0) return
        drag.current = { x: event.clientX, width: panelWidth }
        event.currentTarget.setPointerCapture(event.pointerId)
      }}
      onPointerMove={(event) => {
        if (drag.current) resize(drag.current.width + drag.current.x - event.clientX)
      }}
      onLostPointerCapture={() => { drag.current = null }}
      onKeyDown={(event) => {
        switch (event.key) {
          case 'ArrowLeft': resize(panelWidth + 32); break
          case 'ArrowRight': resize(panelWidth - 32); break
          case 'Home': resize(310); break
          case 'End': resize(maxWidth); break
          default: return
        }
        event.preventDefault()
      }} />
    <dialog ref={dialog} id={id} className="agent-inspector" aria-label={name + ' agent inspector'}
      style={{ '--agent-inspector-width': panelWidth + 'px' } as CSSProperties}
      onCancel={(event) => {
        if (event.target !== dialog.current) return
        event.preventDefault()
        close()
      }}
      onKeyDown={(event) => {
        if (event.key === 'Escape' && (event.target as Element).closest('dialog') === dialog.current) {
          event.preventDefault()
          close()
        }
      }}>
      <header className="agent-inspector__heading">
        <AgentAvatar phase={lane.agent.phase} active={lane.active} isSubagent={lane.agent.isSubagent} size={36} />
        <div className="agent-inspector__identity">
          <div><h3 className="agent-inspector__name">{name}{agentOptions.length > 1 && <>
            <ChevronDown size={15} aria-hidden="true" />
            <select aria-label="Switch inspected agent" value={lane.agent.agentId} onChange={(event) => onSelectAgent(event.currentTarget.value)}>
              {agentOptions}
            </select>
          </>}</h3>{operation}</div>
          <div>{role !== name && <span>{role}</span>}<AgentModelLabel configuration={lane.agent.configuration} /></div>
        </div>
        <div className="agent-inspector__actions">
          {taskId && <button type="button" onClick={() => onOpenWall(taskId)} aria-label="Open agent wall" title="Open agent wall">
            <LayoutGrid size={16} aria-hidden="true" /><span>Wall</span>
          </button>}
          <button type="button" onClick={close} aria-label="Close agent details" title="Close agent details"><X size={18} aria-hidden="true" /></button>
        </div>
      </header>
      <InspectorViews key={JSON.stringify([workspaceId, taskId, lane.agent.agentId])}
        lane={lane} workspaceId={workspaceId} chat={chat} taskId={taskId} section={section} setSection={setSection} />
    </dialog>
  </>
}

function InspectorViews({ lane, workspaceId, chat, taskId, section, setSection }: {
  lane: MachineActivityLane
  workspaceId: string
  chat: AgentChatSession
  taskId?: string | null
  section: InspectorSection
  setSection: (section: InspectorSection) => void
}) {
  const tabId = useId()
  const agent = chat.agents.find((row) => row.id === lane.agent.publicId)
  const messages = sharedAgentMessages(chat, agent)
  const outputs = useAgentChatOutputs(messages)
  const [reveal, setReveal] = useState<{ messageId: string; periodId: string | null; sharing: AgentChatSession['sharing'] } | null>(null)
  const [sharingRequest, setSharingRequest] = useState<'idle' | 'sending' | 'failed'>('idle')
  const notice = agentChatNotice(chat, agent)
  const scopeKey = JSON.stringify([chat.sharing, chat.periodId])
  const openMessage = (messageId: string) => {
    setReveal({ messageId, periodId: chat.periodId, sharing: chat.sharing })
    setSection('Conversation')
  }
  return <>
    <div className="agent-inspector__tabs" role="tablist" aria-label="Agent details">
      {sections.map(({ name, icon: Icon }, index) => <button key={name} type="button" role="tab"
        id={tabId + name} aria-selected={section === name} aria-controls={tabId + name + '-panel'} tabIndex={section === name ? 0 : -1}
        onClick={() => setSection(name)} onKeyDown={(event) => {
          let next: number
          switch (event.key) {
            case 'ArrowRight': next = (index + 1) % sections.length; break
            case 'ArrowLeft': next = (index + sections.length - 1) % sections.length; break
            case 'Home': next = 0; break
            case 'End': next = sections.length - 1; break
            default: return
          }
          event.preventDefault()
          setSection(sections[next].name)
          document.getElementById(tabId + sections[next].name)?.focus()
        }}>
        <Icon size={16} aria-hidden="true" />{name}
        {name === 'Outputs' && outputs.length > 0 && <span className="agent-inspector__count">{outputs.length}</span>}
      </button>)}
    </div>
    {section !== 'Activity' && taskId && chat.sourceMode !== 'Demo' && (chat.sharing === 'Legacy' || chat.sharing === 'Private') &&
      <div className="agent-inspector__sharing">
        <button type="button" disabled={sharingRequest === 'sending'} onClick={() => {
          setSharingRequest('sending')
          void chat.enableSharing().then(() => setSharingRequest('idle'), () => setSharingRequest('failed'))
        }}>Enable live agent chats</button>
        {sharingRequest === 'failed' && <span role="alert">Could not enable live agent chats.</span>}
      </div>}
    <div id={tabId + 'Conversation-panel'} aria-labelledby={tabId + 'Conversation'} role="tabpanel"
      className="agent-inspector__conversation" hidden={section !== 'Conversation'}>
      <AgentChatFeed view="conversation" lane={lane} agent={agent} chat={chat}
        reveal={reveal?.periodId === chat.periodId && reveal.sharing === chat.sharing ? reveal : null} />
    </div>
    <div id={tabId + 'Outputs-panel'} aria-labelledby={tabId + 'Outputs'} role="tabpanel" tabIndex={0}
      className="agent-inspector__reader" hidden={section !== 'Outputs'} key={'outputs' + scopeKey}>
      {section === 'Outputs' && <>
        {notice && <p className="agent-inspector__notice" role="status">{notice}</p>}
        {outputs.length > 0
          ? <AgentChatOutputs outputs={outputs} artifactUrl={chat.artifactUrl} onOpenMessage={openMessage} />
          : !notice && <p className="agent-inspector__notice"><Images size={18} aria-hidden="true" />No diagrams or images in this agent’s shared messages.</p>}
      </>}
    </div>
    <div id={tabId + 'Activity-panel'} aria-labelledby={tabId + 'Activity'} role="tabpanel" tabIndex={0}
      className="agent-inspector__reader agent-inspector__activity" hidden={section !== 'Activity'}>
      {section === 'Activity' && <>
        <dl>{lane.agent.summary && <><dt>{agentFocusLabel(lane.agent)}</dt><dd>{lane.agent.summary}</dd></>}
          {lane.agent.lastObservedActivity && lane.agent.lastObservedActivity !== lane.agent.summary &&
            <><dt>Last observed action</dt><dd>{lane.agent.lastObservedActivity}</dd></>}
        </dl>
        <h4>Recent activity</h4>
        <ol>{agentHistoryMilestones(lane).map((event) => <AgentActivityEntry key={event.id} event={event} />)}</ol>
        {lane.agent.publicId && <a href={`/activity?workspace=${encodeURIComponent(workspaceId)}&agent=${encodeURIComponent(lane.agent.publicId)}`}>
          Open full timeline <ArrowUpRight size={15} aria-hidden="true" /></a>}
      </>}
    </div>
  </>
}
