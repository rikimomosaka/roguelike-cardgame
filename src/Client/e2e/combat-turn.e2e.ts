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

function isBattleAction(
  response: Response,
  action: 'start' | 'play-card' | 'end-turn' | 'set-ready',
) {
  const url = new URL(response.url())
  return response.request().method() === 'POST'
    && url.pathname === `/api/v1/runs/current/battle/${action}`
}

async function waitForPlayerInput(page: Page) {
  await expect(page.getByRole('button', { name: 'READY' })).toBeEnabled({ timeout: 20_000 })
}

type CharacterPick = {
  className: string
  characterName: string
}

const DEFAULT_CHARACTER: CharacterPick = {
  className: 'ストライカー',
  characterName: '残光剣士セラ',
}

async function createRunAndEnterFirstBattle(
  page: Page,
  character: CharacterPick = DEFAULT_CHARACTER,
): Promise<BattleActionResponse> {
  await page.goto('/')

  await page.getByLabel('アカウント ID').fill(`combat-e2e-${Date.now()}`)
  await page.getByRole('button', { name: 'アカウント作成' }).click()
  await page.getByRole('button', { name: 'はじめから' }).click()

  await expect(page.getByRole('heading', { name: 'キャラクター選択' })).toBeVisible()
  await page.getByRole('tab', { name: new RegExp(`^${character.className}`) }).click()
  await page.locator('.character-pick__select')
    .filter({ hasText: character.characterName })
    .click()
  await page.getByRole('button', { name: 'このキャラクターで開始' }).click()

  // The current-location marker intentionally pulses, so Playwright's
  // stability check can otherwise wait forever while the button is usable.
  await page.locator('[data-start-entry="true"]').click({ force: true })
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
  // Participant-aware battles resolve through Ready. Legacy saved battles may
  // still use end-turn, so accept either endpoint during migration.
  const responsePromise = page.waitForResponse(response =>
    isBattleAction(response, 'set-ready') || isBattleAction(response, 'end-turn'))
  await page.getByRole('button', { name: 'READY' }).click()
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
  // Starting with 攻撃の基本 (cost 1) leaves enough energy for any other
  // playable Striker starter attack after combo reduction.
  return names.length >= 2 && names.includes('攻撃の基本')
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
            const computed = getComputedStyle(image)
            const durationMs = Number.parseFloat(computed.animationDuration) * 1_000
            const frameCount = Math.max(
              1,
              Number.parseInt(computed.getPropertyValue('--sprite-step-count'), 10) || 1,
            )
            const firstTransform = getComputedStyle(image).transform
            window.setTimeout(() => {
              if (image.isConnected && getComputedStyle(image).transform !== firstTransform) {
                observedWindow.__rcgHeroAttackFrameChanges!++
              }
            }, Math.min(
              Math.max(40, durationMs - 20),
              Math.max(50, Math.round(durationMs / frameCount * 1.35)),
            ))
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
            const computed = getComputedStyle(image)
            const durationMs = Number.parseFloat(computed.animationDuration) * 1_000
            const frameCount = Math.max(
              1,
              Number.parseInt(computed.getPropertyValue('--sprite-step-count'), 10) || 1,
            )
            const firstTransform = getComputedStyle(image).transform
            window.setTimeout(() => {
              if (image.isConnected && getComputedStyle(image).transform !== firstTransform) {
                observedWindow.__rcgHeroHurtFrameChanges!++
              }
            }, Math.min(
              Math.max(40, durationMs - 20),
              Math.max(50, Math.round(durationMs / frameCount * 1.35)),
            ))
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
    const initialComputed = getComputedStyle(image)
    const durationMs = Number.parseFloat(initialComputed.animationDuration) * 1_000
    const frameCount = Math.max(
      1,
      Number.parseInt(initialComputed.getPropertyValue('--sprite-step-count'), 10) || 1,
    )
    const naturalFirstTransform = getComputedStyle(image).transform
    await new Promise(resolve => setTimeout(resolve, Math.min(350, Math.max(80, durationMs / frameCount * 1.35))))
    const naturalSecondTransform = getComputedStyle(image).transform
    const naturalPlayState = animation.playState
    animation.pause()
    animation.currentTime = 0
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
    const firstFrameTransform = getComputedStyle(image).transform
    animation.currentTime = Math.min(durationMs - 1, durationMs / frameCount * 1.35)
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
      frameCount,
      frameAnimationName: frameComputed.animationName,
      naturalFirstTransform,
      naturalSecondTransform,
      naturalPlayState,
      firstFrameTransform,
      secondFrameTransform,
    }
  })

  expect(result).toMatchObject({
    animationName: 'character-sprite-sheet',
    animationIterationCount: 'infinite',
    // The authored eight-frame sheet owns breathing/sway; an additional
    // container scale made the hero jump in size between poses.
    frameAnimationName: 'none',
  })
  expect(result.naturalWidth).toBeGreaterThan(result.naturalHeight)
  expect(result.naturalHeight).toBeGreaterThan(0)
  expect(result.frameCount).toBeGreaterThanOrEqual(2)
  expect(Number.parseFloat(result.animationDuration)).toBeGreaterThan(0)
  expect(result.naturalPlayState).toBe('running')
  expect(result.naturalSecondTransform).not.toBe(result.naturalFirstTransform)
  expect(result.secondFrameTransform).not.toBe(result.firstFrameTransform)

  await page.emulateMedia({ reducedMotion: 'reduce' })
  const reducedMotion = await sheet.evaluate(element => {
    const image = element as HTMLImageElement
    const frame = image.closest('.sprite-sheet__frame')
    if (!frame) throw new Error('hero idle sprite frame was not found')
    return {
      sheetAnimationName: getComputedStyle(image).animationName,
      sheetAnimationDuration: getComputedStyle(image).animationDuration,
      sheetAnimationIterationCount: getComputedStyle(image).animationIterationCount,
      frameAnimationName: getComputedStyle(frame).animationName,
    }
  })
  expect(reducedMotion).toMatchObject({
    sheetAnimationName: 'character-sprite-sheet',
    sheetAnimationIterationCount: 'infinite',
    frameAnimationName: 'none',
  })
  expect(Number.parseFloat(reducedMotion.sheetAnimationDuration)).toBeGreaterThan(0)
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
        cost: document.fonts.check('20px Silkscreen'),
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
  expect(canonical.cost.width).toBeCloseTo(28, 2)
  expect(canonical.cost.height).toBeCloseTo(28, 2)
  expect(canonical.cost.centerX - canonical.digit.centerX).toBeCloseTo(0, 4)
  expect(canonical.cost.centerY - canonical.digit.centerY).toBeCloseTo(2, 4)
  expect(canonical.costFontFamily).toContain('Silkscreen')
  expect(canonical.nameFontFamily).toContain('DotGothic16')
  expect(canonical.typeFontFamily).toContain('Pixelify Sans')
  expect(canonical.costFontSize).toBe('20px')
  expect(canonical.digitTransform).toBe('matrix(1, 0, 0, 1, 0, -2)')
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
  expect(normalCostOffset).toBeCloseTo(2, 3)

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

