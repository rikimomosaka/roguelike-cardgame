import { expect, test } from '@playwright/test'
import type { Locator, Page } from '@playwright/test'
import { mkdirSync } from 'node:fs'
import { resolve } from 'node:path'

const selectors = {
  card: null,
  top: '.card__top',
  cost: '.card__cost',
  digit: '.card__cost-n',
  name: '.card__name',
  illust: '.card__illust',
  art: '.card__art-content',
  affiliationFrame: '.card__affiliation-frame',
  typeFrame: '.card__type-frame',
  desc: '.card__desc',
} as const

type Geometry = Awaited<ReturnType<typeof readGeometry>>

async function mountFixture(page: Page) {
  await page.goto('/')
  await page.evaluate(async () => {
    const load = (path: string) => import(path)
    const fixture = await load('/e2e/card-geometry.fixture.tsx')
    await fixture.mountCardGeometryFixture()
    await document.fonts.ready
  })
  await expect(page.locator('html')).toHaveAttribute('data-card-geometry-ready', 'true')
  // Wait for the normal hand-entry animation before taking exact measurements.
  await page.waitForTimeout(300)
}

function cardAt(page: Page, context: string) {
  return page.locator(`[data-card-context="${context}"] .card`).first()
}

async function readGeometry(card: Locator) {
  return card.evaluate((root, partSelectors) => {
    const parts: Record<string, {
      visualWidth: number
      visualHeight: number
      cssWidth: string
      cssHeight: string
      fontFamily: string
      fontSize: string
      lineHeight: string
      letterSpacing: string
      transform: string
    }> = {}

    for (const [name, selector] of Object.entries(partSelectors)) {
      const element = selector
        ? root.querySelector<HTMLElement>(selector)
        : root as HTMLElement
      if (!element) throw new Error(`Missing card part: ${name}`)

      const rect = element.getBoundingClientRect()
      const style = getComputedStyle(element)
      parts[name] = {
        visualWidth: rect.width,
        visualHeight: rect.height,
        cssWidth: style.width,
        cssHeight: style.height,
        fontFamily: style.fontFamily,
        fontSize: style.fontSize,
        lineHeight: style.lineHeight,
        letterSpacing: style.letterSpacing,
        transform: style.transform,
      }
    }

    const cost = root.querySelector<HTMLElement>('.card__cost')!
    const digit = root.querySelector<HTMLElement>('.card__cost-n')!
    const costRect = cost.getBoundingClientRect()
    const digitRect = digit.getBoundingClientRect()
    const digitText = document.createRange()
    digitText.selectNodeContents(digit)
    return {
      parts,
      costCenterDelta: {
        x: digitRect.x + digitRect.width / 2 - (costRect.x + costRect.width / 2),
        y: digitRect.y + digitRect.height / 2 - (costRect.y + costRect.height / 2),
      },
      fontsLoaded: {
        digit: document.fonts.check('20px Silkscreen'),
        name: document.fonts.check('14px DotGothic16'),
      },
      digitBand: digit.dataset.digits,
      digitTextWidth: digitText.getBoundingClientRect().width,
      digitInnerWidth: digit.clientWidth,
    }
  }, selectors)
}

function expectSameGeometry(actual: Geometry, expected: Geometry, context = 'card') {
  const textParts = new Set(['digit', 'name', 'art', 'desc'])
  for (const key of Object.keys(expected.parts)) {
    const actualPart = actual.parts[key]
    const expectedPart = expected.parts[key]
    expect(actualPart.visualWidth, `${context} ${key} width`).toBeCloseTo(expectedPart.visualWidth, 4)
    expect(actualPart.visualHeight, `${context} ${key} height`).toBeCloseTo(expectedPart.visualHeight, 4)
    expect(actualPart.cssWidth, `${context} ${key} CSS width`).toBe(expectedPart.cssWidth)
    expect(actualPart.cssHeight, `${context} ${key} CSS height`).toBe(expectedPart.cssHeight)
    // Structural wrappers may inherit a button's UA font, but every element
    // that actually draws card text must use the canonical card typography.
    if (textParts.has(key)) {
      expect(actualPart.fontFamily, `${context} ${key} font`).toBe(expectedPart.fontFamily)
      expect(actualPart.fontSize, `${context} ${key} font size`).toBe(expectedPart.fontSize)
      expect(actualPart.lineHeight, `${context} ${key} line height`).toBe(expectedPart.lineHeight)
      expect(actualPart.letterSpacing, `${context} ${key} letter spacing`).toBe(expectedPart.letterSpacing)
    }
  }
  expect(actual.costCenterDelta.x).toBeCloseTo(expected.costCenterDelta.x, 5)
  expect(actual.costCenterDelta.y).toBeCloseTo(expected.costCenterDelta.y, 5)
}

function expectUniformScale(before: Geometry, after: Geometry, expectedScale: number) {
  for (const key of Object.keys(before.parts)) {
    const beforePart = before.parts[key]
    const afterPart = after.parts[key]
    expect(afterPart.visualWidth / beforePart.visualWidth, `${key} width scale`)
      .toBeCloseTo(expectedScale, 3)
    expect(afterPart.visualHeight / beforePart.visualHeight, `${key} height scale`)
      .toBeCloseTo(expectedScale, 3)
    expect(afterPart.cssWidth, `${key} CSS width`).toBe(beforePart.cssWidth)
    expect(afterPart.cssHeight, `${key} CSS height`).toBe(beforePart.cssHeight)
    expect(afterPart.fontFamily, `${key} font`).toBe(beforePart.fontFamily)
    expect(afterPart.fontSize, `${key} font size`).toBe(beforePart.fontSize)
  }
}

