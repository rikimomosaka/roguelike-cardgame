import { expect, test } from '@playwright/test'

async function mountFixture(page: import('@playwright/test').Page) {
  await page.goto('/')
  await page.evaluate(async () => {
    const load = (path: string) => import(path)
    const fixture = await load('/e2e/energy-orb.fixture.tsx')
    await fixture.mountEnergyOrbFixture()
  })
  await expect(page.locator('html')).toHaveAttribute('data-energy-orb-ready', 'true')
}

test('energy orb keeps one-digit, two-digit, and capped values inside the orb', async ({ page }) => {
  await mountFixture(page)

  const cases = [
    { current: 9, text: '9/+3' },
    { current: 10, text: '10' },
    { current: 100, text: '99+' },
  ]

  for (const energyCase of cases) {
    const root = page.locator(`[data-energy-case="${energyCase.current}"]`)
    const orb = root.locator('.energy')
    await expect(orb).toHaveText(energyCase.text)
    const fits = await root.evaluate(element => {
      const orbElement = element.querySelector<HTMLElement>('.energy')!
      const valueElement = element.querySelector<HTMLElement>('.energy__val')!
      const orbRect = orbElement.getBoundingClientRect()
      const valueRect = valueElement.getBoundingClientRect()
      return valueRect.left >= orbRect.left
        && valueRect.right <= orbRect.right
        && valueRect.top >= orbRect.top
        && valueRect.bottom <= orbRect.bottom
    })
    expect(fits, `energy ${energyCase.current} should remain inside its orb`).toBe(true)
  }

  const oneDigit = page.locator('[data-energy-case="9"] .energy')
  await oneDigit.focus()
  const tooltip = page.getByRole('tooltip')
  await expect(tooltip.locator('.tip__name'))
    .toHaveText('エナジー')
  await expect(tooltip.locator('.tip__status'))
    .toHaveText('(現在9 ターン開始時に3追加)')
  await expect(tooltip.locator('.tip__desc')).toContainText('次のターンへ持ち越され')
  await expect(tooltip.locator('.tip__desc')).not.toContainText('最大値まで回復')
})