test('a selected non-default class enters battle with its own starter deck and skills', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 720 })
  await createRunAndEnterFirstBattle(page, {
    className: 'ガーディアン',
    characterName: '鉄衛騎士ミレイユ',
  })

  await expect(page.getByRole('button', { name: /戦技：守護配置/ })).toBeVisible()
  await expect(page.getByRole('button', { name: /奥義：鉄壁反攻/ })).toBeVisible()
  await expect(page.locator('.battle__side--player .status-hp__num')).toHaveText('96/96')
  const turnEnergy = Number((await page.locator('.energy__recovery').innerText()).replace('/+', ''))
  expect(turnEnergy).toBeGreaterThanOrEqual(3)

  const starterNames = new Set<string>()
  for (let turn = 0; turn < 2; turn++) {
    for (const name of await page.locator('.hand > .card .card__name').allTextContents()) {
      starterNames.add(name.trim())
    }
    if (turn === 0) {
      await endTurn(page)
      await waitForPlayerInput(page)
    }
  }

  expect([...starterNames]).toEqual(expect.arrayContaining([
    '攻撃の基本',
    '防御の基本',
    '足固め',
    '守勢構え',
    '護衛線',
  ]))
})

test('battle controls use transparent generated artwork without exposing skill-detail noise', async ({ page }) => {
  await createRunAndEnterFirstBattle(page)

  const tactical = page.locator('[data-combo-skill="tactical"] .combo-skill-button')
  const ultimate = page.locator('[data-combo-skill="ultimate"] .combo-skill-button')
  const ready = page.locator('.end-turn')
  await expect(tactical).toHaveText(/戦技\s*消費 \d/)
  await expect(ultimate).toHaveText(/奥義\s*消費 \d/)

  const presentation = await page.evaluate(() => {
    const read = (selector: string) => {
      const element = document.querySelector<HTMLElement>(selector)
      if (!element) throw new Error(`control not found: ${selector}`)
      const pseudo = getComputedStyle(element, '::before')
      const computed = getComputedStyle(element)
      return {
        image: pseudo.backgroundImage,
        size: pseudo.backgroundSize,
        fits: element.scrollWidth <= element.clientWidth,
        backgroundColor: computed.backgroundColor,
        backgroundImage: computed.backgroundImage,
        borderWidths: [
          computed.borderTopWidth,
          computed.borderRightWidth,
          computed.borderBottomWidth,
          computed.borderLeftWidth,
        ],
        boxShadow: computed.boxShadow,
      }
    }
    return {
      tactical: read('[data-combo-skill="tactical"] .combo-skill-button'),
      ultimate: read('[data-combo-skill="ultimate"] .combo-skill-button'),
      combo: read('.battle__combo'),
      ready: read('.end-turn'),
    }
  })

  const expectedImages = {
    tactical: '/assets/ui/heroes/default_frame/tactical.png',
    ultimate: '/assets/ui/heroes/default_frame/ultimate.png',
    combo: '/assets/ui/heroes/default_frame/combo.png',
    ready: '/assets/ui/heroes/default_frame/ready.png',
  } as const
  for (const [kind, control] of Object.entries(presentation)) {
    expect(control.image).toContain(expectedImages[kind as keyof typeof expectedImages])
    expect(control.size).toBe('100% 100%')
    expect(control.fits).toBe(true)
    expect(control.backgroundColor).toBe('rgba(0, 0, 0, 0)')
    expect(control.backgroundImage).toBe('none')
    expect(control.borderWidths).toEqual(['0px', '0px', '0px', '0px'])
    expect(control.boxShadow).toBe('none')
  }

  const rasterSpecs = {
    tactical: { source: expectedImages.tactical, width: 168, height: 104 },
    ultimate: { source: expectedImages.ultimate, width: 168, height: 104 },
    combo: { source: expectedImages.combo, width: 220, height: 104 },
    ready: { source: expectedImages.ready, width: 304, height: 128 },
  }
  const rasterPresentation = await page.evaluate(async specs => Object.fromEntries(
    await Promise.all(Object.entries(specs).map(async ([kind, spec]) => {
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
      let transparentPixels = 0
      let visiblePixels = 0
      for (let offset = 3; offset < pixels.length; offset += 4) {
        if (pixels[offset] === 0) transparentPixels++
        if (pixels[offset] > 8) visiblePixels++
      }
      const alphaAt = (x: number, y: number) => pixels[(y * canvas.width + x) * 4 + 3]
      return [kind, {
        width: image.naturalWidth,
        height: image.naturalHeight,
        centerAlpha: alphaAt(Math.floor(canvas.width / 2), Math.floor(canvas.height / 2)),
        cornerAlpha: [
          alphaAt(0, 0),
          alphaAt(canvas.width - 1, 0),
          alphaAt(0, canvas.height - 1),
          alphaAt(canvas.width - 1, canvas.height - 1),
        ],
        transparentRatio: transparentPixels / (canvas.width * canvas.height),
        visibleRatio: visiblePixels / (canvas.width * canvas.height),
      }]
    })),
  ), rasterSpecs)
  for (const [kind, raster] of Object.entries(rasterPresentation)) {
    const expected = rasterSpecs[kind as keyof typeof rasterSpecs]
    expect(raster.width).toBe(expected.width)
    expect(raster.height).toBe(expected.height)
    expect(raster.cornerAlpha).toEqual([0, 0, 0, 0])
    if (kind === 'combo') expect(raster.centerAlpha).toBeGreaterThan(220)
    expect(raster.transparentRatio).toBeGreaterThan(0.1)
    expect(raster.visibleRatio).toBeGreaterThan(0.1)
  }
  await expect(ready).toBeVisible()
})

