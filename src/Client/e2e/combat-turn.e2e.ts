import { expect, test } from '@playwright/test'
import type { Locator, Page, Response } from '@playwright/test'

type CombatSource = {
  kind: string
  ownerSlot: number | null
  definitionId: string | null
}

type DamageCauseFrame = {
  depth: number
  damageType: string
  source: CombatSource
  hitIndex: number | null
  cause: string
}

type BattleEvent = {
  kind: string
  casterInstanceId: string | null
  targetInstanceId: string | null
  hitIndex: number | null
  damageType?: string | null
  source?: CombatSource | null
  causeChain?: DamageCauseFrame[] | null
  actualHpLoss?: number | null
  hitLanded?: boolean | null
}

type CombatActor = {
  instanceId: string
  currentHp: number
}

type BattleState = {
  allies: CombatActor[]
  enemies: CombatActor[]
  comboCount: number
  maxComboThisTurn: number
}

type BattleActionResponse = {
  state: BattleState
  steps: Array<{ event: BattleEvent; snapshotAfter: BattleState }>
}

test.setTimeout(60_000)

function isBattleAction(response: Response, action: 'start' | 'play-card' | 'end-turn') {
  const url = new URL(response.url())
  return response.request().method() === 'POST'
    && url.pathname === `/api/v1/runs/current/battle/${action}`
}

async function waitForPlayerInput(page: Page) {
  await expect(page.getByRole('button', { name: 'TURN END' })).toBeEnabled({ timeout: 20_000 })
}

async function createRunAndEnterFirstBattle(page: Page): Promise<BattleActionResponse> {
  await page.goto('/')

  await page.getByLabel('アカウント ID').fill(`combat-e2e-${Date.now()}`)
  await page.getByRole('button', { name: 'アカウント作成' }).click()
  await page.getByRole('button', { name: 'はじめから' }).click()

  await page.locator('[data-start-entry="true"]').click()
  const relicDialog = page.getByRole('dialog', { name: 'レリックを選ぶ' })
  await expect(relicDialog).toBeVisible()
  await relicDialog.locator('button[aria-pressed]').first().click()
  await relicDialog.getByRole('button', { name: '決定' }).click()

  // Row 1 is guaranteed to be Enemy by map generation. This stays on the
  // production UI/API path and avoids any Development-only debug endpoint.
  const firstFight = page.locator('[data-selectable="true"]').first()
  await expect(firstFight).toBeVisible()
  const startResponsePromise = page.waitForResponse(response => isBattleAction(response, 'start'))
  // Selectable map nodes intentionally float/pulse forever. Playwright's
  // stability check therefore never settles even though the button is enabled.
  await firstFight.click({ force: true })
  const startResponse = await startResponsePromise
  expect(startResponse.ok()).toBeTruthy()

  await expect(page.locator('.battle-screen')).toBeVisible()
  await waitForPlayerInput(page)
  return await startResponse.json() as BattleActionResponse
}

async function endTurn(page: Page): Promise<BattleActionResponse> {
  const responsePromise = page.waitForResponse(response => isBattleAction(response, 'end-turn'))
  await page.getByRole('button', { name: 'TURN END' }).click()
  const response = await responsePromise
  expect(response.ok()).toBeTruthy()
  return await response.json() as BattleActionResponse
}

async function playCard(page: Page, card: ReturnType<Page['locator']>) {
  const responsePromise = page.waitForResponse(response => isBattleAction(response, 'play-card'))
  await card.click()
  const response = await responsePromise
  expect(response.ok()).toBeTruthy()
  await waitForPlayerInput(page)
}

function playableAttacks(page: Page) {
  return page.locator(
    '.hand > .card.card--type-attack.is-playable:not(.is-playing-hidden):not(.is-leaving)',
  )
}

async function hasPlayableTwoHitSequence(page: Page) {
  const attacks = playableAttacks(page)
  const names = await attacks.locator('.card__name').allInnerTexts()
  // Starting with Strike (cost 1) guarantees that either another Strike or
  // Heavy Blow (cost 2, combo-reduced to 1) remains payable this turn.
  return names.length >= 2 && names.includes('ストライク')
}

