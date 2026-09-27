// MIT. Run: node poll.mjs http://127.0.0.1:5098 WORKSPACE_ID
import { setTimeout as delay } from 'node:timers/promises'

const [base = 'http://127.0.0.1:5098', workspace] = process.argv.slice(2)
if (!workspace) throw new Error('Supply the workspaceId from GET /api/workspaces.')
const url = new URL('/api/agent-flow', base)
url.searchParams.set('workspace', workspace)
const maximumBytes = 256 * 1024
let stopped = false
process.on('SIGINT', () => { stopped = true })

while (!stopped) {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(15000) })
    if (!response.ok) throw new Error(`HTTP ${response.status}`)
    const chunks = []
    let size = 0
    for await (const chunk of response.body) {
      size += chunk.byteLength
      if (size > maximumBytes) throw new Error('Response exceeds the v1 client buffer.')
      chunks.push(chunk)
    }
    const flow = JSON.parse(Buffer.concat(chunks).toString('utf8'))
    if (flow.schemaVersion !== 1 || !Array.isArray(flow.agents) || flow.agents.length > 32) {
      throw new Error('Unsupported or malformed Agent Flow response.')
    }
    console.log(`${flow.workspaceName} [${flow.sourceMode}] ${flow.sourceStatus}`)
    const main = flow.mainGoal
    console.log(main.status === 'Ready'
      ? main.goal ? `Goal: ${main.goal.objective}` : 'No main goal set'
      : `Goal: ${main.status}`)
    for (const agent of flow.agents) {
      const work = agent.currentFocus !== null
        ? `Focus: ${agent.currentFocus}`
        : agent.summary === null ? 'No work summary' : `${agent.summaryEvidence ?? 'Unknown'}: ${agent.summary}`
      console.log(`  ${agent.displayName}: ${agent.state} / ${agent.phase ?? '—'} · ${work}`)
    }
    if (flow.totalAgentCount > flow.agents.length) {
      console.log(`  +${flow.totalAgentCount - flow.agents.length} rows omitted`)
    }
  } catch (error) {
    // Do not preserve the previous shared objective as current after a failed read.
    console.error(`Agent Flow disconnected: ${error.message}`)
  }
  if (!stopped) await delay(2000)
}
