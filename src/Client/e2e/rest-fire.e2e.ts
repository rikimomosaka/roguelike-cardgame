import { expect, test } from '@playwright/test'
import type { Locator, Page } from '@playwright/test'

async function mountFixture(page: Page) {
  await page.goto('/')
  await page.evaluate(async () => {
    const load = (path: string) => import(path)
    const fixture = await load('/e2e/rest-fire.fixture.tsx')
    await fixture.mountRestFireFixture()
  })
  await expect(page.locator('html')).toHaveAttribute('data-rest-fire-ready', 'true')
}

async function inspectPng(image: Locator) {
  return image.evaluate(async element => {
    const source = element as HTMLImageElement
    await source.decode()
    const canvas = document.createElement('canvas')
    canvas.width = source.naturalWidth
    canvas.height = source.naturalHeight
    const context = canvas.getContext('2d', { willReadFrequently: true })!
    context.drawImage(source, 0, 0)
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data
    let visible = 0
    let greenLike = 0
    let magentaLike = 0
    const frameWidth = canvas.height
    const frameVisible = Array.from(
      { length: Math.max(1, Math.floor(canvas.width / frameWidth)) },
      () => 0,
    )
    const frameBounds = frameVisible.map(() => ({
      minX: frameWidth,
      maxX: -1,
      minY: canvas.height,
      maxY: -1,
    }))
    const frameHashes = frameVisible.map(() => 2166136261)
    let minX = canvas.width
    let minY = canvas.height
    let maxX = -1
    let maxY = -1
    for (let pixel = 0; pixel < pixels.length; pixel += 4) {
      const red = pixels[pixel]
      const green = pixels[pixel + 1]
      const blue = pixels[pixel + 2]
      const alpha = pixels[pixel + 3]
      const index = pixel / 4
      const x = index % canvas.width
      const y = Math.floor(index / canvas.width)
      const frameIndex = Math.min(frameVisible.length - 1, Math.floor(x / frameWidth))
      for (const channel of [red, green, blue, alpha]) {
        frameHashes[frameIndex] = Math.imul(frameHashes[frameIndex] ^ channel, 16777619) >>> 0
      }
      if (alpha === 0) continue
      visible++
      frameVisible[frameIndex]++
      const localX = x - frameIndex * frameWidth
      frameBounds[frameIndex].minX = Math.min(frameBounds[frameIndex].minX, localX)
      frameBounds[frameIndex].maxX = Math.max(frameBounds[frameIndex].maxX, localX)
      frameBounds[frameIndex].minY = Math.min(frameBounds[frameIndex].minY, y)
      frameBounds[frameIndex].maxY = Math.max(frameBounds[frameIndex].maxY, y)
      minX = Math.min(minX, x)
      minY = Math.min(minY, y)
      maxX = Math.max(maxX, x)
      maxY = Math.max(maxY, y)
      if (green > red + 24 && green > blue + 24) greenLike++
      if (red > 170 && blue > 120 && green < 100) magentaLike++
    }
    return {
      naturalWidth: source.naturalWidth,
      naturalHeight: source.naturalHeight,
      visible,
      greenLike,
      magentaLike,
      frameVisible,
      frameBounds,
      frameHashes,
      bounds: { minX, minY, maxX, maxY },
    }
  })
}

