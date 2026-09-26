import { describe, expect, it } from 'vitest'
import { buildNodeMemoPrompt } from './nodeConversation'
import type { ArchitectureSnapshot } from './types'

const snapshot: ArchitectureSnapshot = {
  metadata: {
    workspaceName: 'CAVE', providerId: 'test', sourceKind: 'CodeGraph', isLive: true,
    generatedAtUtc: '2026-08-19T00:00:00Z',
  },
  graph: {
    nodes: [
      {
        id: 'host', kind: 'Project', name: 'Cave.Host', parentId: null,
        qualifiedName: 'Cave.Host', description: 'HTTP presentation host.', categoryId: null,
        tags: ['host'], sourceLocations: [{ filePath: 'src/Cave.Host/Program.cs', startLine: 1, endLine: 20 }],
      },
      {
        id: 'app', kind: 'Project', name: 'Cave.Application', parentId: null,
        qualifiedName: 'Cave.Application', description: null, categoryId: null,
        tags: [], sourceLocations: [],
      },
    ],
    relations: [{
      id: 'depends', sourceId: 'host', targetId: 'app', kind: 'ProjectReference',
      weight: 1, evidenceCount: 1, confidence: 'Exact',
    }],
  },
  git: {
    status: 'Unavailable', baseline: null, worktree: null, files: [], nodes: [],
    unmappedFiles: [], error: null,
  },
  activity: {
    agents: [], nodes: [], recentEdits: [], latestInstruction: null, unmappedPaths: [], error: null,
  },
  conversation: {
    sharingEnabled: false,
    status: 'Disabled',
    messages: [],
    control: { sessionId: null, turnId: null, state: 'Unavailable', canSend: false, deliveries: [], error: null },
    error: null,
  },
}

describe('node memo context', () => {
  it('labels the selected node and its exact dependency evidence', () => {
    const prompt = buildNodeMemoPrompt(snapshot.graph.nodes[0], snapshot, 'Why does this matter?')

    expect(prompt).toContain('Question: Why does this matter?')
    expect(prompt).toContain('src/Cave.Host/Program.cs:1-20')
    expect(prompt).toContain('Cave.Host --ProjectReference--> Cave.Application')
    expect(prompt).toContain('uncertainties')
  })

})