test('moving a hand card does not replay the hand-entry draw animation later', async ({ page }) => {
  await createRunAndEnterFirstBattle(page)
  // The right-most (last DOM) card owns the top stacking hit area; a middle
  // card's geometric centre may be covered by the next fanned card.
  const card = page.locator('.hand > .card:not(.is-leaving)').last()
  await expect(card).toBeVisible()
  await page.waitForTimeout(340)
  await expect(card).not.toHaveClass(/is-hand-entering/)

  const box = await card.boundingBox()
  if (!box) throw new Error('hand card geometry was unavailable')
  const startX = box.x + box.width / 2
  const startY = box.y + box.height / 2
  await page.mouse.move(startX, startY)
  await page.mouse.down()
  await page.mouse.move(startX + 34, startY - 38, { steps: 4 })
  await expect(card).toHaveClass(/is-dragging/)
  await page.mouse.up()
  await expect(card).not.toHaveClass(/is-dragging/)

  await page.waitForTimeout(1_550)
  await expect(page.locator('.draw-overlay .draw-sprite')).toHaveCount(0)
  expect(await card.evaluate(element => getComputedStyle(element).animationName)).toBe('none')
})

test('static enemies keep a slow idle presence loop in normal and reduced motion', async ({ page }) => {
  await createRunAndEnterFirstBattle(page)
  const enemy = page.locator(
    '.battle__side--enemy .sprite[data-motion="idle"] > .sprite__static-image',
  ).first()
  await expect(enemy).toBeVisible()

  for (const reducedMotion of ['no-preference', 'reduce'] as const) {
    await page.emulateMedia({ reducedMotion })
    const presentation = await enemy.evaluate(async element => {
      const animation = element.getAnimations()[0]
      if (!animation) throw new Error('static enemy idle animation was not found')
      await animation.ready
      const timing = animation.effect?.getComputedTiming()
      return {
        name: getComputedStyle(element).animationName,
        duration: Number(timing?.duration ?? 0),
        iterations: timing?.iterations,
        playState: animation.playState,
      }
    })
    expect(presentation.name).toBe(
      reducedMotion === 'reduce'
        ? 'character-idle-presence-reduced'
        : 'character-idle-presence',
    )
    expect(presentation.duration).toBeGreaterThanOrEqual(
      reducedMotion === 'reduce' ? 4_400 : 3_600,
    )
    expect(presentation.iterations).toBe(Infinity)
    expect(presentation.playState).toBe('running')
  }
})