test('rest fire layers a six-frame flame sprite in front of static logs', async ({ page }) => {
  await mountFixture(page)
  const fire = page.locator('.rs-fire')
  const stage = page.locator('.rs-stage')
  const flameViewport = fire.locator('.rs-fire__flame-viewport')
  const flame = fire.locator('.rs-fire__flame')
  const logs = fire.locator('.rs-fire__logs')
  await expect(fire).toBeVisible()
  await expect(flameViewport).toBeVisible()
  await expect(flame).toBeVisible()
  await expect(logs).toBeVisible()
  await expect(flame).toHaveAttribute('src', '/assets/icons/scene/rest_flame.png')
  await expect(logs).toHaveAttribute('src', '/assets/icons/scene/rest_logs.png')
  await expect(page.locator('.popup__title')).toHaveText('焚き火')
  await expect(page.getByText('最大HPの30％を回復する(HP30→59)。')).toBeVisible()
  await expect(page.getByText('HP+5')).toBeVisible()
  const titleIsNotClipped = await page.locator('.popup__title').evaluate(element =>
    element.scrollHeight <= element.clientHeight,
  )
  expect(titleIsNotClipped).toBe(true)

  const fireRect = await fire.boundingBox()
  const stageRect = await stage.boundingBox()
  expect(page.viewportSize()).toEqual({ width: 1280, height: 720 })
  expect(fireRect?.width).toBeCloseTo(192, 2)
  expect(fireRect?.height).toBeCloseTo(192, 2)
  expect(fireRect!.x + fireRect!.width / 2).toBeCloseTo(
    stageRect!.x + stageRect!.width / 2,
    1,
  )

  const flamePng = await inspectPng(flame)
  const logsPng = await inspectPng(logs)
  expect(flamePng.naturalWidth).toBe(288)
  expect(flamePng.naturalHeight).toBe(48)
  expect(flamePng.frameVisible).toHaveLength(6)
  for (const visiblePixels of flamePng.frameVisible) {
    expect(visiblePixels).toBeGreaterThan(0)
  }
  for (const bounds of flamePng.frameBounds) {
    // The source itself consists of six 48px cells (displayed at x4).  Every flame has a
    // transparent gutter and its visible bounds are centred in its own cell,
    // so CSS frame stepping cannot expose or borrow a neighbouring frame.
    expect(bounds.minX).toBeGreaterThan(0)
    expect(bounds.maxX).toBeLessThan(47)
    expect(Math.abs((bounds.minX + bounds.maxX) / 2 - 23.5)).toBeLessThanOrEqual(0.5)
  }
  expect(new Set(flamePng.frameHashes).size).toBe(6)
  expect(logsPng.naturalWidth).toBe(48)
  expect(logsPng.naturalHeight).toBe(48)
  expect(logsPng.frameVisible).toHaveLength(1)
  for (const png of [flamePng, logsPng]) {
    expect(png.visible).toBeGreaterThan(0)
    expect(png.greenLike).toBe(0)
    expect(png.magentaLike).toBe(0)
  }

  const layers = await fire.evaluate(element => {
    const logsElement = element.querySelector<HTMLElement>('.rs-fire__logs')!
    const viewportElement = element.querySelector<HTMLElement>('.rs-fire__flame-viewport')!
    const logsStyle = getComputedStyle(logsElement)
    const viewportStyle = getComputedStyle(viewportElement)
    return {
      logsZ: Number(logsStyle.zIndex),
      flameZ: Number(viewportStyle.zIndex),
      overflow: viewportStyle.overflow,
      fireMarginBottom: getComputedStyle(element).marginBottom,
      logsFirst: element.children[0] === logsElement,
      flameSecond: element.children[1] === viewportElement,
    }
  })
  expect(layers.logsZ).toBe(1)
  expect(layers.flameZ).toBe(2)
  expect(layers.flameZ).toBeGreaterThan(layers.logsZ)
  expect(layers.overflow).toBe('hidden')
  expect(layers.fireMarginBottom).toBe('10px')
  expect(layers.logsFirst).toBe(true)
  expect(layers.flameSecond).toBe(true)

  const viewportRect = await flameViewport.boundingBox()
  const logsRect = await logs.boundingBox()
  expect(viewportRect?.width).toBeCloseTo(192, 2)
  expect(viewportRect?.height).toBeCloseTo(192, 2)
  expect(viewportRect!.y - fireRect!.y).toBeCloseTo(-12, 2)
  expect(logsRect!.y - fireRect!.y).toBeCloseTo(18, 2)
  expect(logsRect!.y - viewportRect!.y).toBeCloseTo(30, 2)
  expect(viewportRect!.x + viewportRect!.width / 2).toBeCloseTo(
    fireRect!.x + fireRect!.width / 2,
    1,
  )
  expect(logsRect!.x + logsRect!.width / 2).toBeCloseTo(
    fireRect!.x + fireRect!.width / 2,
    1,
  )

  const flameVisibleTop = -12 + flamePng.bounds.minY
  const flameVisibleBottom = -12 + flamePng.bounds.maxY
  const logsVisibleTop = 18 + logsPng.bounds.minY
  const logsVisibleBottom = 18 + logsPng.bounds.maxY
  expect(flameVisibleTop).toBeLessThan(logsVisibleTop)
  expect(flameVisibleBottom).toBeLessThan(logsVisibleBottom)
  expect(logsVisibleBottom - flameVisibleBottom).toBeGreaterThanOrEqual(28)

  const flameMotion = await flame.evaluate(async element => {
    const animation = element.getAnimations().find(item => (
      getComputedStyle(element).animationName.includes('rs-flame-sprite')
        && item.effect !== null
    ))
    if (!animation) throw new Error('flame animation was not found')
    await animation.ready
    animation.pause()
    const positions: number[] = []
    const frameCenterOffsets: number[] = []
    for (const time of [60, 180, 300, 420, 540, 660]) {
      animation.currentTime = time
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
      positions.push(new DOMMatrix(getComputedStyle(element).transform).m41)
      const frameIndex = frameCenterOffsets.length
      const sheetRect = element.getBoundingClientRect()
      const viewportRect = element.parentElement!.getBoundingClientRect()
      frameCenterOffsets.push(
        sheetRect.left + frameIndex * 192 + 96
          - (viewportRect.left + viewportRect.width / 2),
      )
    }
    animation.play()
    return {
      name: getComputedStyle(element).animationName,
      duration: getComputedStyle(element).animationDuration,
      timing: getComputedStyle(element).animationTimingFunction,
      positions,
      frameCenterOffsets,
    }
  })
  expect(flameMotion.name).toBe('rs-flame-sprite')
  expect(flameMotion.duration).toBe('0.72s')
  expect(flameMotion.timing).toContain('steps(6')
  for (const [index, position] of flameMotion.positions.entries()) {
    expect(position).toBeCloseTo(index * -192, 0)
  }
  for (const offset of flameMotion.frameCenterOffsets) {
    expect(offset).toBeCloseTo(0, 1)
  }
  expect(await logs.evaluate(element => element.getAnimations().length)).toBe(0)
})

