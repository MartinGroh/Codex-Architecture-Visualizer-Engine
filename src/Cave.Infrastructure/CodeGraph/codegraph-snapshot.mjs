import { pathToFileURL } from 'node:url'
import path from 'node:path'

const workspaceRoot = process.argv[2]
if (!workspaceRoot || !path.isAbsolute(workspaceRoot)) {
  throw new Error('CAVE requires an absolute workspace root.')
}

const localAppData = process.env.LOCALAPPDATA
if (!localAppData) {
  throw new Error('LOCALAPPDATA is unavailable; the CodeGraph installation cannot be located.')
}

const libraryPath = path.join(localAppData, 'codegraph', 'current', 'lib', 'dist', 'index.js')
const { CodeGraph, setLogger, silentLogger } = await import(pathToFileURL(libraryPath).href)
setLogger(silentLogger)

const graph = await CodeGraph.open(workspaceRoot, { sync: true })

try {
  const visibleKinds = ['namespace', 'class', 'struct', 'interface', 'enum', 'component']
  const nodes = visibleKinds.flatMap((kind) => graph.getNodesByKind(kind))
  const visibleIds = new Set(nodes.map((node) => node.id))
  const edges = nodes
    .flatMap((node) => graph.getOutgoingEdges(node.id))
    .filter((edge) => visibleIds.has(edge.source) && visibleIds.has(edge.target))

  process.stdout.write(JSON.stringify({ nodes, edges }))
} finally {
  graph.close()
}