async function observeHeroAttackSlides(page: Page) {
  await page.evaluate(() => {
    const observedWindow = window as Window & {
      __rcgHeroAttackSlides?: number
      __rcgHeroAttackMotions?: number
      __rcgHeroHurtMotions?: number
      __rcgHeroAttackSources?: string[]
      __rcgHeroHurtSources?: string[]
      __rcgHeroAttackTimings?: string[]
      __rcgHeroAttackDurations?: string[]
      __rcgHeroHurtDurations?: string[]
      __rcgHeroAttackFrameChanges?: number
      __rcgHeroHurtFrameChanges?: number
      __rcgHeroAttackObserver?: MutationObserver
      __rcgHeroMotionObserver?: MutationObserver
    }
    observedWindow.__rcgHeroAttackSlides = 0
    observedWindow.__rcgHeroAttackMotions = 0
    observedWindow.__rcgHeroHurtMotions = 0
    observedWindow.__rcgHeroAttackSources = []
    observedWindow.__rcgHeroHurtSources = []
    observedWindow.__rcgHeroAttackTimings = []
    observedWindow.__rcgHeroAttackDurations = []
    observedWindow.__rcgHeroHurtDurations = []
    observedWindow.__rcgHeroAttackFrameChanges = 0
    observedWindow.__rcgHeroHurtFrameChanges = 0

    const hero = document.querySelector(
      '.battle__side--player .battle__slot[data-occupied="1"]',
    )
    if (!hero) throw new Error('hero battle slot was not found')
    const sprite = hero.querySelector('.sprite[data-motion]')
    if (!sprite) throw new Error('hero animated sprite was not found')

    let wasAttacking = hero.classList.contains('is-attacking-enemy')
    observedWindow.__rcgHeroAttackObserver?.disconnect()
    observedWindow.__rcgHeroAttackObserver = new MutationObserver(() => {
      const isAttacking = hero.classList.contains('is-attacking-enemy')
      if (isAttacking && !wasAttacking) observedWindow.__rcgHeroAttackSlides!++
      wasAttacking = isAttacking
    })
    observedWindow.__rcgHeroAttackObserver.observe(hero, {
      attributes: true,
      attributeFilter: ['class'],
    })

    let previousMotion = sprite.getAttribute('data-motion')
    observedWindow.__rcgHeroMotionObserver?.disconnect()
    observedWindow.__rcgHeroMotionObserver = new MutationObserver(() => {
      const motion = sprite.getAttribute('data-motion')
      if (motion !== previousMotion) {
        const source = sprite.querySelector('.sprite-sheet__image')?.getAttribute('src') ?? ''
        if (motion === 'attack') {
          observedWindow.__rcgHeroAttackMotions!++
          observedWindow.__rcgHeroAttackSources!.push(source)
          const image = sprite.querySelector('.sprite-sheet__image')
          observedWindow.__rcgHeroAttackTimings!.push(
            image ? getComputedStyle(image).animationTimingFunction : '',
          )
          observedWindow.__rcgHeroAttackDurations!.push(
            image ? getComputedStyle(image).animationDuration : '',
          )
          if (image) {
            const firstTransform = getComputedStyle(image).transform
            window.setTimeout(() => {
              if (image.isConnected && getComputedStyle(image).transform !== firstTransform) {
                observedWindow.__rcgHeroAttackFrameChanges!++
              }
            }, 120)
          }
        }
        if (motion === 'hurt') {
          observedWindow.__rcgHeroHurtMotions!++
          observedWindow.__rcgHeroHurtSources!.push(source)
          const image = sprite.querySelector('.sprite-sheet__image')
          observedWindow.__rcgHeroHurtDurations!.push(
            image ? getComputedStyle(image).animationDuration : '',
          )
          if (image) {
            const firstTransform = getComputedStyle(image).transform
            window.setTimeout(() => {
              if (image.isConnected && getComputedStyle(image).transform !== firstTransform) {
                observedWindow.__rcgHeroHurtFrameChanges!++
              }
            }, 150)
          }
        }
        previousMotion = motion
      }
    })
    observedWindow.__rcgHeroMotionObserver.observe(sprite, {
      attributes: true,
      attributeFilter: ['data-motion'],
    })
  })
}

