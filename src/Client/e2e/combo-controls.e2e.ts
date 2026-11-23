import { expect, test } from '@playwright/test'
import type { Locator, Page } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import path from 'node:path'

type FixtureOptions = {
  comboGauge?: number
  tacticalDisabled?: boolean
  ultimateDisabled?: boolean
  withFinisherRecord?: boolean
}

async function mountFixture(page: Page, options: FixtureOptions = {}) {
  await page.goto('/')
  await page.evaluate(async fixtureOptions => {
    const load = (path: string) => import(path)
    const fixture = await load('/e2e/combo-controls.fixture.tsx')
    await fixture.mountComboControlsFixture(fixtureOptions)
  }, options)
  await expect(page.locator('html')).toHaveAttribute('data-combo-controls-ready', 'true')
}

async function box(locator: Locator) {
  const value = await locator.boundingBox()
  if (!value) throw new Error('Expected element geometry')
  return value
}

function overlaps(
  a: { x: number; y: number; width: number; height: number },
  b: { x: number; y: number; width: number; height: number },
) {
  return a.x < b.x + b.width
    && a.x + a.width > b.x
    && a.y < b.y + b.height
    && a.y + a.height > b.y
}

test('combo skills flank the combo, gauge sits below, and controls avoid hand and turn end', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await mountFixture(page)
  const artifactDirectory = path.resolve(process.cwd(), '..', '..', '.artifacts', 'visual-qa')
  await mkdir(artifactDirectory, { recursive: true })

  const controls = page.locator('.battle__combo-controls')
  const tactical = controls.locator('[data-combo-skill="tactical"]')
  const combo = controls.locator('.battle__combo')
  const gauge = controls.locator('.combo-gauge')
  const ultimate = controls.locator('[data-combo-skill="ultimate"]')
  const hand = page.locator('[data-layout-obstacle="hand"]')
  const endTurn = page.locator('[data-layout-obstacle="end-turn"]')

  const [controlsBox, tacticalBox, comboBox, gaugeBox, ultimateBox, handBox, endTurnBox] =
    await Promise.all([controls, tactical, combo, gauge, ultimate, hand, endTurn].map(box))

  expect(tacticalBox.x + tacticalBox.width).toBeLessThanOrEqual(comboBox.x)
  expect(ultimateBox.x).toBeGreaterThanOrEqual(comboBox.x + comboBox.width)
  expect(gaugeBox.y).toBeGreaterThanOrEqual(comboBox.y + comboBox.height)
  expect(gaugeBox.x + gaugeBox.width / 2).toBeCloseTo(comboBox.x + comboBox.width / 2, 3)
  expect(overlaps(controlsBox, handBox)).toBe(false)
  expect(overlaps(controlsBox, endTurnBox)).toBe(false)

  const [currentComboBox, maxComboBox] = await Promise.all([
    box(combo.locator('.battle__combo-current')),
    box(combo.locator('.battle__combo-max')),
  ])
  // MAX is independently right-aligned: it must never shift the large current
  // count away from the physical center of the fixed combo box.
  expect(currentComboBox.x + currentComboBox.width / 2)
    .toBeCloseTo(comboBox.x + comboBox.width / 2, 3)
  expect(maxComboBox.x).toBeGreaterThan(currentComboBox.x + currentComboBox.width)
  expect(maxComboBox.x + maxComboBox.width).toBeLessThan(comboBox.x + comboBox.width)

  const comboLabelStyle = await combo.locator('.battle__combo-k').evaluate(element => {
    const style = getComputedStyle(element)
    return { fontSize: Number.parseFloat(style.fontSize), top: style.top }
  })
  const maxComboStyle = await combo.locator('.battle__combo-max').evaluate(element => {
    const style = getComputedStyle(element)
    return {
      fontSize: Number.parseFloat(style.fontSize),
      letterSpacing: style.letterSpacing,
    }
  })
  expect(comboLabelStyle.fontSize).toBeGreaterThanOrEqual(10)
  expect(comboLabelStyle.top).toBe('8px')
  expect(maxComboStyle.fontSize).toBeGreaterThanOrEqual(10)
  expect(['0px', 'normal']).toContain(maxComboStyle.letterSpacing)

  await expect(gauge).toHaveAttribute('aria-label', 'コンボゲージ 0/10')
  await expect(gauge.locator('.combo-gauge__segment')).toHaveCount(10)
  await expect(gauge.locator('.combo-gauge__segment.is-filled')).toHaveCount(0)

  await expect(tactical.getByRole('button')).toBeDisabled()
  await expect(ultimate.getByRole('button')).toBeDisabled()
  await page.screenshot({
    path: path.join(artifactDirectory, 'combo-controls-gauge-0-disabled.png'),
  })

  await tactical.focus()
  const tacticalTooltip = page.getByRole('tooltip')
  await expect(tacticalTooltip.locator('.tip__name')).toHaveText('戦技')
  await expect(tacticalTooltip.locator('.tip__status')).toHaveText('(コンボゲージ3消費)')
  await expect(tacticalTooltip.locator('.tip__desc')).toContainText(
    'ターン中1度だけ使用可能(0/1)。',
  )

  await gauge.focus()
  const gaugeTooltip = page.getByRole('tooltip')
  await expect(gaugeTooltip.locator('.tip__name')).toHaveText('コンボゲージ')
  await expect(gaugeTooltip.locator('.tip__status')).toHaveText('(現在0/10)')
  await expect(gaugeTooltip.locator('.tip__desc')).toContainText(
    '最大コンボを更新するたび、その増加分だけ回復する。',
  )

  await mountFixture(page, {
    comboGauge: 10,
    tacticalDisabled: false,
    ultimateDisabled: false,
  })
  const fullControls = page.locator('.battle__combo-controls')
  await expect(fullControls.locator('.combo-gauge'))
    .toHaveAttribute('aria-label', 'コンボゲージ 10/10')
  await expect(fullControls.locator('.combo-gauge__segment.is-filled')).toHaveCount(10)
  await expect(fullControls.locator('[data-combo-skill="tactical"] button')).toBeEnabled()
  await expect(fullControls.locator('[data-combo-skill="ultimate"] button')).toBeEnabled()
  await page.screenshot({
    path: path.join(artifactDirectory, 'combo-controls-gauge-10-enabled.png'),
  })
})

test('saved afterimage control appears below the gauge without moving tactical, combo, or ultimate controls', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await mountFixture(page)

  const controls = page.locator('.battle__combo-controls')
  const selectors = [
    '[data-combo-skill="tactical"]',
    '.battle__combo',
    '.combo-gauge',
    '[data-combo-skill="ultimate"]',
  ] as const
  const before = await Promise.all(selectors.map(selector => box(controls.locator(selector))))

  await mountFixture(page, { withFinisherRecord: true })
  const afterControls = page.locator('.battle__combo-controls')
  const after = await Promise.all(selectors.map(selector => box(afterControls.locator(selector))))
  after.forEach((geometry, index) => {
    expect(geometry.x).toBeCloseTo(before[index].x, 3)
    expect(geometry.y).toBeCloseTo(before[index].y, 3)
  })

  const record = afterControls.getByRole('button', { name: '保存された残光を表示（1枚）' })
  const [recordBox, gaugeBox] = await Promise.all([
    box(record),
    box(afterControls.locator('.combo-gauge')),
  ])
  expect(recordBox.y).toBeGreaterThanOrEqual(gaugeBox.y + gaugeBox.height)
})