test('two queued card attacks display and resolve as two real browser hits', async ({ page }) => {
  let beforeResolution = (await createRunAndEnterFirstBattle(page)).state
  await assertHeroIdleAnimation(page)

  // Reproduce Windows' "Animation effects: Off" setting. Combat motion is
  // shortened, but its meaningful four-frame attack/hurt state must remain visible.
  await page.emulateMedia({ reducedMotion: 'reduce' })

  // The fixed 10-card starter deck contains six attacks across three kinds.
  // With a five-or-more-card opening hand, a payable pair is guaranteed in
  // either turn 1 or turn 2. We inspect rendered cards instead of RNG details.
  if (!await hasPlayableTwoHitSequence(page)) {
    await endTurn(page)
    await waitForPlayerInput(page)
  }

  expect(await hasPlayableTwoHitSequence(page)).toBeTruthy()

  const strike = playableAttacks(page).filter({ hasText: '攻撃の基本' }).first()
  await playCard(page, strike)
  await playCard(page, playableAttacks(page).first())

  const heroIntent = page.locator('.battle__side--player .intent__seg--attack')
  await expect(heroIntent.locator('.intent__scope-label')).toHaveText('SINGLE')
  await expect(heroIntent.locator('.intent__hit-count')).toHaveText('(2HIT)')
  const displayedTotal = Number(await heroIntent.locator('.intent__num').innerText())
  // The rebuilt starter pool's cheapest pair is 攻撃の基本6 + 抜き打ち4.
  expect(displayedTotal).toBeGreaterThanOrEqual(10)
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

  // The large value is the current chain. A smaller MAX value is rendered only
  // after the current chain falls below the turn maximum.
  const comboChip = page.locator('.battle__combo')
  await expect(comboChip.locator('.battle__combo-current'))
    .toHaveText(String(beforeResolution.comboCount))
  if (beforeResolution.maxComboThisTurn > beforeResolution.comboCount) {
    await expect(comboChip.locator('.battle__combo-max'))
      .toHaveText(`(MAX${beforeResolution.maxComboThisTurn})`)
  } else {
    await expect(comboChip.locator('.battle__combo-max')).toHaveCount(0)
  }
  await expect(comboChip).toHaveAttribute(
    'aria-label',
    `現在${beforeResolution.comboCount}コンボ${beforeResolution.maxComboThisTurn > beforeResolution.comboCount
      ? ` 最大${beforeResolution.maxComboThisTurn}`
      : ''}`,
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
    expect([
      'basic_attack',
      'striker_basic_quickdraw',
      'striker_basic_finishing_slash',
    ]).toContain(event.source?.definitionId)
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
    '/assets/actors/heroes/default/attack.png',
    '/assets/actors/heroes/default/attack.png',
  ])
  const attackTimings = await page.evaluate(() => (
    window as Window & { __rcgHeroAttackTimings?: string[] }
  ).__rcgHeroAttackTimings ?? [])
  expect(attackTimings).toHaveLength(2)
  for (const timing of attackTimings) expect(timing).toMatch(/^steps\(\d+, jump-none\)$/)
  const attackDurations = await page.evaluate(() => (
    window as Window & { __rcgHeroAttackDurations?: string[] }
  ).__rcgHeroAttackDurations ?? [])
  expect(attackDurations).toHaveLength(2)
  for (const duration of attackDurations) {
    expect(Number.parseFloat(duration)).toBeGreaterThan(0)
  }
  await expect.poll(
    () => page.evaluate(() => (window as Window & { __rcgHeroAttackMotions?: number })
      .__rcgHeroAttackMotions ?? 0),
    { timeout: 20_000 },
  ).toBe(2)
  expect(await page.evaluate(() => (window as Window & { __rcgHeroAttackFrameChanges?: number })
    .__rcgHeroAttackFrameChanges ?? 0)).toBeGreaterThanOrEqual(1)

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
      .__rcgHeroHurtSources ?? [])).toContain('/assets/actors/heroes/default/hurt.png')
    const hurtDurations = await page.evaluate(() => (
      window as Window & { __rcgHeroHurtDurations?: string[] }
    ).__rcgHeroHurtDurations ?? [])
    expect(hurtDurations.some(duration => Number.parseFloat(duration) > 0)).toBe(true)
  }
  await page.emulateMedia({ reducedMotion: 'no-preference' })
})

