import { expect, test } from '@playwright/test'

test('each attack scope and following action occupy a separate visual row', async ({ page }) => {
  await page.goto('/')
  await page.evaluate(async () => {
    const fixture = await import('/e2e/intent-layout.fixture.tsx')
    await fixture.mountIntentLayoutFixture()
  })
  await page.waitForFunction(() => document.documentElement.dataset.intentLayoutReady === 'true')

  const scopeRows = page.locator('.intent__scope-row')
  await expect(scopeRows).toHaveCount(3)
  await expect(scopeRows.locator('.intent__scope-label')).toHaveText(['SINGLE', 'RANDOM', 'ALL'])

  const rowBoxes = await scopeRows.evaluateAll(rows => rows.map(row => {
    const rect = row.getBoundingClientRect()
    return { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
  }))
  for (let index = 1; index < rowBoxes.length; index += 1) {
    expect(rowBoxes[index].y).toBeGreaterThanOrEqual(
      rowBoxes[index - 1].y + rowBoxes[index - 1].height,
    )
  }

  const defendBox = await page.locator('.intent__seg--defend').evaluate(element => {
    const rect = element.getBoundingClientRect()
    return { y: rect.y, height: rect.height }
  })
  const lastAttackRow = rowBoxes.at(-1)!
  expect(defendBox.y).toBeGreaterThanOrEqual(lastAttackRow.y + lastAttackRow.height)

  // The new layout's widest single action remains compact instead of adding
  // all three attack summaries into one horizontal strip.
  expect(Math.max(...rowBoxes.map(row => row.width))).toBeLessThan(120)
})