async function assertHeroIdleAnimation(page: Page) {
  const sheet = page.locator(
    '.battle__side--player .sprite[data-motion="idle"] .sprite-sheet__image',
  )
  await expect(sheet).toBeVisible()

  const result = await sheet.evaluate(async element => {
    const image = element as HTMLImageElement
    await image.decode()
    const animation = image.getAnimations()[0]
    if (!animation) throw new Error('hero idle CSS animation was not found')

    await animation.ready
    const naturalFirstTransform = getComputedStyle(image).transform
    await new Promise(resolve => setTimeout(resolve, 180))
    const naturalSecondTransform = getComputedStyle(image).transform
    const naturalPlayState = animation.playState
    animation.pause()
    animation.currentTime = 0
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    const firstFrameTransform = getComputedStyle(image).transform
    // 1200ms / 8 = 150ms per frame. Sample safely inside frame 2.
    animation.currentTime = 180
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    const secondFrameTransform = getComputedStyle(image).transform
    const computed = getComputedStyle(image)
    const frame = image.closest('.sprite-sheet__frame')
    if (!frame) throw new Error('hero idle sprite frame was not found')
    const frameComputed = getComputedStyle(frame)
    animation.play()

    return {
      naturalWidth: image.naturalWidth,
      naturalHeight: image.naturalHeight,
      animationName: computed.animationName,
      animationDuration: computed.animationDuration,
      animationIterationCount: computed.animationIterationCount,
      frameAnimationName: frameComputed.animationName,
      naturalFirstTransform,
      naturalSecondTransform,
      naturalPlayState,
      firstFrameTransform,
      secondFrameTransform,
    }
  })

  expect(result).toMatchObject({
    naturalWidth: 1024,
    naturalHeight: 256,
    animationName: 'character-sprite-sheet',
    animationDuration: '1.2s',
    animationIterationCount: 'infinite',
    frameAnimationName: 'character-idle-bob',
  })
  expect(result.naturalPlayState).toBe('running')
  expect(result.naturalSecondTransform).not.toBe(result.naturalFirstTransform)
  expect(result.secondFrameTransform).not.toBe(result.firstFrameTransform)

  await page.emulateMedia({ reducedMotion: 'reduce' })
  const reducedMotion = await sheet.evaluate(async element => {
    const image = element as HTMLImageElement
    const frame = image.closest('.sprite-sheet__frame')
    if (!frame) throw new Error('hero idle sprite frame was not found')
    const animation = image.getAnimations()[0]
    if (!animation) throw new Error('reduced-motion idle animation was not found')
    await animation.ready
    animation.pause()
    animation.currentTime = 0
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    const firstTransform = getComputedStyle(image).transform
    animation.currentTime = 350
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    const secondTransform = getComputedStyle(image).transform
    animation.play()
    return {
      sheetAnimationName: getComputedStyle(image).animationName,
      sheetAnimationDuration: getComputedStyle(image).animationDuration,
      frameAnimationName: getComputedStyle(frame).animationName,
      firstTransform,
      secondTransform,
    }
  })
  expect(reducedMotion).toMatchObject({
    sheetAnimationName: 'character-sprite-sheet',
    sheetAnimationDuration: '2.4s',
    frameAnimationName: 'none',
  })
  expect(reducedMotion.secondTransform).not.toBe(reducedMotion.firstTransform)
  await page.emulateMedia({ reducedMotion: 'no-preference' })
}