test('normal motion visibly advances the hero attack sheet during the forward lunge', async ({ page }) => {
  await createRunAndEnterFirstBattle(page)
  await page.emulateMedia({ reducedMotion: 'no-preference' })
  await page.evaluate(() => {
    const observed = window as Window & { __rcgEnemyStaticHurtAnimations?: string[] }
    observed.__rcgEnemyStaticHurtAnimations = []
    document.addEventListener('animationstart', event => {
      const target = event.target
      if (target instanceof Element
          && target.matches('.battle__side--enemy .sprite__static-image')) {
        observed.__rcgEnemyStaticHurtAnimations!.push((event as AnimationEvent).animationName)
      }
    }, true)
  })

  const attack = playableAttacks(page).first()
  await expect(attack).toBeVisible()
  await playCard(page, attack)
  await observeHeroAttackSlides(page)
  await endTurn(page)

  await expect.poll(
    () => page.evaluate(() => (window as Window & { __rcgHeroAttackMotions?: number })
      .__rcgHeroAttackMotions ?? 0),
    { timeout: 20_000 },
  ).toBeGreaterThanOrEqual(1)
  expect((await page.evaluate(() => (
    window as Window & { __rcgHeroAttackDurations?: string[] }
  ).__rcgHeroAttackDurations ?? [])).some(duration => Number.parseFloat(duration) > 0)).toBe(true)
  await expect.poll(
    () => page.evaluate(() => (window as Window & { __rcgHeroAttackFrameChanges?: number })
      .__rcgHeroAttackFrameChanges ?? 0),
    { timeout: 5_000 },
  ).toBeGreaterThanOrEqual(1)
  await expect.poll(() => page.evaluate(() => ((
    window as Window & { __rcgEnemyStaticHurtAnimations?: string[] }
  ).__rcgEnemyStaticHurtAnimations ?? []).includes('static-actor-hurt'))).toBe(true)
})