test('all full-card contexts share exact geometry and scale as one composition', async ({ page }) => {
  await mountFixture(page)

  const baseline = await readGeometry(cardAt(page, 'plain'))
  expect(baseline.parts.card.visualWidth).toBeCloseTo(112, 4)
  expect(baseline.parts.card.visualHeight).toBeCloseTo(156.8, 1)
  expect(baseline.parts.cost.visualWidth).toBeCloseTo(28, 4)
  expect(baseline.parts.cost.visualHeight).toBeCloseTo(28, 4)
  expect(baseline.parts.digit.fontFamily).toContain('Silkscreen')
  expect(baseline.parts.digit.fontSize).toBe('20px')
  expect(baseline.parts.digit.transform).toBe('matrix(1, 0, 0, 1, 0, -2)')
  expect(baseline.costCenterDelta.x).toBeCloseTo(0, 5)
  expect(baseline.costCenterDelta.y).toBeCloseTo(-2, 5)
  expect(baseline.fontsLoaded).toEqual({ digit: true, name: true })
  expect(baseline.parts.illust.visualWidth).toBeCloseTo(112, 4)
  expect(baseline.parts.illust.visualHeight).toBeCloseTo(156.8, 1)
  expect(baseline.parts.desc.visualWidth).toBeCloseTo(94, 4)
  expect(baseline.parts.desc.visualHeight).toBeGreaterThanOrEqual(49)

  const twoDigit = await readGeometry(cardAt(page, 'two-digit'))
  expect(twoDigit.parts.card.visualWidth).toBeCloseTo(baseline.parts.card.visualWidth, 4)
  expect(twoDigit.parts.card.visualHeight).toBeCloseTo(baseline.parts.card.visualHeight, 4)
  expect(twoDigit.parts.cost.visualWidth).toBeCloseTo(28, 4)
  expect(twoDigit.parts.digit.fontSize).toBe('16px')
  expect(twoDigit.digitBand).toBe('2')
  expect(twoDigit.digitTextWidth).toBeLessThanOrEqual(twoDigit.digitInnerWidth)

  const reducedCard = cardAt(page, 'reduced-cost')
  const reducedGeometry = await reducedCard.evaluate(root => {
    const cardRect = root.getBoundingClientRect()
    const currentCost = root.querySelector<HTMLElement>('.card__cost--current')!
    const originalCost = root.querySelector<HTMLElement>('.card__cost--orig')!
    const currentDigit = currentCost.querySelector<HTMLElement>('.card__cost-n')!
    const originalDigit = originalCost.querySelector<HTMLElement>('.card__cost-n')!
    const currentRect = currentCost.getBoundingClientRect()
    const originalRect = originalCost.getBoundingClientRect()
    const currentStyle = getComputedStyle(currentDigit)
    const currentCostStyle = getComputedStyle(currentCost)
    const originalCostStyle = getComputedStyle(originalCost)
    const auraStyle = getComputedStyle(root, '::before')
    return {
      card: {
        x: cardRect.x,
        top: cardRect.top,
      },
      current: {
        x: currentRect.x,
        top: currentRect.top,
        right: currentRect.right,
        bottom: currentRect.bottom,
        width: currentRect.width,
        centerX: currentRect.x + currentRect.width / 2,
      },
      original: {
        x: originalRect.x,
        right: originalRect.right,
        top: originalRect.top,
        width: originalRect.width,
        centerX: originalRect.x + originalRect.width / 2,
      },
      currentFontWeight: currentStyle.fontWeight,
      currentColor: currentStyle.color,
      currentTextShadow: currentStyle.textShadow,
      originalTransform: getComputedStyle(originalDigit).transform,
      originalFontSize: getComputedStyle(originalDigit).fontSize,
      originalTextDecoration: getComputedStyle(originalDigit).textDecorationLine,
      currentBackground: currentCostStyle.backgroundColor,
      currentPlateCount: currentCost.querySelectorAll('.card__cost-base').length,
      originalPlateCount: originalCost.querySelectorAll('.card__cost-base').length,
      auraContent: auraStyle.content,
      auraBorder: auraStyle.borderColor,
      originalBorder: originalCostStyle.borderColor,
      originalShadow: originalCostStyle.boxShadow,
      cardOverflow: getComputedStyle(root).overflow,
      topZ: getComputedStyle(root.querySelector<HTMLElement>('.card__top')!).zIndex,
    }
  })
  expect(reducedGeometry.current.width).toBeCloseTo(28, 5)
  expect(reducedGeometry.original.width).toBeCloseTo(22, 5)
  expect(reducedGeometry.current.x).toBeLessThan(reducedGeometry.card.x)
  expect(reducedGeometry.current.top).toBeLessThan(reducedGeometry.card.top)
  expect(reducedGeometry.original.centerX).toBeCloseTo(reducedGeometry.current.centerX, 5)
  expect(reducedGeometry.original.top - reducedGeometry.current.bottom).toBeCloseTo(-1, 5)
  expect(reducedGeometry.currentFontWeight).toBe('400')
  expect(reducedGeometry.currentColor).toBe('rgb(255, 213, 79)')
  expect(reducedGeometry.currentTextShadow).not.toContain('241, 201, 91')
  expect(reducedGeometry.originalTransform).toBe('matrix(1, 0, 0, 1, 0, -1)')
  expect(reducedGeometry.originalFontSize).toBe('13px')
  expect(reducedGeometry.originalTextDecoration).toBe('none')
  expect(reducedGeometry.currentBackground).toBe('rgba(0, 0, 0, 0)')
  expect(reducedGeometry.currentPlateCount).toBe(1)
  expect(reducedGeometry.originalPlateCount).toBe(1)
  expect(reducedGeometry.auraContent).not.toBe('none')
  expect(reducedGeometry.auraBorder).not.toBe('rgba(0, 0, 0, 0)')
  expect(reducedGeometry.originalBorder).toBe('rgba(0, 0, 0, 0)')
  expect(reducedGeometry.originalShadow).toBe('none')
  expect(reducedGeometry.cardOverflow).toBe('visible')
  expect(Number(reducedGeometry.topZ)).toBeGreaterThan(5)

  const authoritativeOriginal = cardAt(page, 'cost-original-only')
  await expect(authoritativeOriginal.locator('.card__cost--orig')).toHaveText('3')
  await expect(authoritativeOriginal.locator('.card__cost--current')).toHaveText('1')
  await expect(authoritativeOriginal.locator('.card__cost--current .card__cost-base')).toHaveCount(1)
  await expect(authoritativeOriginal.locator('.card__cost--orig .card__cost-base')).toHaveCount(1)

  const unavailableCost = await cardAt(page, 'unavailable-cost').evaluate(element => {
    const cardRect = element.getBoundingClientRect()
    const unavailable = element.querySelector<HTMLElement>('.card__cost-unavailable')!
    const stack = element.querySelector<HTMLElement>('.card__cost-stack')!
    const unavailableRect = unavailable.getBoundingClientRect()
    return {
      width: unavailableRect.width,
      height: unavailableRect.height,
      projectsLeft: unavailableRect.left < cardRect.left,
      projectsAbove: unavailableRect.top < cardRect.top,
      fontSize: getComputedStyle(unavailable).fontSize,
      unavailableZ: Number(getComputedStyle(unavailable).zIndex),
      stackZ: Number(getComputedStyle(stack).zIndex),
      costPlateCount: stack.querySelectorAll('.card__cost-base').length,
    }
  })
  expect(unavailableCost).toEqual({
    width: 28,
    height: 28,
    projectsLeft: true,
    projectsAbove: true,
    fontSize: '28px',
    unavailableZ: 6,
    stackZ: 5,
    costPlateCount: 2,
  })
  expect(unavailableCost.unavailableZ).toBeGreaterThan(unavailableCost.stackZ)

  const increasedCost = cardAt(page, 'increased-cost')
  const increasedPresentation = await increasedCost.evaluate(element => {
    const digit = element.querySelector<HTMLElement>('.card__cost--current .card__cost-n')!
    const probe = document.createElement('span')
    probe.style.color = 'var(--state-block)'
    element.append(probe)
    const stateBlockColor = getComputedStyle(probe).color
    probe.remove()
    return {
      current: element.querySelector<HTMLElement>('.card__cost--current')!.textContent,
      original: element.querySelector<HTMLElement>('.card__cost--orig')!.textContent,
      currentColor: getComputedStyle(digit).color,
      stateBlockColor,
      plates: element.querySelectorAll('.card__cost-base').length,
    }
  })
  expect(increasedPresentation).toEqual({
    current: '3',
    original: '2',
    currentColor: increasedPresentation.stateBlockColor,
    stateBlockColor: increasedPresentation.stateBlockColor,
    plates: 2,
  })

  const transformedCost = await cardAt(page, 'transformed-cost').evaluate(element => {
    const currentPlate = element.querySelector<HTMLImageElement>('.card__cost--current .card__cost-base')!
    const originalPlate = element.querySelector<HTMLImageElement>('.card__cost--orig .card__cost-base')!
    const currentRect = currentPlate.getBoundingClientRect()
    const originalRect = originalPlate.getBoundingClientRect()
    return {
      currentNaturalSize: [currentPlate.naturalWidth, currentPlate.naturalHeight],
      originalNaturalSize: [originalPlate.naturalWidth, originalPlate.naturalHeight],
      currentVisible: currentRect.width > 0 && currentRect.height > 0,
      originalVisible: originalRect.width > 0 && originalRect.height > 0,
      currentLocalSize: [currentPlate.offsetWidth, currentPlate.offsetHeight],
      originalLocalSize: [originalPlate.offsetWidth, originalPlate.offsetHeight],
    }
  })
  expect(transformedCost).toMatchObject({
    currentNaturalSize: [128, 128],
    originalNaturalSize: [128, 128],
    currentVisible: true,
    originalVisible: true,
  })
  expect(transformedCost.currentLocalSize[0]).toBeCloseTo(30, 4)
  expect(transformedCost.currentLocalSize[1]).toBeCloseTo(30, 4)
  expect(transformedCost.originalLocalSize[0]).toBeCloseTo(22, 4)
  expect(transformedCost.originalLocalSize[1]).toBeCloseTo(22, 4)

  for (const context of [
    'reward',
    'merchant-sale',
    'merchant-picker',
    'rest-picker',
    'topbar',
    'achievement',
    'choice',
    'hand',
  ]) {
    expectSameGeometry(await readGeometry(cardAt(page, context)), baseline, context)
  }

  const rewardCard = cardAt(page, 'reward')
  const rewardBefore = await readGeometry(rewardCard)
  await page.locator('[data-card-context="reward"] .rw-picker__card').hover()
  await expect.poll(async () => (
    (await readGeometry(rewardCard)).parts.card.visualWidth
      / rewardBefore.parts.card.visualWidth
  )).toBeCloseTo(1.03, 3)
  expectUniformScale(rewardBefore, await readGeometry(rewardCard), 1.03)

  const handCard = cardAt(page, 'hand')
  const handBefore = await readGeometry(handCard)
  await handCard.hover()
  await expect.poll(async () => (
    (await readGeometry(handCard)).parts.card.visualWidth
      / handBefore.parts.card.visualWidth
  )).toBeCloseTo(1.06, 3)
  expectUniformScale(handBefore, await readGeometry(handCard), 1.06)

  expectUniformScale(baseline, await readGeometry(cardAt(page, 'playing')), 1.6)
  const exhaustCard = cardAt(page, 'effect-exhaust')
  expectUniformScale(baseline, await readGeometry(exhaustCard), 1.6)
  const exhaustBox = await exhaustCard.boundingBox()
  expect(exhaustBox).not.toBeNull()
  expect(exhaustBox!.x + exhaustBox!.width / 2).toBeCloseTo(640, 0)
  expect(exhaustBox!.y + exhaustBox!.height / 2).toBeCloseTo(360, 0)
  await expect(exhaustCard.locator('.card__desc')).toHaveText('5ダメージ。4ブロック。')
})

