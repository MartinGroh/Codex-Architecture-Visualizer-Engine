import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { createElement } from 'react'
import type { EdgeProps } from '@xyflow/react'
import { ArchitectureRouteEdge } from './ArchitectureRouteEdge'
import { architectureActivityEdgePaths, architectureEdgePath } from './edgeRouting'
import type { ArchitectureFlowEdge } from './graphPresentation'

describe('architecture route edge', () => {
  it('renders ELK sections as orthogonal line segments without cubic curves', () => {
    const path = architectureEdgePath({
      sections: [[
        { x: 10, y: 20 },
        { x: 30, y: 20 },
        { x: 30, y: 60 },
        { x: 80, y: 60 },
      ]],
    })

    expect(path).toBe('M 10 20 L 30 20 L 30 60 L 80 60')
    expect(path).not.toContain('C')
  })

  it('moves an input from its provider toward the active consumer', () => {
    const paths = architectureActivityEdgePaths({
      sections: [[
        { x: 10, y: 20 },
        { x: 30, y: 20 },
        { x: 30, y: 60 },
        { x: 80, y: 60 },
      ]],
    }, 'input')

    expect(paths).toEqual(['M 80 60 L 30 60 L 30 20 L 10 20'])
  })

  it('moves an output from the active provider toward its consumer', () => {
    const paths = architectureActivityEdgePaths({
      sections: [[{ x: 10, y: 20 }, { x: 80, y: 20 }]],
    }, 'output')

    expect(paths).toEqual(['M 80 20 L 10 20'])
  })

  it('renders no activity path for an inactive relation', () => {
    expect(architectureActivityEdgePaths({
      sections: [[{ x: 10, y: 20 }, { x: 80, y: 20 }]],
    }, undefined)).toEqual([])
  })

  it('renders two visible traveling signals on each active route', () => {
    const route = {
      sections: [[{ x: 10, y: 20 }, { x: 80, y: 20 }]],
    }
    const { container } = render(createElement(ArchitectureRouteEdge, {
      id: 'active-edge',
      data: { route, activityDirection: 'input' },
      interactionWidth: 24,
    } as EdgeProps<ArchitectureFlowEdge>))

    const signals = container.querySelectorAll('.edge-activity-signal')
    expect(signals).toHaveLength(2)
    expect(signals[0].querySelector('animateMotion')?.getAttribute('path'))
      .toBe('M 80 20 L 10 20')
    expect(signals[0].querySelector('animateMotion')?.getAttribute('begin')).toBe('0s')
    expect(signals[1].querySelector('animateMotion')?.getAttribute('begin')).toBe('-0.9s')
  })
})
