export interface NodeReadability {
  effectiveZoom: number
  navigationScale: number
  indicatorScale: number
  secondaryOpacity: number
  metadataOpacity: number
  titleExpansion: number
  detailCapacity: number
  secondaryScale: number
}

export interface NodeReadabilityViewport {
  width: number
  height: number
}

const navigationTarget = 0.9
const indicatorTarget = 0.86

/**
 * Converts React Flow zoom into a device-pixel-aware card presentation.
 *
 * The returned values only affect content inside a node. Layout dimensions and
 * graph coordinates remain owned by the layout engine and never change here.
 */
export function nodeReadabilityForViewport(
  zoom: number,
  viewport?: NodeReadabilityViewport,
): NodeReadability {
  const safeZoom = positiveFiniteOr(zoom, 1)
  // CSS pixels are already device-independent. Multiplying by devicePixelRatio made
  // overview text physically smaller on the high-density displays that need this policy most.
  const effectiveZoom = safeZoom
  const detailCapacity = viewport === undefined
    ? 0
    : Math.min(
        smoothStep(1_500, 2_300, positiveFiniteOr(viewport.width, 1)),
        smoothStep(780, 1_160, positiveFiniteOr(viewport.height, 1)),
      )
  const secondaryLower = interpolate(0.76, 0.36, detailCapacity)
  const secondaryUpper = interpolate(0.96, 0.68, detailCapacity)
  const metadataLower = interpolate(0.84, 0.42, detailCapacity)
  const metadataUpper = interpolate(0.99, 0.7, detailCapacity)
  const secondaryOpacity = smoothStep(secondaryLower, secondaryUpper, effectiveZoom)
  const metadataOpacity = smoothStep(metadataLower, metadataUpper, effectiveZoom)
  const secondaryScale = secondaryOpacity >= 0.55
    ? (1 + (0.16 * detailCapacity)) * clamp(0.8 / effectiveZoom, 1, 1.5)
    : 1

  return {
    effectiveZoom,
    navigationScale: clamp(navigationTarget / effectiveZoom, 1, 2.5),
    indicatorScale: clamp(indicatorTarget / effectiveZoom, 1, 2.3),
    secondaryOpacity,
    metadataOpacity,
    titleExpansion: 1 - smoothStep(0.5, 0.85, effectiveZoom),
    detailCapacity,
    secondaryScale,
  }
}

/** Maps the readability policy to the inherited CSS variables used by every card. */
export function nodeReadabilityCssVariables(
  readability: NodeReadability,
): Record<string, string> {
  const titleExpansion = readability.titleExpansion
  const titleCondense = 1 - (0.28 * titleExpansion)
  const titleLineHeight = 1.25 - (0.25 * titleExpansion)
  // A single overview row clipped everything after the first semantic wrap point.
  // Projection-owned card heights reserve two complete rows at every camera zoom.
  const titleRows = 2
  const titleGlyphClearance = 6
  const titleSize = 14.5 * readability.navigationScale
  const nestedTitleSize = 13.5 * readability.navigationScale
  const iconOverflow = 12.5 * (readability.navigationScale - 1)
  const nestedIconOverflow = 11.5 * (readability.navigationScale - 1)
  const descriptionRows = readability.secondaryOpacity >= 0.92
    ? 2
    : readability.secondaryOpacity >= 0.55
      ? 1
      : 0
  const secondaryOpacity = descriptionRows === 0
    ? 0
    : Math.max(0.74, readability.secondaryOpacity)
  const footerVisible = readability.metadataOpacity >= 0.97
  const descriptionSize = 9.5 * readability.secondaryScale
  const nestedDescriptionSize = 8.5 * readability.secondaryScale

  return {
    '--cave-node-title-size': pixels(titleSize),
    '--cave-nested-node-title-size': pixels(nestedTitleSize),
    '--cave-node-title-height': pixels((titleSize * titleLineHeight * titleRows) + titleGlyphClearance),
    '--cave-nested-node-title-height': pixels((nestedTitleSize * titleLineHeight * titleRows) + titleGlyphClearance),
    '--cave-node-title-line-height': decimal(titleLineHeight),
    '--cave-node-title-condense': decimal(titleCondense),
    '--cave-node-title-margin': pixels(7 + Math.min(iconOverflow, 8)),
    '--cave-nested-node-title-margin': pixels(6 + Math.min(nestedIconOverflow, 7)),
    '--cave-node-icon-scale': decimal(readability.navigationScale),
    '--cave-node-indicator-scale': decimal(readability.indicatorScale),
    '--cave-node-secondary-opacity': decimal(secondaryOpacity),
    '--cave-node-metadata-opacity': decimal(readability.metadataOpacity),
    '--cave-node-kind-space': pixels(110 * readability.metadataOpacity),
    '--cave-node-footer-height': pixels(footerVisible ? 10 : 0),
    '--cave-node-footer-padding': pixels(footerVisible ? 2 : 0),
    '--cave-node-padding-top': pixels(9 + Math.min(iconOverflow, 10)),
    '--cave-node-padding-bottom': pixels(7 - (2 * titleExpansion)),
    '--cave-nested-node-padding-top': pixels(7 + Math.min(nestedIconOverflow, 9)),
    '--cave-nested-node-padding-bottom': pixels(6 - titleExpansion),
    '--cave-node-description-size': pixels(descriptionSize),
    '--cave-nested-node-description-size': pixels(nestedDescriptionSize),
    '--cave-node-description-height': pixels(12.6 * readability.secondaryScale * descriptionRows),
    '--cave-nested-node-description-height': pixels(11.9 * readability.secondaryScale * descriptionRows),
    '--cave-node-description-margin': pixels(descriptionRows === 0 ? 0 : 2),
  }
}

function interpolate(start: number, end: number, progress: number): number {
  return start + ((end - start) * progress)
}

function smoothStep(lower: number, upper: number, value: number): number {
  const progress = clamp((value - lower) / (upper - lower), 0, 1)
  return progress * progress * (3 - (2 * progress))
}

function positiveFiniteOr(value: number, fallback: number): number {
  return Number.isFinite(value) && value > 0 ? value : fallback
}

function clamp(value: number, minimum: number, maximum: number): number {
  return Math.min(maximum, Math.max(minimum, value))
}

function pixels(value: number): string {
  return `${decimal(value)}px`
}

function decimal(value: number): string {
  return value.toFixed(4).replace(/\.?0+$/, '')
}
