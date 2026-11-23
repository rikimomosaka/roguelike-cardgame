import { expect, test } from '@playwright/test'

test('archive obscures undiscovered art and keeps real keyword lines inside their text frame', async ({ page }) => {
  await page.goto('/')
  await page.getByLabel('アカウント ID').fill(`archive-${Date.now()}`)
  await page.getByRole('button', { name: 'アカウント作成' }).click()
  await page.getByRole('button', { name: '実績' }).click()

  await page.getByRole('tab', { name: /^カード/ }).click()

  // Real catalog regression: keyword-condition lines need one clear pixel
  // between their 1px black outline and the rules-panel edge.
  await page.evaluate(() => document.fonts.ready)
  for (const text of [
    '伏策：相手のアタック時。',
    '再演：コスト2以下のアタック・スキル。',
  ]) {
    const line = page.locator('.achievements__card-grid .card-desc-line')
      .filter({ hasText: text })
      .first()
    await expect(line).toBeVisible()
    const inset = await line.evaluate(wrapper => {
      const inner = wrapper.querySelector<HTMLElement>('.card-desc-line-inner')!
      const desc = wrapper.closest<HTMLElement>('.card__desc')!
      const wrapperRect = wrapper.getBoundingClientRect()
      const innerRect = inner.getBoundingClientRect()
      const descRect = desc.getBoundingClientRect()
      const ancestorScaleX = wrapperRect.width / wrapper.clientWidth
      return {
        left: (innerRect.left - wrapperRect.left) / ancestorScaleX,
        right: (wrapperRect.right - innerRect.right) / ancestorScaleX,
        wrapperLeft: wrapperRect.left,
        wrapperRight: wrapperRect.right,
        descLeft: descRect.left,
        descRight: descRect.right,
        descClientWidth: desc.clientWidth,
        descScrollWidth: desc.scrollWidth,
      }
    })
    expect(inset.left, `${text} left paint inset`).toBeGreaterThanOrEqual(1.99)
    expect(inset.right, `${text} right paint inset`).toBeGreaterThanOrEqual(1.99)
    expect(inset.wrapperLeft, `${text} wrapper clips at description left`)
      .toBeGreaterThanOrEqual(inset.descLeft)
    expect(inset.wrapperRight, `${text} wrapper clips at description right`)
      .toBeLessThanOrEqual(inset.descRight)
    expect(inset.descScrollWidth, `${text} overflows the description viewport`)
      .toBeLessThanOrEqual(inset.descClientWidth)
  }

  const hiddenCardCell = page.locator('.achievements__card-cell.is-undiscovered').first()
  const hiddenCard = hiddenCardCell.locator('.card')
  await expect(hiddenCardCell).toBeVisible()
  await expect(hiddenCard).toHaveAttribute('aria-hidden', 'true')
  await expect(hiddenCard.locator('.card__name')).not.toHaveText('? ? ?')
  const cardVisual = await hiddenCard.evaluate((element) => {
    const style = getComputedStyle(element)
    return { filter: style.filter, pointerEvents: style.pointerEvents }
  })
  expect(cardVisual.filter).toContain('blur(7px)')
  expect(cardVisual.pointerEvents).toBe('none')
  await hiddenCardCell.hover()
  await expect(page.getByRole('tooltip')).toHaveCount(0)

  await page.getByRole('tab', { name: /^レリック/ }).click()
  const relic = page.locator('.achievements__tile-cell.is-locked img').first()
  await expect(relic).toBeVisible()
  await expect(relic).toHaveAttribute('src', /\/icons\/relics\/.+\.png$/)
  await expect(page.getByRole('tooltip')).toHaveCount(0)

  await page.getByRole('tab', { name: /^ポーション/ }).click()
  const potion = page.locator('.achievements__tile-cell.is-locked img').first()
  await expect(potion).toBeVisible()
  await expect(potion).toHaveAttribute('src', /\/icons\/potions\/.+\.png$/)
})