test('PNG affiliation and type layers stay distinct inside the fixed card box', async ({ page }) => {
  await mountFixture(page)

  const baseline = await cardAt(page, 'plain').boundingBox()
  expect(baseline).not.toBeNull()

  const affiliationSources: string[] = []
  for (const pool of ['striker', 'guardian', 'tactician', 'enhancer', 'universal']) {
    const card = cardAt(page, `pool-${pool}`)
    const box = await card.boundingBox()
    expect(box).not.toBeNull()
    expect(box!.width, `${pool} card width`).toBeCloseTo(baseline!.width, 4)
    expect(box!.height, `${pool} card height`).toBeCloseTo(baseline!.height, 4)

    const affiliation = await card.evaluate(element => {
      const frame = element.querySelector<HTMLImageElement>('.card__affiliation-frame')!
      const rect = frame.getBoundingClientRect()
      return {
        source: new URL(frame.src).pathname,
        width: rect.width,
        height: rect.height,
        naturalWidth: frame.naturalWidth,
        naturalHeight: frame.naturalHeight,
        zIndex: getComputedStyle(frame).zIndex,
        costPlateCount: element.querySelectorAll('.card__cost-base').length,
      }
    })
    expect(affiliation).toMatchObject({
      source: `/cards/frames/affiliations/${pool}.png`,
      naturalWidth: 560,
      naturalHeight: 784,
      zIndex: '0',
      costPlateCount: 1,
    })
    expect(affiliation.width).toBeCloseTo(baseline!.width, 4)
    expect(affiliation.height).toBeCloseTo(baseline!.height, 1)
    affiliationSources.push(affiliation.source)
  }
  expect(new Set(affiliationSources).size).toBe(affiliationSources.length)

  const typeMasks: string[] = []
  for (const type of ['attack', 'skill', 'power', 'curse', 'status', 'unit']) {
    const card = cardAt(page, `type-${type}`)
    const layer = await card.evaluate(async (element, cardType) => {
      const illustration = element.querySelector<HTMLElement>('.card__illust')!
      const artContent = element.querySelector<HTMLElement>('.card__art-content')!
      const frame = element.querySelector<HTMLImageElement>('.card__type-frame')!
      const name = element.querySelector<HTMLElement>('.card__name')!
      const style = getComputedStyle(illustration)
      const mask = new Image()
      mask.src = `/cards/frames/masks/${cardType}.png`
      await Promise.all([mask.decode(), frame.decode()])
      const canvas = document.createElement('canvas')
      canvas.width = mask.naturalWidth
      canvas.height = mask.naturalHeight
      const context = canvas.getContext('2d', { willReadFrequently: true })!
      context.drawImage(mask, 0, 0)
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data
      let minX = canvas.width
      let minY = canvas.height
      let maxX = -1
      let maxY = -1
      for (let y = 0; y < canvas.height; y++) {
        for (let x = 0; x < canvas.width; x++) {
          if (pixels[(y * canvas.width + x) * 4 + 3] === 0) continue
          minX = Math.min(minX, x)
          minY = Math.min(minY, y)
          maxX = Math.max(maxX, x)
          maxY = Math.max(maxY, y)
        }
      }
      let maskContainsSharedFocusRegion = true
      // The focus contract uses half-open image coordinates: [120, 445) x
      // [155, 405). Rounded silhouette pixels may sit on the following edge.
      for (let y = 155; y < 405 && maskContainsSharedFocusRegion; y++) {
        for (let x = 120; x < 445; x++) {
          if (pixels[(y * canvas.width + x) * 4 + 3] !== 0) continue
          maskContainsSharedFocusRegion = false
          break
        }
      }

      context.clearRect(0, 0, canvas.width, canvas.height)
      context.drawImage(frame, 0, 0, canvas.width, canvas.height)
      const framePixels = context.getImageData(0, 0, canvas.width, canvas.height).data
      let frameAlphaTop = canvas.height
      let frameAlphaBottom = -1
      for (let y = 0; y < canvas.height; y++) {
        for (let x = 0; x < canvas.width; x++) {
          if (framePixels[(y * canvas.width + x) * 4 + 3] === 0) continue
          frameAlphaTop = Math.min(frameAlphaTop, y)
          frameAlphaBottom = Math.max(frameAlphaBottom, y)
        }
      }
      const frameRect = frame.getBoundingClientRect()
      const nameTextRange = document.createRange()
      nameTextRange.selectNodeContents(name)
      const nameTextRect = nameTextRange.getBoundingClientRect()
      const typeFrameOpaqueVisualTop = frameRect.top
        + frameAlphaTop / frame.naturalHeight * frameRect.height
      const illustrationRect = illustration.getBoundingClientRect()
      const artRect = artContent.getBoundingClientRect()
      const aperture = {
        left: illustrationRect.left + minX / canvas.width * illustrationRect.width,
        top: illustrationRect.top + minY / canvas.height * illustrationRect.height,
        right: illustrationRect.left + (maxX + 1) / canvas.width * illustrationRect.width,
        bottom: illustrationRect.top + (maxY + 1) / canvas.height * illustrationRect.height,
      }
      return {
        maskImage: style.maskImage || style.webkitMaskImage,
        clipPath: style.clipPath,
        frameSource: new URL(frame.src).pathname,
        frameNaturalWidth: frame.naturalWidth,
        frameNaturalHeight: frame.naturalHeight,
        frameZ: getComputedStyle(frame).zIndex,
        artZ: style.zIndex,
        frameAlphaTop,
        frameAlphaBottom,
        typeFrameOpaqueVisualTop,
        nameTextBottom: nameTextRect.bottom,
        maskContainsSharedFocusRegion,
        artCoversAperture: artRect.left <= aperture.left
          && artRect.top <= aperture.top
          && artRect.right >= aperture.right
          && artRect.bottom >= aperture.bottom,
      }
    }, type)
    expect(layer.frameSource).toBe(`/cards/frames/types/${type}.png`)
    expect(layer.maskImage).toContain(`/cards/frames/masks/${type}.png`)
    expect(layer.clipPath).toBe('none')
    expect(layer).toMatchObject({
      frameNaturalWidth: 560,
      frameNaturalHeight: 784,
      frameZ: '2',
      artZ: '1',
      maskContainsSharedFocusRegion: true,
      artCoversAperture: true,
    })
    expect(layer.frameAlphaTop, `${type} type frame reaches into the title`).toBeGreaterThanOrEqual(100)
    expect(layer.frameAlphaBottom, `${type} type frame reaches into body copy`).toBeLessThanOrEqual(505)
    expect(
      layer.typeFrameOpaqueVisualTop,
      `${type} type frame intersects the rendered card name`,
    ).toBeGreaterThanOrEqual(layer.nameTextBottom)
    typeMasks.push(layer.maskImage)
  }
  expect(new Set(typeMasks).size).toBe(typeMasks.length)

  await expect(cardAt(page, 'plain').locator('.card__affiliation-frame'))
    .toHaveAttribute('src', '/cards/frames/affiliations/universal.png')
  await expect(cardAt(page, 'type-curse')).toHaveAttribute('data-card-affiliation', 'curse')
  await expect(cardAt(page, 'type-curse').locator('.card__affiliation-frame'))
    .toHaveAttribute('src', '/cards/frames/affiliations/curse.png')

  const combinedState = await cardAt(page, 'combined-frame-states').evaluate(element => {
    const cardStyle = getComputedStyle(element)
    const selectionStyle = getComputedStyle(element, '::after')
    const frame = element.querySelector<HTMLImageElement>('.card__affiliation-frame')!
    const typeFrameStyle = getComputedStyle(
      element.querySelector<HTMLElement>('.card__type-frame')!,
    )
    const contingencyStyle = getComputedStyle(
      element.querySelector<HTMLElement>('.card__contingency-ring')!,
    )
    return {
      boxShadow: cardStyle.boxShadow,
      selectionBorderColor: selectionStyle.borderColor,
      frameSource: new URL(frame.src).pathname,
      typeFrameZ: typeFrameStyle.zIndex,
      contingencyZ: contingencyStyle.zIndex,
    }
  })
  expect(combinedState.boxShadow).not.toBe('none')
  expect(combinedState.selectionBorderColor).toBe('rgb(255, 213, 79)')
  expect(combinedState.frameSource).toBe('/cards/frames/affiliations/tactician.png')
  expect(Number(combinedState.contingencyZ)).toBeGreaterThan(Number(combinedState.typeFrameZ))
})

