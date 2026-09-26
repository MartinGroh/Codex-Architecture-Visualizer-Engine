import {
  AppWindow,
  Boxes,
  Braces,
  CircleDot,
  Component,
  FlaskConical,
  Globe,
  Library,
  Package,
  PackageOpen,
  PanelsTopLeft,
  RadioTower,
  SquareTerminal,
  type LucideIcon,
} from 'lucide-react'
import { formatNodeKind } from './graphPresentation'
import { projectTypeOf, type ProjectType } from './projectTypes'
import type { ArchitectureNode } from './types'

export interface ArchitectureNodeVisual {
  Icon: LucideIcon
  label: string
  className: string
}

const projectVisuals: Record<ProjectType, ArchitectureNodeVisual> = {
  test: { Icon: FlaskConical, label: 'Test project', className: 'project-visual project-visual--test' },
  'class-library': { Icon: Library, label: 'Class library', className: 'project-visual project-visual--class-library' },
  'rest-api': { Icon: Globe, label: 'REST API', className: 'project-visual project-visual--rest-api' },
  'grpc-api': { Icon: RadioTower, label: 'gRPC API', className: 'project-visual project-visual--grpc-api' },
  executable: { Icon: SquareTerminal, label: 'CLI / executable', className: 'project-visual project-visual--executable' },
  winforms: { Icon: AppWindow, label: 'WinForms app', className: 'project-visual project-visual--winforms' },
  winui: { Icon: PanelsTopLeft, label: 'WinUI app', className: 'project-visual project-visual--winui' },
  typescript: { Icon: Braces, label: 'TypeScript package', className: 'project-visual project-visual--typescript' },
  'nuget-package': { Icon: PackageOpen, label: 'NuGet package', className: 'project-visual project-visual--nuget-package' },
  'unity-package': { Icon: PackageOpen, label: 'Unity package', className: 'project-visual project-visual--unity-package' },
  workspace: { Icon: Boxes, label: 'Workspace sources', className: 'project-visual project-visual--workspace' },
  unknown: { Icon: Package, label: 'Project', className: 'project-visual project-visual--unknown' },
}

const nodeIcons: Record<Exclude<ArchitectureNode['kind'], 'Project'>, LucideIcon> = {
  ArchitectureGroup: Boxes,
  Namespace: Braces,
  Class: Component,
  Interface: CircleDot,
  AbstractClass: Component,
}

/** Returns the shared icon, label, and accent class for cards and detail views. */
export function architectureNodeVisual(node: ArchitectureNode): ArchitectureNodeVisual {
  if (node.kind === 'Project') return projectVisuals[projectTypeOf(node)]

  return {
    Icon: nodeIcons[node.kind],
    label: formatNodeKind(node.kind),
    className: '',
  }
}
