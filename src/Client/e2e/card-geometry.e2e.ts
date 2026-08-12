import { expect, test } from '@playwright/test'
import type { Locator, Page } from '@playwright/test'

const selectors = {
  card: null,
  top: '.card__top',
  cost: '.card__cost',
  digit: '.card__cost-n',
  name: '.card__name',
  illust: '.card__illust',
  desc: '.card__desc',
  type: '.card__type',
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
        digit: document.fonts.check('16px Silkscreen'),
        name: document.fonts.check('14px DotGothic16'),
        type: document.fonts.check('8px "Pixelify Sans"'),
      },
      digitBand: digit.dataset.digits,
      digitTextWidth: digitText.getBoundingClientRect().width,
      digitInnerWidth: digit.clientWidth,
    }
  }, selectors)
}

function expectSameGeometry(actual: Geometry, expected: Geometry) {
  const textParts = new Set(['digit', 'name', 'illust', 'desc', 'type'])
  for (const key of Object.keys(expected.parts)) {
    const actualPart = actual.parts[key]
    const expectedPart = expected.parts[key]
    expect(actualPart.visualWidth, `${key} width`).toBeCloseTo(expectedPart.visualWidth, 4)
    expect(actualPart.visualHeight, `${key} height`).toBeCloseTo(expectedPart.visualHeight, 4)
    expect(actualPart.cssWidth, `${key} CSS width`).toBe(expectedPart.cssWidth)
    expect(actualPart.cssHeight, `${key} CSS height`).toBe(expectedPart.cssHeight)
    // Structural wrappers may inherit a button's UA font, but every element
    // that actually draws card text must use the canonical card typography.
    if (textParts.has(key)) {
      expect(actualPart.fontFamily, `${key} font`).toBe(expectedPart.fontFamily)
      expect(actualPart.fontSize, `${key} font size`).toBe(expectedPart.fontSize)
      expect(actualPart.lineHeight, `${key} line height`).toBe(expectedPart.lineHeight)
      expect(actualPart.letterSpacing, `${key} letter spacing`).toBe(expectedPart.letterSpacing)
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
  expect(baseline.parts.cost.visualWidth).toBeCloseTo(22, 4)
  expect(baseline.parts.cost.visualHeight).toBeCloseTo(22, 4)
  expect(baseline.parts.digit.fontFamily).toContain('Silkscreen')
  expect(baseline.parts.digit.fontSize).toBe('16px')
  expect(baseline.parts.digit.transform).toBe('matrix(1, 0, 0, 1, 0, -1)')
  expect(baseline.costCenterDelta.x).toBeCloseTo(0, 5)
  expect(baseline.costCenterDelta.y).toBeCloseTo(-1, 5)
  expect(baseline.fontsLoaded).toEqual({ digit: true, name: true, type: true })

  const twoDigit = await readGeometry(cardAt(page, 'two-digit'))
  expect(twoDigit.parts.card.visualWidth).toBeCloseTo(baseline.parts.card.visualWidth, 4)
  expect(twoDigit.parts.card.visualHeight).toBeCloseTo(baseline.parts.card.visualHeight, 4)
  expect(twoDigit.parts.cost.visualWidth).toBeCloseTo(22, 4)
  expect(twoDigit.parts.digit.fontSize).toBe('13px')
  expect(twoDigit.digitBand).toBe('2')
  expect(twoDigit.digitTextWidth).toBeLessThanOrEqual(twoDigit.digitInnerWidth)

  const reducedCard = cardAt(page, 'reduced-cost')
  const reducedGeometry = await reducedCard.evaluate(root => {
    const currentCircle = root.querySelector<HTMLElement>('.card__cost--current')!
    const originalCircle = root.querySelector<HTMLElement>('.card__cost--orig')!
    const currentDigit = currentCircle.querySelector<HTMLElement>('.card__cost-n')!
    const originalDigit = originalCircle.querySelector<HTMLElement>('.card__cost-n')!
    const currentRect = currentCircle.getBoundingClientRect()
    const originalRect = originalCircle.getBoundingClientRect()
    const currentStyle = getComputedStyle(currentDigit)
    const currentCircleStyle = getComputedStyle(currentCircle)
    const originalCircleStyle = getComputedStyle(originalCircle)
    return {
      current: { x: currentRect.x, right: currentRect.right, bottom: currentRect.bottom },
      original: { x: originalRect.x, right: originalRect.right, top: originalRect.top },
      currentFontWeight: currentStyle.fontWeight,
      currentTextShadow: currentStyle.textShadow,
      originalTransform: getComputedStyle(originalDigit).transform,
      currentBorder: currentCircleStyle.borderColor,
      originalBorder: originalCircleStyle.borderColor,
      originalShadow: originalCircleStyle.boxShadow,
    }
  })
  expect(reducedGeometry.original.x).toBeCloseTo(reducedGeometry.current.x, 5)
  expect(reducedGeometry.original.right).toBeCloseTo(reducedGeometry.current.right, 5)
  expect(reducedGeometry.original.top - reducedGeometry.current.bottom).toBeCloseTo(2, 5)
  expect(reducedGeometry.currentFontWeight).toBe('400')
  expect(reducedGeometry.currentTextShadow).not.toContain('241, 201, 91')
  expect(reducedGeometry.originalTransform).toBe('matrix(1, 0, 0, 1, 0, 0)')
  expect(reducedGeometry.currentBorder).toBe('rgb(241, 201, 91)')
  expect(reducedGeometry.originalBorder).toBe('rgb(136, 136, 136)')
  expect(reducedGeometry.originalShadow).toBe('none')

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
    expectSameGeometry(await readGeometry(cardAt(page, context)), baseline)
  }

  const rewardCard = cardAt(page, 'reward')
  const rewardBefore = await readGeometry(rewardCard)
  await page.locator('[data-card-context="reward"] .rw-picker__card').hover()
  await expect.poll(async () => (
    (await readGeometry(rewardCard)).parts.card.visualWidth
      / rewardBefore.parts.card.visualWidth
  )).toBeCloseTo(1.03, 2)
  expectUniformScale(rewardBefore, await readGeometry(rewardCard), 1.03)

  const handCard = cardAt(page, 'hand')
  const handBefore = await readGeometry(handCard)
  await handCard.hover()
  await expect.poll(async () => (
    (await readGeometry(handCard)).parts.card.visualWidth
      / handBefore.parts.card.visualWidth
  )).toBeCloseTo(1.06, 2)
  expectUniformScale(handBefore, await readGeometry(handCard), 1.06)

  expectUniformScale(baseline, await readGeometry(cardAt(page, 'playing')), 1.6)
})