test('projecting costs stay visible at clipped list edges and avoid the finisher order badge', async ({ page }) => {
  await mountFixture(page)

  await cardAt(page, 'edge-rest').locator('xpath=..').hover()

  const edgeCases = [
    ['edge-topbar', '.topbar__deck-list', '.card__cost--current .card__cost-base'],
    ['edge-pile', '.pile-modal__list', '.card__cost-unavailable'],
    ['edge-choice-selected', '.ccm-card-grid', '.card__cost-unavailable'],
    ['edge-rest', '.rs-picker-body', '.card__cost--current .card__cost-base'],
    ['edge-summon-rail', '.achievements__summon-cards', '.card__cost--current .card__cost-base'],
    ['edge-achievement-grid', '.achievements__scroll', '.card__cost-unavailable'],
  ] as const

  for (const [context, viewportSelector, visualSelector] of edgeCases) {
    const metrics = await cardAt(page, context).evaluate((card, selectors) => {
      const viewport = card.closest<HTMLElement>(selectors.viewport)!
      const visual = card.querySelector<HTMLElement>(selectors.visual)!
      const viewportRect = viewport.getBoundingClientRect()
      const visualRect = visual.getBoundingClientRect()
      return {
        viewportLeft: viewportRect.left,
        viewportTop: viewportRect.top,
        visualLeft: visualRect.left,
        visualTop: visualRect.top,
      }
    }, { viewport: viewportSelector, visual: visualSelector })
    expect(metrics.visualLeft, `${context} clips the left cost edge`)
      .toBeGreaterThanOrEqual(metrics.viewportLeft)
    expect(metrics.visualTop, `${context} clips the top cost edge`)
      .toBeGreaterThanOrEqual(metrics.viewportTop)
  }

  const finisherOverlap = await cardAt(page, 'finisher-order').evaluate(card => {
    const cost = card
      .querySelector<HTMLElement>('.card__cost--current .card__cost-base')!
      .getBoundingClientRect()
    const order = card.parentElement!
      .querySelector<HTMLElement>('.finisher-record__order')!
      .getBoundingClientRect()
    return !(
      cost.right <= order.left
      || order.right <= cost.left
      || cost.bottom <= order.top
      || order.bottom <= cost.top
    )
  })
  expect(finisherOverlap).toBe(false)
})

