import type { ArchitectureNode } from './types'

export const projectTypes = [
  'test',
  'class-library',
  'rest-api',
  'grpc-api',
  'executable',
  'winforms',
  'winui',
  'typescript',
  'nuget-package',
  'unity-package',
  'workspace',
] as const

export type ProjectType = (typeof projectTypes)[number] | 'unknown'

/** Resolves the normalized project type emitted by the semantic adapter. */
export function projectTypeOf(node: ArchitectureNode): ProjectType {
  if (node.kind !== 'Project') return 'unknown'

  for (const projectType of projectTypes) {
    if (node.tags.includes(`project-type:${projectType}`)) return projectType
  }

  return 'unknown'
}

/** Identifies a project whose manifest evidence classifies it as test infrastructure. */
export function isTestProject(node: ArchitectureNode): boolean {
  return node.kind === 'Project' && projectTypeOf(node) === 'test'
}