test('rest fire keeps a slower frame animation when reduced motion is requested', async ({ page }) => {
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await mountFixture(page)

  const fire = page.locator('.rs-fire')
  const stage = page.locator('.rs-stage')
  const flame = fire.locator('.rs-fire__flame')
  await expect(flame).toBeVisible()

  const reducedMotion = await flame.evaluate(async element => {
    const animation = element.getAnimations().find(item => item.effect !== null)
    if (!animation) throw new Error('reduced-motion flame animation was not found')
    await animation.ready
    animation.pause()
    const positions: number[] = []
    const frameCenterOffsets: number[] = []
    for (const time of [120, 360]) {
      animation.currentTime = time
      await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
      positions.push(new DOMMatrix(getComputedStyle(element).transform).m41)
      const frameIndex = frameCenterOffsets.length
      const sheetRect = element.getBoundingClientRect()
      const viewportRect = element.parentElement!.getBoundingClientRect()
      frameCenterOffsets.push(
        sheetRect.left + frameIndex * 192 + 96
          - (viewportRect.left + viewportRect.width / 2),
      )
    }
    return {
      name: getComputedStyle(element).animationName,
      duration: getComputedStyle(element).animationDuration,
      positions,
      frameCenterOffsets,
    }
  })
  expect(reducedMotion.name).toBe('rs-flame-sprite')
  expect(reducedMotion.duration).toBe('1.44s')
  expect(reducedMotion.positions).toEqual([0, -192])
  for (const offset of reducedMotion.frameCenterOffsets) {
    expect(offset).toBeCloseTo(0, 1)
  }
  const [reducedFireRect, reducedStageRect] = await Promise.all([
    fire.boundingBox(),
    stage.boundingBox(),
  ])
  expect(reducedFireRect!.x + reducedFireRect!.width / 2).toBeCloseTo(
    reducedStageRect!.x + reducedStageRect!.width / 2,
    1,
  )
  expect(await fire.evaluate(element => getComputedStyle(element, '::before').animationName)).toBe('none')
})