test('finisher record modal remains inside the fixed-height game stage on tall viewports', async ({ page }) => {
  await page.setViewportSize({ width: 2560, height: 1440 })
  await mountFixture(page)

  const metrics = await page.evaluate(() => {
    const stage = document.createElement('div')
    stage.style.position = 'fixed'
    stage.style.inset = '0 auto auto 0'
    stage.style.width = '1280px'
    stage.style.height = '720px'
    stage.style.overflow = 'hidden'
    stage.style.transform = 'translateZ(0)'
    stage.style.setProperty('--app-stage-height', '720px')

    const backdrop = document.createElement('div')
    backdrop.className = 'finisher-record__backdrop'
    const modal = document.createElement('section')
    modal.className = 'finisher-record__modal'
    modal.style.height = '1000px'
    modal.textContent = 'height regression fixture'
    backdrop.append(modal)
    stage.append(backdrop)
    document.body.append(stage)

    const stageRect = stage.getBoundingClientRect()
    const modalRect = modal.getBoundingClientRect()
    const modalStyle = getComputedStyle(modal)
    return {
      stageTop: stageRect.top,
      stageBottom: stageRect.bottom,
      modalTop: modalRect.top,
      modalBottom: modalRect.bottom,
      modalHeight: modalRect.height,
      maxHeight: modalStyle.maxHeight,
      boxSizing: modalStyle.boxSizing,
    }
  })

  expect(metrics.maxHeight).toBe('672px')
  expect(metrics.boxSizing).toBe('border-box')
  expect(metrics.modalHeight).toBeCloseTo(672, 2)
  expect(metrics.modalTop).toBeGreaterThanOrEqual(metrics.stageTop)
  expect(metrics.modalBottom).toBeLessThanOrEqual(metrics.stageBottom)
})

