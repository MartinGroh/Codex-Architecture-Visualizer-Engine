import type {
  ArchitectureNode,
  ArchitectureSnapshot,
} from './types'

/** Builds bounded, evidence-labelled context for an isolated node memo request. */
export function buildNodeMemoPrompt(
  node: ArchitectureNode,
  snapshot: ArchitectureSnapshot,
  question: string,
): string {
  const nodesById = new Map(snapshot.graph.nodes.map((candidate) => [candidate.id, candidate]))
  const incoming = snapshot.graph.relations
    .filter((relation) => relation.targetId === node.id)
    .slice(0, 20)
    .map((relation) => `${nodesById.get(relation.sourceId)?.name ?? relation.sourceId} --${relation.kind}--> ${node.name}`)
  const outgoing = snapshot.graph.relations
    .filter((relation) => relation.sourceId === node.id)
    .slice(0, 20)
    .map((relation) => `${node.name} --${relation.kind}--> ${nodesById.get(relation.targetId)?.name ?? relation.targetId}`)
  const sources = node.sourceLocations.slice(0, 12)
    .map((source) => `${source.filePath}:${source.startLine}-${source.endLine}`)

  return [
    `Question: ${question.trim()}`,
    '',
    'CAVE node evidence:',
    `- Name: ${node.name}`,
    `- Kind: ${node.kind}`,
    `- Qualified name: ${node.qualifiedName ?? 'not supplied'}`,
    `- Description: ${node.description?.trim() || 'not supplied'}`,
    `- Tags: ${node.tags.join(', ') || 'none'}`,
    `- Sources: ${sources.join(', ') || 'none supplied'}`,
    `- Incoming relations: ${incoming.join('; ') || 'none in this projection'}`,
    `- Outgoing relations: ${outgoing.join('; ') || 'none in this projection'}`,
    '',
    'Write a compact memo with: purpose, key dependencies, implications, and uncertainties.',
  ].join('\n')
}