test('card geometry and cost centering stay identical while the completed card scales', async ({ page }) => {
  await createRunAndEnterFirstBattle(page)

  const measure = (card: Locator) => card.evaluate(async element => {
    await document.fonts.ready
    const cost = element.querySelector<HTMLElement>('.card__cost')
    const digit = element.querySelector<HTMLElement>('.card__cost-n')
    const name = element.querySelector<HTMLElement>('.card__name')
    const type = element.querySelector<HTMLElement>('.card__type')
    if (!cost || !digit || !name || !type) {
      throw new Error('canonical card text elements were not found')
    }

    const rect = (target: Element) => {
      const value = target.getBoundingClientRect()
      return {
        width: value.width,
        height: value.height,
        centerX: value.left + value.width / 2,
        centerY: value.top + value.height / 2,
      }
    }
    const computed = getComputedStyle(element)
    const costComputed = getComputedStyle(cost)
    const digitComputed = getComputedStyle(digit)
    const parts = ['.card__cost', '.card__name', '.card__illust', '.card__type']
      .map(selector => {
        const part = element.querySelector(selector)
        if (!part) throw new Error(`card part was not found: ${selector}`)
        return { selector, rect: rect(part) }
      })

    return {
      card: rect(element),
      cost: rect(cost),
      digit: rect(digit),
      parts,
      computedWidth: computed.width,
      computedHeight: computed.height,
      costFontFamily: costComputed.fontFamily,
      costFontSize: costComputed.fontSize,
      nameFontFamily: getComputedStyle(name).fontFamily,
      typeFontFamily: getComputedStyle(type).fontFamily,
      digitTransform: digitComputed.transform,
      fontsLoaded: {
        cost: document.fonts.check('16px Silkscreen'),
        name: document.fonts.check('14px DotGothic16'),
        type: document.fonts.check('8px "Pixelify Sans"'),
      },
    }
  })

  // The pile modal has no fan rotation or emphasis transform, so it is the
  // canonical place to assert the exact composed-card dimensions.
  await page.getByRole('button', { name: /山札 \(\d+枚\) を表示/ }).click()
  const canonicalCard = page.locator('.pile-modal .card').first()
  await expect(canonicalCard).toBeVisible()
  const canonical = await measure(canonicalCard)
  expect(canonical.computedWidth).toBe('112px')
  expect(Number.parseFloat(canonical.computedHeight)).toBeCloseTo(156.8, 1)
  expect(canonical.card.width).toBeCloseTo(112, 2)
  expect(canonical.card.height).toBeCloseTo(156.8, 1)
  expect(canonical.cost.width).toBeCloseTo(22, 2)
  expect(canonical.cost.height).toBeCloseTo(22, 2)
  expect(canonical.cost.centerX - canonical.digit.centerX).toBeCloseTo(0, 4)
  expect(canonical.cost.centerY - canonical.digit.centerY).toBeCloseTo(1, 4)
  expect(canonical.costFontFamily).toContain('Silkscreen')
  expect(canonical.nameFontFamily).toContain('DotGothic16')
  expect(canonical.typeFontFamily).toContain('Pixelify Sans')
  expect(canonical.costFontSize).toBe('16px')
  expect(canonical.digitTransform).toBe('matrix(1, 0, 0, 1, 0, -1)')
  expect(canonical.fontsLoaded).toEqual({ cost: true, name: true, type: true })
  await page.getByRole('button', { name: '閉じる' }).click()

  // Hand cards are deliberately fan-rotated. Their axis-aligned bounding box
  // is therefore not 112x156.8, but every composed part must scale by exactly
  // the same factor when hovered while its computed geometry stays canonical.
  const cards = page.locator(
    '.hand > .card:not(.is-playing-hidden):not(.is-leaving)',
  )
  const card = cards.nth(Math.floor(await cards.count() / 2))
  await expect(card).toBeVisible()
  const normal = await measure(card)
  expect(normal.computedWidth).toBe(canonical.computedWidth)
  expect(normal.computedHeight).toBe(canonical.computedHeight)
  const normalCostOffset = Math.hypot(
    normal.cost.centerX - normal.digit.centerX,
    normal.cost.centerY - normal.digit.centerY,
  )
  expect(normalCostOffset).toBeCloseTo(1, 3)

  await card.hover()
  await page.waitForTimeout(250)
  const hovered = await measure(card)
  expect(hovered.computedWidth).toBe(normal.computedWidth)
  expect(hovered.computedHeight).toBe(normal.computedHeight)
  expect(hovered.costFontFamily).toBe(normal.costFontFamily)
  expect(hovered.costFontSize).toBe(normal.costFontSize)

  const rootScale = hovered.card.width / normal.card.width
  expect(rootScale).toBeCloseTo(1.06, 2)
  const hoveredCostOffset = Math.hypot(
    hovered.cost.centerX - hovered.digit.centerX,
    hovered.cost.centerY - hovered.digit.centerY,
  )
  expect(hoveredCostOffset / normalCostOffset).toBeCloseTo(rootScale, 2)
  for (const normalPart of normal.parts) {
    const hoveredPart = hovered.parts.find(part => part.selector === normalPart.selector)
    expect(hoveredPart, normalPart.selector).toBeDefined()
    expect(hoveredPart!.rect.width / normalPart.rect.width).toBeCloseTo(rootScale, 2)
    expect(hoveredPart!.rect.height / normalPart.rect.height).toBeCloseTo(rootScale, 2)
  }
})