test('only overflowing text is squeezed and titles remain centered on the card', async ({ page }) => {
  await mountFixture(page)

  const description = await cardAt(page, 'long-text').evaluate(element => {
    const card = element.getBoundingClientRect()
    const desc = element.querySelector<HTMLElement>('.card__desc')!
    const descRect = desc.getBoundingClientRect()
    return {
      clientWidth: desc.clientWidth,
      scrollWidth: desc.scrollWidth,
      clientHeight: desc.clientHeight,
      scrollHeight: desc.scrollHeight,
      lines: desc.querySelectorAll('.card-desc-line').length,
      centerOffset: descRect.left + descRect.width / 2 - (card.left + card.width / 2),
    }
  })
  expect(description.clientWidth).toBe(94)
  expect(description.scrollWidth).toBeLessThanOrEqual(description.clientWidth)
  expect(description.scrollHeight).toBeLessThanOrEqual(description.clientHeight)
  expect(description.lines).toBeGreaterThanOrEqual(4)
  expect(description.centerOffset).toBeCloseTo(2, 5)

  const verticalMetrics = async (context: string) => cardAt(page, context).evaluate(element => {
    const desc = element.querySelector<HTMLElement>('.card__desc')!
    const richText = desc.querySelector<HTMLElement>('.card-desc')!
    const descRect = desc.getBoundingClientRect()
    const richTextRect = richText.getBoundingClientRect()
    const style = getComputedStyle(richText)
    return {
      lines: richText.dataset.lines,
      topGap: richTextRect.top - descRect.top,
      bottomGap: descRect.bottom - richTextRect.bottom,
      lineHeight: style.lineHeight,
      transform: style.transform,
    }
  })

  for (const [context, lines, lineHeight] of [
    ['text-short', '1', '11.25px'],
    ['two-line-text', '2', '12.15px'],
    ['keyword-boundary-text', '3', '12.15px'],
    ['long-text', '4', '9.72px'],
    ['five-line-text', '5', '9px'],
  ] as const) {
    const metrics = await verticalMetrics(context)
    expect(metrics.lines, `${context} line band`).toBe(lines)
    expect(metrics.topGap, `${context} top inset`).toBeCloseTo(4, 2)
    expect(metrics.bottomGap, `${context} bottom outline clearance`).toBeGreaterThan(1.5)
    expect(metrics.lineHeight, `${context} line rhythm`).toBe(lineHeight)
    if (context === 'five-line-text') {
      expect(metrics.transform).toBe('matrix(1, 0, 0, 0.88, 0, 0)')
    } else {
      expect(metrics.transform).toBe('none')
    }
  }

  const fiveLineDescription = await cardAt(page, 'five-line-text').evaluate(element => {
    const desc = element.querySelector<HTMLElement>('.card__desc')!
    const richText = desc.querySelector<HTMLElement>('.card-desc')!
    const lastLine = desc.querySelectorAll<HTMLElement>('.card-desc-line-inner').item(4)
    const descRect = desc.getBoundingClientRect()
    const lastRect = lastLine.getBoundingClientRect()
    const style = getComputedStyle(richText)
    return {
      lines: richText.dataset.lines,
      lineHeight: style.lineHeight,
      textShadow: getComputedStyle(desc).textShadow,
      scrollHeight: desc.scrollHeight,
      clientHeight: desc.clientHeight,
      lastBottom: lastRect.bottom,
      descBottom: descRect.bottom,
    }
  })
  expect(fiveLineDescription.lines).toBe('5')
  expect(fiveLineDescription.lineHeight).toBe('9px')
  expect(fiveLineDescription.scrollHeight).toBeLessThanOrEqual(fiveLineDescription.clientHeight)
  expect(fiveLineDescription.lastBottom).toBeLessThan(fiveLineDescription.descBottom)
  expect(fiveLineDescription.textShadow.match(/rgb\(0, 0, 0\)/g)).toHaveLength(4)

  const lineMetrics = async (context: string) => cardAt(page, context).evaluate(element => {
    const wrapper = element.querySelector<HTMLElement>('.card-desc-line')!
    const inner = element.querySelector<HTMLElement>('.card-desc-line-inner')!
    const wrapperRect = wrapper.getBoundingClientRect()
    const innerRect = inner.getBoundingClientRect()
    const style = getComputedStyle(inner)
    return {
      wrapperLeft: wrapperRect.left,
      wrapperRight: wrapperRect.right,
      innerLeft: innerRect.left,
      innerRight: innerRect.right,
      fontSize: style.fontSize,
      letterSpacing: style.letterSpacing,
      transform: style.transform,
      fit: inner.dataset.fit ?? null,
    }
  })

  const shortLine = await lineMetrics('text-short')
  expect(shortLine.fontSize).toBe('9px')
  expect(shortLine.transform).toBe('none')
  expect(shortLine.letterSpacing).toBe('normal')
  expect(shortLine.fit).toBeNull()

  const longLine = await lineMetrics('text-long')
  expect(longLine.fontSize).toBe('9px')
  expect(longLine.fit).toBe('scaled')
  expect(longLine.transform).not.toBe('none')
  expect(longLine.innerLeft).toBeGreaterThanOrEqual(longLine.wrapperLeft)
  expect(longLine.innerRight).toBeLessThanOrEqual(longLine.wrapperRight)

  const keywordBoundaryLines = await cardAt(page, 'keyword-boundary-text').evaluate(element => (
    [...element.querySelectorAll<HTMLElement>('.card-desc-line')].map(wrapper => {
      const inner = wrapper.querySelector<HTMLElement>('.card-desc-line-inner')!
      const wrapperRect = wrapper.getBoundingClientRect()
      const innerRect = inner.getBoundingClientRect()
      return {
        text: inner.textContent,
        fit: inner.dataset.fit ?? null,
        wrapperLeft: wrapperRect.left,
        wrapperRight: wrapperRect.right,
        innerLeft: innerRect.left,
        innerRight: innerRect.right,
      }
    })
  ))
  expect(keywordBoundaryLines.map(line => line.text)).toEqual([
    '伏策：相手のアタック時。',
    '伏策：3コンボ達成時。',
    '再演：コスト2以下のアタック・スキル。',
  ])
  for (const line of keywordBoundaryLines) {
    expect(line.innerLeft, `${line.text} left outline inset`)
      .toBeGreaterThanOrEqual(line.wrapperLeft + 1.99)
    expect(line.innerRight, `${line.text} right outline inset`)
      .toBeLessThanOrEqual(line.wrapperRight - 1.99)
  }

  const localKeywordWidths = async (context: string) => cardAt(page, context).evaluate(element => (
    [...element.querySelectorAll<HTMLElement>('.card-desc-line')].map(wrapper => {
      const inner = wrapper.querySelector<HTMLElement>('.card-desc-line-inner')!
      const style = getComputedStyle(inner)
      const scaleX = style.transform === 'none'
        ? 1
        : Math.abs(new DOMMatrixReadOnly(style.transform).a)
      return {
        fit: inner.dataset.fit ?? null,
        availableWidth: wrapper.clientWidth - 4,
        fittedWidth: Number.parseFloat(style.width) * scaleX,
      }
    })
  ))
  const untransformedLocalWidths = await localKeywordWidths('keyword-boundary-text')
  const transformedLocalWidths = await localKeywordWidths('transformed-keyword-boundary-text')
  expect(transformedLocalWidths.map(line => line.fit)).toEqual(
    untransformedLocalWidths.map(line => line.fit),
  )
  for (const line of transformedLocalWidths) {
    expect(line.fittedWidth).toBeLessThanOrEqual(line.availableWidth + 0.01)
  }

  for (const context of ['name-five', 'name-seven', 'name-long']) {
    const metrics = await cardAt(page, context).evaluate(element => {
      const cardRect = element.getBoundingClientRect()
      const name = element.querySelector<HTMLElement>('.card__name')!
      const cost = element.querySelector<HTMLElement>('.card__cost')!
      const costPlate = element.querySelector<HTMLElement>('.card__cost--current .card__cost-base')!
      const nameRect = name.getBoundingClientRect()
      const costRect = cost.getBoundingClientRect()
      const costPlateRect = costPlate.getBoundingClientRect()
      const range = document.createRange()
      range.selectNodeContents(name)
      const textRect = range.getBoundingClientRect()
      return {
        clientWidth: name.clientWidth,
        scrollWidth: name.scrollWidth,
        nameLeft: nameRect.left,
        nameRight: nameRect.right,
        nameCenter: nameRect.left + nameRect.width / 2,
        cardCenter: cardRect.left + cardRect.width / 2,
        textCenter: textRect.left + textRect.width / 2,
        textLeft: textRect.left,
        textRight: textRect.right,
        costRight: Math.max(costRect.right, costPlateRect.right),
        fontSize: getComputedStyle(name).fontSize,
      }
    })
    expect(metrics.scrollWidth, `${context} title overflow`).toBeLessThanOrEqual(
      metrics.clientWidth,
    )
    expect(metrics.textLeft, `${context} overlaps the cost plate`).toBeGreaterThanOrEqual(
      metrics.costRight,
    )
    expect(metrics.textLeft, `${context} clips at title left`).toBeGreaterThanOrEqual(
      metrics.nameLeft,
    )
    expect(metrics.textRight, `${context} clips at title right`).toBeLessThanOrEqual(
      metrics.nameRight,
    )
    expect(metrics.textCenter, `${context} title centering`).toBeCloseTo(metrics.nameCenter, 0)
    expect(metrics.nameCenter, `${context} card centering`).toBeCloseTo(metrics.cardCenter, 5)
  }

  await expect(cardAt(page, 'name-five').locator('.card__name')).toHaveCSS('font-size', '13px')
  await expect(cardAt(page, 'name-seven').locator('.card__name')).toHaveCSS('font-size', '9px')
  await expect(cardAt(page, 'name-long').locator('.card__name')).toHaveCSS('font-size', '7px')

  const comboAura = await cardAt(page, 'combo-aura').evaluate(element => {
    const aura = getComputedStyle(element, '::before')
    const cost = element.querySelector<HTMLElement>('.card__cost--current')!
    const pulse = element.querySelector<HTMLElement>('.card__combo-pulse')!
    const pulseLayer = element.querySelector<HTMLElement>('.card__combo-aura')!
    const typeFrame = element.querySelector<HTMLElement>('.card__type-frame')!
    const top = element.querySelector<HTMLElement>('.card__top')!
    const desc = element.querySelector<HTMLElement>('.card__desc')!
    return {
      active: element.classList.contains('is-combo-continuing'),
      content: aura.content,
      borderColor: aura.borderColor,
      costAfterContent: getComputedStyle(cost, '::after').content,
      pulseCount: element.querySelectorAll('.card__combo-pulse').length,
      pulseAnimation: getComputedStyle(pulse).animationName,
      pulseDuration: getComputedStyle(pulse).animationDuration,
      pulseBackground: getComputedStyle(pulse).backgroundImage,
      pulseZ: getComputedStyle(pulseLayer).zIndex,
      typeFrameZ: getComputedStyle(typeFrame).zIndex,
      topZ: getComputedStyle(top).zIndex,
      descZ: getComputedStyle(desc).zIndex,
    }
  })
  expect(comboAura.active).toBe(true)
  expect(comboAura.content).not.toBe('none')
  expect(comboAura.borderColor).not.toBe('rgba(0, 0, 0, 0)')
  expect(comboAura.costAfterContent).toBe('none')
  expect(comboAura.pulseCount).toBe(1)
  expect(comboAura.pulseAnimation).toBe('card-combo-pulse')
  expect(comboAura.pulseDuration).toBe('1.65s')
  expect(comboAura.pulseBackground).toContain('radial-gradient')
  expect(Number(comboAura.pulseZ)).toBeGreaterThanOrEqual(Number(comboAura.typeFrameZ))
  expect(Number(comboAura.pulseZ)).toBeLessThan(Number(comboAura.topZ))
  expect(Number(comboAura.pulseZ)).toBeLessThan(Number(comboAura.descZ))

  const pulseMotion = await cardAt(page, 'combo-aura').evaluate(async element => {
    const pulse = element.querySelector<HTMLElement>('.card__combo-pulse')!
    const animation = pulse.getAnimations()[0]
    await animation.ready
    animation.pause()
    animation.currentTime = 0
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    const start = getComputedStyle(pulse)
    const startState = { transform: start.transform, opacity: start.opacity }
    animation.currentTime = 825
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    const middle = getComputedStyle(pulse)
    return {
      start: startState,
      middle: { transform: middle.transform, opacity: middle.opacity },
    }
  })
  expect(pulseMotion.middle.transform).not.toBe(pulseMotion.start.transform)
  expect(pulseMotion.middle.opacity).not.toBe(pulseMotion.start.opacity)

  await page.emulateMedia({ reducedMotion: 'reduce' })
  const reducedMotionAura = await cardAt(page, 'combo-aura').evaluate(element => {
    const pulse = getComputedStyle(element.querySelector<HTMLElement>('.card__combo-pulse')!)
    return {
      animation: pulse.animationName,
      opacity: pulse.opacity,
      transform: pulse.transform,
      filter: pulse.filter,
    }
  })
  expect(reducedMotionAura).toEqual({
    animation: 'none',
    opacity: '0.58',
    transform: 'none',
    filter: 'none',
  })
  await page.emulateMedia({ reducedMotion: 'no-preference' })
})