test('reduced motion keeps the shortened four-frame hero hurt reaction visible', async ({ page }) => {
  const started = await createRunAndEnterFirstBattle(page)
  const heroId = started.state.allies[0].instanceId
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await observeHeroAttackSlides(page)
  await page.evaluate(() => {
    const observed = window as Window & { __rcgEnemyStaticAttackAnimations?: string[] }
    observed.__rcgEnemyStaticAttackAnimations = []
    document.addEventListener('animationstart', event => {
      const target = event.target
      if (target instanceof Element
          && target.matches('.battle__side--enemy .sprite__static-image')) {
        observed.__rcgEnemyStaticAttackAnimations!.push((event as AnimationEvent).animationName)
      }
    }, true)
  })

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
    .__rcgHeroHurtSources ?? [])).toContain('/assets/actors/heroes/default/hurt.png')
  expect((await page.evaluate(() => (
    window as Window & { __rcgHeroHurtDurations?: string[] }
  ).__rcgHeroHurtDurations ?? [])).some(duration => Number.parseFloat(duration) > 0)).toBe(true)
  expect(await page.evaluate(() => (window as Window & { __rcgHeroHurtFrameChanges?: number })
    .__rcgHeroHurtFrameChanges ?? 0)).toBeGreaterThanOrEqual(1)
  await expect.poll(() => page.evaluate(() => ((
    window as Window & { __rcgEnemyStaticAttackAnimations?: string[] }
  ).__rcgEnemyStaticAttackAnimations ?? []).includes('static-actor-attack'))).toBe(true)
})

test('generated combat status PNGs are crisp pixel art in a real browser', async ({ page }) => {
  await page.goto('/')
  const decoded = await page.evaluate(async () => {
    const sources = [
      '/assets/icons/status/barrier.png',
      '/assets/icons/status/lifesteal.png',
      '/assets/icons/status/reflect.png',
      '/assets/icons/status/on_hit.png',
      '/assets/icons/status/omnistrike.png',
      '/assets/icons/status/hand_cost_reduction.png',
      '/assets/icons/status/hand_cost_increase.png',
      '/assets/icons/status/vigor.png',
      '/assets/icons/status/pursuit.png',
      '/assets/icons/status/regeneration.png',
      '/assets/icons/status/fortify.png',
      '/assets/icons/status/resistance.png',
      '/assets/icons/status/revive.png',
      '/assets/icons/status/growth.png',
      '/assets/icons/status/insight.png',
      '/assets/icons/status/haste.png',
      '/assets/icons/status/continuity.png',
      '/assets/icons/status/mastery.png',
      '/assets/icons/status/virulent_poison.png',
      '/assets/icons/status/stagnation.png',
      '/assets/icons/status/burn.png',
      '/assets/icons/status/confusion.png',
      '/assets/icons/status/curse.png',
      '/assets/icons/status/bind.png',
      '/assets/icons/status/silence.png',
      '/assets/icons/status/seal.png',
      '/assets/icons/status/exhaustion.png',
      '/assets/icons/status/distress.png',
      '/assets/icons/status/afterimage.png',
      '/assets/icons/status/steadfast.png',
      '/assets/icons/status/cover.png',
      '/assets/icons/status/defensive_stance.png',
      '/assets/icons/status/combo_carryover.png',
      '/assets/icons/status/afterglow.png',
      '/assets/icons/status/resonance.png',
      '/assets/icons/status/disruption.png',
      '/assets/icons/status/status_unknown.png',
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

  expect(decoded).toHaveLength(37)
  expect(decoded.map(({ width, height }) => ({ width, height })))
    .toEqual(Array.from({ length: 37 }, () => ({ width: 180, height: 180 })))
  for (const image of decoded) {
    expect(image.pixelGrid3x).toBe(true)
    expect(image.binaryAlpha).toBe(true)
    expect(image.opaqueColorCount).toBeLessThanOrEqual(20)
  }
})
