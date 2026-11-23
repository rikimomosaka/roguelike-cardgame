import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

async function mountFixture(page: Page) {
  await page.goto('/')
  await page.evaluate(async () => {
    const load = (path: string) => import(path)
    const fixture = await load('/e2e/merchant.fixture.tsx')
    await fixture.mountMerchantFixture()
  })
  await expect(page.locator('html')).toHaveAttribute('data-merchant-fixture-ready', 'true')
}

test('shopkeeper conversation stays above scrolled product cards', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await mountFixture(page)
  await expect(page.locator('.popup:has(.mc-shop-panel)')).toHaveAttribute('data-state', 'open')

  const layering = await page.locator('.mc-shop-panel .popup__body').evaluate(bodyElement => {
    const body = bodyElement as HTMLElement
    const shopkeeper = body.querySelector<HTMLElement>('.mc-shopkeeper')!
    const product = body.querySelector<HTMLElement>('.mc-card-slot')!
    let overlap: DOMRect | null = null

    for (let scrollTop = 0; scrollTop <= body.scrollHeight - body.clientHeight; scrollTop += 8) {
      body.scrollTop = scrollTop
      const headerRect = shopkeeper.getBoundingClientRect()
      const productRect = product.getBoundingClientRect()
      if (
        productRect.bottom > headerRect.top
        && productRect.top < headerRect.bottom
        && productRect.right > headerRect.left
        && productRect.left < headerRect.right
      ) {
        overlap = productRect
        break
      }
    }

    if (!overlap) throw new Error('Fixture did not create a sticky-header overlap')
    const headerRect = shopkeeper.getBoundingClientRect()
    const x = (
      Math.max(headerRect.left, overlap.left)
      + Math.min(headerRect.right, overlap.right)
    ) / 2
    const y = (
      Math.max(headerRect.top, overlap.top)
      + Math.min(headerRect.bottom, overlap.bottom)
    ) / 2
    const frontmost = document.elementFromPoint(x, y)
    return {
      headerZIndex: getComputedStyle(shopkeeper).zIndex,
      bodyIsolation: getComputedStyle(body).isolation,
      frontmostIsShopkeeper: frontmost?.closest('.mc-shopkeeper') === shopkeeper,
    }
  })

  expect(layering.headerZIndex).toBe('50')
  expect(layering.bodyIsolation).toBe('isolate')
  expect(layering.frontmostIsShopkeeper).toBe(true)
})

test('card selection uses the shopkeeper band for confirmation and thanks', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await mountFixture(page)

  const dialogue = page.getByRole('status')
  await expect(dialogue).toHaveText('いらっしゃいませ。')
  await page.getByRole('button', { name: 'Select offer_card_01' }).click()
  await expect(dialogue).toHaveText('こちらをお買い上げになりますか？')
  await expect(page.getByRole('group', { name: 'offer_card_01の購入確認' })).toBeVisible()
  await expect(page.getByLabel('価格 40ゴールド')).toBeVisible()
  await expect(page.getByRole('complementary')).toHaveCount(0)

  await page.getByRole('button', { name: 'Buy offer_card_01' }).click()
  await expect(dialogue).toHaveText('ありがとうございます。')
  await expect(page.getByText('取引が完了しました。')).toHaveCount(0)
  await expect(page.getByText('お買い上げありがとうございます。')).toHaveCount(0)
  expect(await page.evaluate(() => window.__merchantBuyCalls)).toEqual([
    'card:offer_card_01',
  ])
})
