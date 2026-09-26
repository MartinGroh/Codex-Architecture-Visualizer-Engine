import { describe, expect, it } from 'vitest'
import {
  nodeReadabilityCssVariables,
  nodeReadabilityForViewport,
} from './nodeReadability'

describe('nodeReadabilityForViewport', () => {
  it('preserves the normal card presentation when content has enough device pixels', () => {
    expect(nodeReadabilityForViewport(1)).toEqual({
      effectiveZoom: 1,
      navigationScale: 1,
      indicatorScale: 1,
      secondaryOpacity: 1,
      metadataOpacity: 1,
      titleExpansion: 0,
      detailCapacity: 0,
      secondaryScale: 1,
    })
  })

  it('enlarges navigation content and removes secondary copy at a distant viewport', () => {
    expect(nodeReadabilityForViewport(0.32)).toEqual({
      effectiveZoom: 0.32,
      navigationScale: 2.5,
      indicatorScale: 2.3,
      secondaryOpacity: 0,
      metadataOpacity: 0,
      titleExpansion: 1,
      detailCapacity: 0,
      secondaryScale: 1,
    })
  })

  it('retains larger secondary copy longer when a large canvas has room for it', () => {
    const ordinary = nodeReadabilityForViewport(0.74, { width: 1_180, height: 720 })
    const large = nodeReadabilityForViewport(0.74, { width: 2_500, height: 1_300 })
    const distant = nodeReadabilityForViewport(0.32, { width: 2_500, height: 1_300 })
    const variables = nodeReadabilityCssVariables(large)

    expect(ordinary.secondaryOpacity).toBe(0)
    expect(large.secondaryOpacity).toBe(1)
    expect(large.metadataOpacity).toBeGreaterThanOrEqual(0.97)
    expect(large.secondaryScale).toBeGreaterThan(1.16)
    expect(Number.parseFloat(variables['--cave-node-description-size'])).toBeGreaterThan(11)
    expect(Number.parseFloat(variables['--cave-node-description-height'])).toBeGreaterThan(29)
    expect(variables['--cave-node-footer-height']).toBe('10px')
    expect(distant.secondaryOpacity).toBe(0)
    expect(distant.metadataOpacity).toBe(0)
  })

  it('keeps the first large-canvas detail row legible at an intermediate zoom', () => {
    const readability = nodeReadabilityForViewport(0.61, { width: 2_068, height: 1_137 })
    const variables = nodeReadabilityCssVariables(readability)

    expect(readability.secondaryOpacity).toBeGreaterThanOrEqual(0.55)
    expect(readability.secondaryOpacity).toBeLessThan(0.74)
    expect(variables['--cave-node-secondary-opacity']).toBe('0.74')
    expect(Number.parseFloat(variables['--cave-node-description-size'])).toBeGreaterThan(12)
    expect(Number.parseFloat(variables['--cave-node-description-height'])).toBeGreaterThan(16)
  })

  it('keeps the primary title near a readable CSS-pixel size through overview zooms', () => {
    for (const zoom of [0.32, 0.4, 0.5, 0.65, 0.75]) {
      const readability = nodeReadabilityForViewport(zoom)
      const titlePixelsOnScreen = 14.5 * readability.navigationScale * zoom

      expect(titlePixelsOnScreen).toBeGreaterThanOrEqual(11.5)
    }
  })

  it('changes continuously between the distant and normal presentations', () => {
    const distant = nodeReadabilityForViewport(0.6)
    const middle = nodeReadabilityForViewport(0.8)
    const near = nodeReadabilityForViewport(0.92)

    expect(distant.navigationScale).toBeGreaterThan(middle.navigationScale)
    expect(middle.navigationScale).toBeGreaterThan(near.navigationScale)
    expect(distant.secondaryOpacity).toBeLessThan(middle.secondaryOpacity)
    expect(middle.secondaryOpacity).toBeLessThan(near.secondaryOpacity)
    expect(distant.titleExpansion).toBeGreaterThan(middle.titleExpansion)
    expect(middle.titleExpansion).toBeGreaterThan(near.titleExpansion)
  })

  it('emits concrete CSS sizes without changing node geometry variables', () => {
    const variables = nodeReadabilityCssVariables(nodeReadabilityForViewport(0.32))

    expect(variables['--cave-node-title-size']).toBe('36.25px')
    expect(variables['--cave-node-title-height']).toBe('78.5px')
    expect(variables['--cave-node-title-condense']).toBe('0.72')
    expect(variables['--cave-node-title-margin']).toBe('15px')
    expect(variables['--cave-node-padding-top']).toBe('19px')
    expect(variables['--cave-node-icon-scale']).toBe('2.5')
    expect(variables['--cave-node-description-height']).toBe('0px')
    expect(variables['--cave-node-footer-height']).toBe('0px')
    expect(Object.keys(variables).some((name) => name.includes('width') || name.includes('position'))).toBe(false)
  })

  it('reserves clearance for scaled overview icons before the title', () => {
    const variables = nodeReadabilityCssVariables(nodeReadabilityForViewport(0.6))

    expect(variables['--cave-node-title-margin']).toBe('13.25px')
    expect(variables['--cave-nested-node-title-margin']).toBe('11.75px')
    expect(variables['--cave-node-padding-top']).toBe('15.25px')
    expect(variables['--cave-nested-node-padding-top']).toBe('12.75px')
  })

  it('reserves two complete title rows at distant and overview zooms', () => {
    const distant = nodeReadabilityCssVariables(nodeReadabilityForViewport(0.52))
    const overview = nodeReadabilityCssVariables(nodeReadabilityForViewport(0.6))

    expect(distant['--cave-node-title-height']).toBe('56.3105px')
    expect(overview['--cave-node-title-height']).toBe('51.656px')
  })

  it('allocates only complete description rows and footer rows', () => {
    const middle = nodeReadabilityCssVariables(nodeReadabilityForViewport(0.9))
    const near = nodeReadabilityCssVariables(nodeReadabilityForViewport(1))

    expect(middle['--cave-node-description-height']).toBe('12.6px')
    expect(middle['--cave-node-footer-height']).toBe('0px')
    expect(near['--cave-node-description-height']).toBe('25.2px')
    expect(near['--cave-node-footer-height']).toBe('10px')
  })
})