test('renders a reviewable PNG card fixture screenshot', async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 900 })
  await mountFixture(page)

  const outputDirectory = resolve(process.cwd(), '..', '..', 'artifacts')
  mkdirSync(outputDirectory, { recursive: true })
  await page.locator('#card-geometry-fixture').screenshot({
    path: resolve(outputDirectory, 'card-png-fixture.png'),
    animations: 'disabled',
  })
  await cardAt(page, 'reduced-cost').screenshot({
    path: resolve(outputDirectory, 'card-cost-stack-fixture.png'),
    animations: 'disabled',
  })
  await cardAt(page, 'five-line-text').screenshot({
    path: resolve(outputDirectory, 'card-five-line-fixture.png'),
    animations: 'disabled',
  })
  await cardAt(page, 'keyword-boundary-text').screenshot({
    path: resolve(outputDirectory, 'card-keyword-boundary-fixture.png'),
    animations: 'disabled',
  })

  const captureAtTripleScale = async (
    context: string,
    filename: string,
    animations: 'allow' | 'disabled' = 'disabled',
    outsidePadding = 0,
  ) => {
    const card = cardAt(page, context)
    const previousStyle = await card.getAttribute('style')
    await card.evaluate(element => {
      const fixture = document.getElementById('card-geometry-fixture')
      fixture?.querySelectorAll<HTMLElement>('.card').forEach(other => {
        if (other === element) return
        other.dataset.capturePreviousVisibility = other.style.visibility
        other.dataset.captureHidden = 'true'
        other.style.visibility = 'hidden'
      })
      Object.assign((element as HTMLElement).style, {
        position: 'fixed',
        top: '32px',
        left: '32px',
        zIndex: '2147483647',
        transform: 'scale(3)',
        transformOrigin: 'top left',
        transition: 'none',
      })
    })
    if (outsidePadding > 0) {
      const box = await card.boundingBox()
      if (!box) throw new Error(`Could not measure ${context} for screenshot`)
      await page.screenshot({
        path: resolve(outputDirectory, filename),
        animations,
        clip: {
          x: box.x - outsidePadding,
          y: box.y - outsidePadding,
          width: box.width + outsidePadding * 2,
          height: box.height + outsidePadding * 2,
        },
      })
    } else {
      await card.screenshot({
        path: resolve(outputDirectory, filename),
        animations,
      })
    }
    await card.evaluate((element, style) => {
      if (style === null) element.removeAttribute('style')
      else element.setAttribute('style', style)
      document.querySelectorAll<HTMLElement>('[data-capture-hidden="true"]').forEach(other => {
        other.style.visibility = other.dataset.capturePreviousVisibility ?? ''
        delete other.dataset.capturePreviousVisibility
        delete other.dataset.captureHidden
      })
    }, previousStyle)
  }
  await captureAtTripleScale('reduced-cost', 'card-cost-stack-fixture-3x.png', 'disabled', 24)
  await captureAtTripleScale('five-line-text', 'card-five-line-fixture-3x.png')
  await captureAtTripleScale('keyword-boundary-text', 'card-keyword-boundary-fixture-3x.png')
  await cardAt(page, 'combo-aura').evaluate(element => {
    const animations = element.querySelector<HTMLElement>('.card__combo-aura')!.getAnimations({ subtree: true })
    animations.forEach(animation => {
      animation.pause()
      animation.currentTime = 825
    })
  })
  await captureAtTripleScale('combo-aura', 'card-combo-pulse-fixture-3x.png', 'allow')
})

