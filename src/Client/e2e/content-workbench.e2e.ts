import { expect, test } from '@playwright/test'

test('all game data layers open in the full-viewport content workbench', async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 })
  await page.goto('/?dev=cards')

  await expect(page.getByRole('heading', { name: 'ゲームデータ管理' })).toBeVisible()
  const stage = page.locator('.app-stage--workbench')
  await expect(stage).toHaveCSS('transform', 'none')
  const stageBox = await stage.boundingBox()
  expect(stageBox?.width).toBe(1920)
  expect(stageBox?.height).toBe(1080)
  const workbenchBox = await page.locator('.content-workbench').boundingBox()
  expect(workbenchBox?.x).toBe(0)
  expect(workbenchBox?.width).toBe(1920)
  const navOverflows = await page.locator('.content-workbench__kinds').evaluate(element =>
    element.scrollWidth > element.clientWidth,
  )
  expect(navOverflows).toBe(false)

  await expect(page.locator('[data-content-id="basic_attack"]')).toBeVisible()
  await page.locator('[data-content-id="basic_attack"]').click()
  await expect(page.locator('.content-json-field input').first()).toBeVisible()
  await expect(page.locator('.content-preview-card .card').first()).toBeVisible()
  await expect(page.locator('.content-preview-rich-text')).not.toContainText('[N:')
  await expect(page.locator('.content-preview-rich-text')).not.toContainText('[S:')
  await expect(page.getByLabel('カード種別')).toHaveText(/Attack\(攻撃\)/)
  await expect(page.locator('select option').filter({ hasText: 'block(ブロック)' }).first()).toBeAttached()
  await expect(page.getByRole('button', { name: '＋ 通常版キーワード欄を追加' })).toBeVisible()
  await expect(page.getByRole('button', { name: '＋ 強化後キーワード欄を追加' })).toBeVisible()
  await expect(page.locator('option').filter({ hasText: 'upgradedCost(強化後コスト)' }).first()).toBeAttached()
  await expect(page.locator('option').filter({ hasText: 'usesPerTurn(本体の1ターン使用回数)' }).first()).toBeAttached()
  await expect(page.locator('option').filter({ hasText: 'upgradedUsesPerTurn(強化版本体の1ターン使用回数)' }).first()).toBeAttached()

  await page.getByRole('button', { name: 'キャラクター', exact: true }).click()
  await page.locator('[data-content-id="default"]').click()
  const statusTerm = page.locator('.content-preview-generic__effect [data-term="afterimage"]').first()
  await expect(statusTerm).toBeVisible()
  await statusTerm.hover()
  await expect(page.getByRole('tooltip')).toContainText('残光')
  await expect(page.getByRole('tooltip')).toContainText('終撃によって保存された全ての効果')

  await page.getByRole('button', { name: 'レリック', exact: true }).click()
  await page.locator('[data-content-id="nyango_bell"]').click()
  const relicPreview = page.locator('.content-preview-generic')
  const relicIconBox = await relicPreview.locator(':scope > img').boundingBox()
  const onHitIconBox = await relicPreview.locator('.card-desc-trigger__icon').boundingBox()
  expect(relicIconBox?.width).toBe(82)
  expect(onHitIconBox?.width).toBeLessThan(24)

  await page.getByRole('button', { name: 'カード', exact: true }).click()
  await page.locator('[data-content-id="guardian_common_shield_bash"]').click()
  await expect(page.getByRole('button', { name: /^v1（使用中）\s*approved-v2/ })).toHaveAttribute('aria-expanded', 'true')

  const kinds = [
    'ポーション', 'レリック', '敵キャラクター', '召喚キャラクター',
    '敵編成', 'イベント', '各層の報酬プール', '各層の開始レリック', '商店価格', 'マップ生成',
  ]
  for (const label of kinds) {
    await page.getByRole('button', { name: label, exact: true }).click()
    await expect(page.locator('.content-browser__list > button').first()).toBeVisible()
    await expect(page.locator('.content-editor__identity code')).not.toHaveText('')
  }

  await page.getByRole('button', { name: '状態変化', exact: true }).click()
  await expect(page.locator('[data-content-id="strength"]')).toBeVisible()
  await expect(page.getByText('現在はCoreの戦闘ルールと密結合')).toBeVisible()

  await page.getByRole('button', { name: '効果・自動翻訳', exact: true }).click()
  await expect(page.locator('[data-content-id="attack"]')).toBeVisible()
  await expect(page.getByText('新しいactionの追加はCore実装を伴う')).toBeVisible()

  for (const size of [{ width: 1280, height: 800 }, { width: 820, height: 760 }]) {
    await page.setViewportSize(size)
    const responsiveBox = await page.locator('.content-workbench').boundingBox()
    expect(responsiveBox?.width).toBe(size.width)
    const workbenchOverflows = await page.locator('.content-workbench').evaluate(element =>
      element.scrollWidth > element.clientWidth,
    )
    expect(workbenchOverflows).toBe(false)
  }
})