test('two queued card attacks display and resolve as two real browser hits', async ({ page }) => {
  let beforeResolution = (await createRunAndEnterFirstBattle(page)).state
  await assertHeroIdleAnimation(page)

  // Reproduce Windows' "Animation effects: Off" setting. One-shot combat
  // motions must still finish inside their active 240/300ms UI windows.
  await page.emulateMedia({ reducedMotion: 'reduce' })

  // The fixed 10-card starter deck contains two Strikes and two Heavy Blows.
  // With a five-or-more-card opening hand, a payable pair is guaranteed in
  // either turn 1 or turn 2. We inspect rendered cards instead of RNG details.
  if (!await hasPlayableTwoHitSequence(page)) {
    await endTurn(page)
    await waitForPlayerInput(page)
  }

  expect(await hasPlayableTwoHitSequence(page)).toBeTruthy()

  const strike = playableAttacks(page).filter({ hasText: 'ストライク' }).first()
  await playCard(page, strike)
  await playCard(page, playableAttacks(page).first())

  const heroIntent = page.locator('.battle__side--player .intent__seg--attack')
  await expect(heroIntent.locator('.intent__scope-label')).toHaveText('SINGLE')
  await expect(heroIntent.locator('.intent__hit-count')).toHaveText('(2HIT)')
  const displayedTotal = Number(await heroIntent.locator('.intent__num').innerText())
  expect(displayedTotal).toBeGreaterThanOrEqual(12)
  await expect(heroIntent.locator('.intent__scope-summary')).toHaveAttribute(
    'data-attack-summary',
    `${displayedTotal}(2HIT)`,
  )

  const intentGeometry = await heroIntent.evaluate(root => {
    const damage = root.querySelector<HTMLElement>('.intent__num')!.getBoundingClientRect()
    const meta = root.querySelector<HTMLElement>('.intent__scope-meta')!.getBoundingClientRect()
    const label = root.querySelector<HTMLElement>('.intent__scope-label')!
    return {
      damageHeight: damage.height,
      metaHeight: meta.height,
      labelSize: Number.parseFloat(getComputedStyle(label).fontSize),
    }
  })
  expect(intentGeometry.metaHeight).toBeCloseTo(intentGeometry.damageHeight, 1)
  expect(intentGeometry.labelSize).toBeLessThan(9)

  // Play-card responses update the battle session, so fetch the exact HP
  // baseline immediately before the queued attacks resolve.
  const accountId = await page.evaluate(() => localStorage.getItem('rcg.accountId'))
  expect(accountId).not.toBeNull()
  const stateResponse = await page.request.get('/api/v1/runs/current/battle', {
    headers: { 'X-Account-Id': accountId! },
  })
  expect(stateResponse.ok()).toBeTruthy()
  beforeResolution = await stateResponse.json() as BattleState

  // The compact chip keeps the turn maximum, while its tooltip continues to
  // explain the current chain. Verify the API, visible value, line order and
  // non-white accent in a real browser rather than relying on CSS text alone.
  const comboChip = page.locator('.battle__combo')
  await expect(comboChip).toHaveText(`COMBO×${beforeResolution.maxComboThisTurn}`)
  await expect(comboChip).toHaveAttribute(
    'aria-label',
    `このターンの最大コンボ ×${beforeResolution.maxComboThisTurn}`,
  )
  await comboChip.hover()
  const comboTooltip = page.getByRole('tooltip')
  const maxComboLine = comboTooltip.locator('.tip__status')
  const comboDescription = comboTooltip.locator('.tip__desc')
  await expect(maxComboLine).toHaveText(
    `(このターンの最大コンボ:${beforeResolution.maxComboThisTurn})`,
  )
  await expect(comboDescription).toContainText(`現在${beforeResolution.comboCount}コンボ。`)
  const tooltipOrderAndColor = await comboTooltip.evaluate(element => {
    const status = element.querySelector<HTMLElement>('.tip__status')!
    const description = element.querySelector<HTMLElement>('.tip__desc')!
    return {
      statusBeforeDescription:
        (status.compareDocumentPosition(description) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0,
      statusColor: getComputedStyle(status).color,
    }
  })
  expect(tooltipOrderAndColor.statusBeforeDescription).toBe(true)
  expect(tooltipOrderAndColor.statusColor).toBe('rgb(255, 210, 122)')

  await observeHeroAttackSlides(page)
  const resolved = await endTurn(page)
  const heroId = beforeResolution.allies[0].instanceId
  const enemyIds = new Set(beforeResolution.enemies.map(enemy => enemy.instanceId))
  const cardDamage = resolved.steps
    .map(step => step.event)
    .filter(event => event.kind === 'DealDamage'
      && event.casterInstanceId === heroId
      && event.targetInstanceId !== null
      && enemyIds.has(event.targetInstanceId)
      && event.source?.kind === 'CardEffect')

  expect(cardDamage).toHaveLength(2)
  expect(new Set(cardDamage.map(event => event.hitIndex)).size).toBe(2)
  for (const event of cardDamage) {
    expect(event.damageType).toBe('Attack')
    expect(event.hitLanded).toBe(true)
    expect(event.actualHpLoss).toBeGreaterThan(0)
    expect(event.source?.ownerSlot).toBe(0)
    expect(['strike', 'starter_strike_2']).toContain(event.source?.definitionId)
    expect(event.causeChain).toHaveLength(1)
    expect(event.causeChain?.[0]).toMatchObject({
      depth: 0,
      damageType: 'Attack',
      hitIndex: event.hitIndex,
      cause: 'attack',
    })
  }

  const hpBefore = beforeResolution.enemies.reduce((sum, enemy) => sum + enemy.currentHp, 0)
  const hpAfter = resolved.state.enemies.reduce((sum, enemy) => sum + enemy.currentHp, 0)
  const actualHpLoss = cardDamage.reduce((sum, event) => sum + (event.actualHpLoss ?? 0), 0)
  expect(hpBefore - hpAfter).toBe(actualHpLoss)

  // The UI must animate once per HitIndex, not once for the accumulated total.
  await expect.poll(
    () => page.evaluate(() => (window as Window & { __rcgHeroAttackSlides?: number })
      .__rcgHeroAttackSlides ?? 0),
    { timeout: 20_000 },
  ).toBe(2)
  expect(await page.evaluate(() => (window as Window & { __rcgHeroAttackSources?: string[] })
    .__rcgHeroAttackSources ?? [])).toEqual([
    '/characters/player_attack.png?v=1',
    '/characters/player_attack.png?v=1',
  ])
  expect(await page.evaluate(() => (window as Window & { __rcgHeroAttackTimings?: string[] })
    .__rcgHeroAttackTimings ?? [])).toEqual([
    'steps(4, jump-none)',
    'steps(4, jump-none)',
  ])
  expect(await page.evaluate(() => (window as Window & { __rcgHeroAttackDurations?: string[] })
    .__rcgHeroAttackDurations ?? [])).toEqual(['0.24s', '0.24s'])
  await expect.poll(
    () => page.evaluate(() => (window as Window & { __rcgHeroAttackMotions?: number })
      .__rcgHeroAttackMotions ?? 0),
    { timeout: 20_000 },
  ).toBe(2)
  await expect.poll(
    () => page.evaluate(() => (window as Window & { __rcgHeroAttackFrameChanges?: number })
      .__rcgHeroAttackFrameChanges ?? 0),
    { timeout: 20_000 },
  ).toBe(2)

  const heroTookAttackDamage = resolved.steps.some(step => step.event.kind === 'DealDamage'
    && step.event.targetInstanceId === heroId
    && step.event.damageType === 'Attack'
    && (step.event.actualHpLoss ?? 0) > 0)
  if (heroTookAttackDamage) {
    await expect.poll(
      () => page.evaluate(() => (window as Window & { __rcgHeroHurtMotions?: number })
        .__rcgHeroHurtMotions ?? 0),
      { timeout: 20_000 },
    ).toBeGreaterThanOrEqual(1)
    expect(await page.evaluate(() => (window as Window & { __rcgHeroHurtSources?: string[] })
      .__rcgHeroHurtSources ?? [])).toContain('/characters/player_hurt.png?v=1')
    expect(await page.evaluate(() => (window as Window & { __rcgHeroHurtDurations?: string[] })
      .__rcgHeroHurtDurations ?? [])).toContain('0.3s')
    await expect.poll(
      () => page.evaluate(() => (window as Window & { __rcgHeroHurtFrameChanges?: number })
        .__rcgHeroHurtFrameChanges ?? 0),
      { timeout: 20_000 },
    ).toBeGreaterThanOrEqual(1)
  }
  await page.emulateMedia({ reducedMotion: 'no-preference' })
})

test('hero hurt motion visibly advances with reduced motion enabled', async ({ page }) => {
  const started = await createRunAndEnterFirstBattle(page)
  const heroId = started.state.allies[0].instanceId
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await observeHeroAttackSlides(page)

  // Weak encounters may open with a buff/defend move. Pass turns until a real
  // HP-damaging enemy attack occurs; with no player attacks the target remains
  // alive and the test does not depend on a particular encounter roll.
  let tookAttackDamage = false
  for (let turn = 0; turn < 4 && !tookAttackDamage; turn++) {
    const response = await endTurn(page)
    tookAttackDamage = response.steps.some(step => step.event.kind === 'DealDamage'
      && step.event.targetInstanceId === heroId
      && step.event.damageType === 'Attack'
      && (step.event.actualHpLoss ?? 0) > 0)
    if (!tookAttackDamage) await waitForPlayerInput(page)
  }
  expect(tookAttackDamage).toBe(true)

  await expect.poll(
    () => page.evaluate(() => (window as Window & { __rcgHeroHurtMotions?: number })
      .__rcgHeroHurtMotions ?? 0),
    { timeout: 20_000 },
  ).toBeGreaterThanOrEqual(1)
  expect(await page.evaluate(() => (window as Window & { __rcgHeroHurtSources?: string[] })
    .__rcgHeroHurtSources ?? [])).toContain('/characters/player_hurt.png?v=1')
  expect(await page.evaluate(() => (window as Window & { __rcgHeroHurtDurations?: string[] })
    .__rcgHeroHurtDurations ?? [])).toContain('0.3s')
  await expect.poll(
    () => page.evaluate(() => (window as Window & { __rcgHeroHurtFrameChanges?: number })
      .__rcgHeroHurtFrameChanges ?? 0),
    { timeout: 20_000 },
  ).toBeGreaterThanOrEqual(1)
})

test('hero idle sprite sheet is eight distinct crisp pixel-art frames', async ({ page }) => {
  await page.goto('/')
  const decoded = await page.evaluate(async () => {
    const image = new Image()
    image.src = '/characters/player_idle.png'
    await image.decode()

    const canvas = document.createElement('canvas')
    canvas.width = image.naturalWidth
    canvas.height = image.naturalHeight
    const context = canvas.getContext('2d', { willReadFrequently: true })
    if (!context) throw new Error('2D canvas context is unavailable')
    context.drawImage(image, 0, 0)
    const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data
    const colors = new Set<string>()
    const frameHashes: number[] = []
    const frameOpaqueCounts: number[] = []
    const frameTouchesBaseline: boolean[] = []
    let binaryAlpha = true

    for (let frame = 0; frame < 8; frame++) {
      let hash = 2166136261
      let opaqueCount = 0
      let touchesBaseline = false
      for (let y = 0; y < 256; y++) {
        for (let localX = 0; localX < 128; localX++) {
          const x = frame * 128 + localX
          const offset = (y * canvas.width + x) * 4
          const red = pixels[offset]
          const green = pixels[offset + 1]
          const blue = pixels[offset + 2]
          const alpha = pixels[offset + 3]
          if (alpha !== 0 && alpha !== 255) binaryAlpha = false
          if (alpha === 255) {
            opaqueCount++
            colors.add(`${red},${green},${blue}`)
            if (y === 255) touchesBaseline = true
          }
          hash = Math.imul(hash ^ red, 16777619)
          hash = Math.imul(hash ^ green, 16777619)
          hash = Math.imul(hash ^ blue, 16777619)
          hash = Math.imul(hash ^ alpha, 16777619)
        }
      }
      frameHashes.push(hash >>> 0)
      frameOpaqueCounts.push(opaqueCount)
      frameTouchesBaseline.push(touchesBaseline)
    }

    return {
      width: image.naturalWidth,
      height: image.naturalHeight,
      binaryAlpha,
      opaqueColorCount: colors.size,
      distinctFrameCount: new Set(frameHashes).size,
      frameOpaqueCounts,
      frameTouchesBaseline,
    }
  })

  expect(decoded).toMatchObject({
    width: 1024,
    height: 256,
    binaryAlpha: true,
    opaqueColorCount: 16,
    distinctFrameCount: 8,
    frameTouchesBaseline: Array(8).fill(true),
  })
  for (const opaqueCount of decoded.frameOpaqueCounts) {
    expect(opaqueCount).toBeGreaterThan(10_000)
  }
})

test('hero attack and hurt sheets are four-frame pixel-art motions', async ({ page }) => {
  await page.goto('/')
  const decoded = await page.evaluate(async () => {
    const specs = [
      { motion: 'attack', source: '/characters/player_attack.png' },
      { motion: 'hurt', source: '/characters/player_hurt.png' },
    ]
    return await Promise.all(specs.map(async spec => {
      const image = new Image()
      image.src = spec.source
      await image.decode()
      const canvas = document.createElement('canvas')
      canvas.width = image.naturalWidth
      canvas.height = image.naturalHeight
      const context = canvas.getContext('2d', { willReadFrequently: true })
      if (!context) throw new Error('2D canvas context is unavailable')
      context.drawImage(image, 0, 0)
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data
      const colors = new Set<string>()
      const frameHashes: number[] = []
      const frameOpaqueCounts: number[] = []
      const frameTouchesBaseline: boolean[] = []
      let binaryAlpha = true

      for (let frame = 0; frame < 4; frame++) {
        let hash = 2166136261
        let opaqueCount = 0
        let touchesBaseline = false
        for (let y = 0; y < 256; y++) {
          for (let localX = 0; localX < 192; localX++) {
            const x = frame * 192 + localX
            const offset = (y * canvas.width + x) * 4
            const red = pixels[offset]
            const green = pixels[offset + 1]
            const blue = pixels[offset + 2]
            const alpha = pixels[offset + 3]
            if (alpha !== 0 && alpha !== 255) binaryAlpha = false
            if (alpha === 255) {
              opaqueCount++
              colors.add(`${red},${green},${blue}`)
              if (y === 255) touchesBaseline = true
            }
            hash = Math.imul(hash ^ red, 16777619)
            hash = Math.imul(hash ^ green, 16777619)
            hash = Math.imul(hash ^ blue, 16777619)
            hash = Math.imul(hash ^ alpha, 16777619)
          }
        }
        frameHashes.push(hash >>> 0)
        frameOpaqueCounts.push(opaqueCount)
        frameTouchesBaseline.push(touchesBaseline)
      }

      return {
        motion: spec.motion,
        width: image.naturalWidth,
        height: image.naturalHeight,
        binaryAlpha,
        opaqueColorCount: colors.size,
        distinctFrameCount: new Set(frameHashes).size,
        frameOpaqueCounts,
        frameTouchesBaseline,
      }
    }))
  })

  for (const sheet of decoded) {
    expect(sheet).toMatchObject({
      width: 768,
      height: 256,
      binaryAlpha: true,
      opaqueColorCount: 16,
      distinctFrameCount: 4,
      frameTouchesBaseline: Array(4).fill(true),
    })
    for (const opaqueCount of sheet.frameOpaqueCounts) {
      expect(opaqueCount).toBeGreaterThan(10_000)
    }
  }
})

test('generated combat status PNGs are crisp pixel art in a real browser', async ({ page }) => {
  await page.goto('/')
  const decoded = await page.evaluate(async () => {
    const sources = [
      '/icons/ui/barrier.png',
      '/icons/ui/lifesteal.png',
      '/icons/ui/reflect.png',
      '/icons/ui/on_hit.png',
    ]
    return await Promise.all(sources.map(async source => {
      const image = new Image()
      image.src = source
      await image.decode()
      const canvas = document.createElement('canvas')
      canvas.width = image.naturalWidth
      canvas.height = image.naturalHeight
      const context = canvas.getContext('2d', { willReadFrequently: true })
      if (!context) throw new Error('2D canvas context is unavailable')
      context.drawImage(image, 0, 0)
      const pixels = context.getImageData(0, 0, canvas.width, canvas.height).data
      const colorAt = (x: number, y: number) => {
        const offset = (y * canvas.width + x) * 4
        return [pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]]
      }
      let pixelGrid3x = true
      let binaryAlpha = true
      const opaqueColors = new Set<string>()
      for (let y = 0; y < canvas.height; y++) {
        for (let x = 0; x < canvas.width; x++) {
          const color = colorAt(x, y)
          const blockColor = colorAt(x - (x % 3), y - (y % 3))
          if (color.some((channel, index) => channel !== blockColor[index])) pixelGrid3x = false
          if (color[3] !== 0 && color[3] !== 255) binaryAlpha = false
          if (color[3] === 255) opaqueColors.add(color.slice(0, 3).join(','))
        }
      }
      return {
        source,
        width: image.naturalWidth,
        height: image.naturalHeight,
        pixelGrid3x,
        binaryAlpha,
        opaqueColorCount: opaqueColors.size,
      }
    }))
  })

  expect(decoded.map(({ source, width, height }) => ({ source, width, height }))).toEqual([
    { source: '/icons/ui/barrier.png', width: 180, height: 180 },
    { source: '/icons/ui/lifesteal.png', width: 180, height: 180 },
    { source: '/icons/ui/reflect.png', width: 180, height: 180 },
    { source: '/icons/ui/on_hit.png', width: 180, height: 180 },
  ])
  for (const image of decoded) {
    expect(image.pixelGrid3x).toBe(true)
    expect(image.binaryAlpha).toBe(true)
    expect(image.opaqueColorCount).toBeLessThanOrEqual(20)
  }
})