test('cost breakdown stays farther from the cursor than the normal card text', async ({ page }) => {
  await mountFixture(page)

  const card = cardAt(page, 'cost-breakdown')
  await card.hover()
  const tooltip = page.getByRole('tooltip')
  const panel = page.getByLabel('コスト変更の内訳')
  await expect(tooltip).toBeVisible()
  await expect(panel).toBeVisible()
  await expect(panel).toContainText('コスト2')
  await expect(panel).toContainText('[混乱]によりコスト3に変更')
  await expect(panel).toContainText('「手札整備+」によりコスト-1')
  await expect(panel).toContainText('＝2コスト')
  await expect(card.locator('.card__cost-stack')).toHaveCount(0)

  const [tooltipBox, panelBox] = await Promise.all([
    tooltip.boundingBox(),
    panel.boundingBox(),
  ])
  expect(tooltipBox).not.toBeNull()
  expect(panelBox).not.toBeNull()
  expect(panelBox!.x).toBeGreaterThanOrEqual(tooltipBox!.x + tooltipBox!.width)
  expect(panelBox!.y).toBeCloseTo(tooltipBox!.y, 0)

  await card.hover({ position: { x: 110, y: 20 } })
  await card.evaluate(element => {
    const host = element.closest<HTMLElement>('[data-card-context="cost-breakdown"]')
    if (!host) throw new Error('cost-breakdown fixture host was not found')
    host.style.position = 'fixed'
    host.style.right = '8px'
    host.style.top = '20px'
    host.style.zIndex = '20'
  })
  await card.hover({ position: { x: 110, y: 20 } })
  await expect(tooltip).toHaveAttribute('data-cost-side', 'left')

  const [leftTooltipBox, leftPanelBox] = await Promise.all([
    tooltip.boundingBox(),
    panel.boundingBox(),
  ])
  expect(leftTooltipBox).not.toBeNull()
  expect(leftPanelBox).not.toBeNull()
  expect(leftPanelBox!.x + leftPanelBox!.width).toBeLessThanOrEqual(leftTooltipBox!.x)
  expect(leftPanelBox!.y).toBeCloseTo(leftTooltipBox!.y, 0)
})
