import { expect, test } from '@playwright/test'

test('account creation unlocks the first route after choosing an act-start relic', async ({ page }) => {
  await page.goto('/')

  await page.getByLabel('アカウント ID').fill(`e2e-${Date.now()}`)
  await page.getByRole('button', { name: 'アカウント作成' }).click()

  await expect(page.getByRole('button', { name: 'はじめから' })).toBeVisible()
  await page.getByRole('button', { name: 'はじめから' }).click()

  await expect(page.getByRole('heading', { name: 'キャラクター選択' })).toBeVisible()
  await page.locator('.character-pick__select').filter({ hasText: '残光剣士セラ' }).click()
  await page.getByRole('button', { name: 'このキャラクターで開始' }).click()

  const startNode = page.locator('[data-start-entry="true"]')
  await expect(startNode).toHaveCount(1)
  await expect(page.locator('[data-selectable="true"]')).toHaveCount(0)
  // The current-location marker intentionally pulses, so the node never
  // becomes geometrically stable even though it is the intended entry action.
  await startNode.click({ force: true })

  const relicDialog = page.getByRole('dialog', { name: 'レリックを選ぶ' })
  await expect(relicDialog).toBeVisible()
  const relicChoice = relicDialog.locator('button[aria-pressed]').first()
  await relicChoice.click()
  await expect(relicChoice).toHaveAttribute('aria-pressed', 'true')
  await relicDialog.getByRole('button', { name: '決定' }).click()

  await expect(relicDialog).toBeHidden()
  const currentNode = page.locator('[aria-current="location"][data-visited="true"]')
  await expect(currentNode).toHaveCount(1)
  await expect(currentNode.locator('.map-screen__current-marker')).toHaveText('現在地')
  await expect.poll(async () => page.locator('[data-selectable="true"]').count()).toBeGreaterThan(0)

  const nextNode = page.locator('[data-selectable="true"]').first()
  const [currentVisual, nextVisual] = await Promise.all([
    currentNode.evaluate((element) => {
      const nodeRect = element.getBoundingClientRect()
      const marker = element.querySelector<HTMLElement>('.map-screen__current-marker')
      if (!marker) throw new Error('current-location marker was not rendered')
      const markerRect = marker.getBoundingClientRect()
      const computed = getComputedStyle(element)
      return {
        animationName: computed.animationName,
        borderColor: computed.borderColor,
        markerAboveNode: markerRect.bottom < nodeRect.top,
      }
    }),
    nextNode.evaluate((element) => {
      const computed = getComputedStyle(element)
      return {
        animationName: computed.animationName,
        borderColor: computed.borderColor,
        hasCurrentMarker: element.querySelector('.map-screen__current-marker') !== null,
      }
    }),
  ])

  expect(currentVisual.animationName).toContain('map-current-arrive')
  expect(currentVisual.markerAboveNode).toBe(true)
  expect(nextVisual.animationName).toContain('map-next-pulse')
  expect(nextVisual.hasCurrentMarker).toBe(false)
  expect(currentVisual.borderColor).not.toBe(nextVisual.borderColor)
  await expect(page.getByLabel(/レリック \(1個\)/)).toBeVisible()
})
