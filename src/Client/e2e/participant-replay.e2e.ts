import { expect, test } from '@playwright/test'

const catalog = {
  cards: {
    wisp_spark: {
      id: 'wisp_spark', name: '火花', displayName: '火花', rarity: 0,
      cardType: 'attack', cost: 1, upgradedCost: 1, upgradable: false,
      description: '敵単体に9アタック。', upgradedDescription: null,
    },
  },
  enemies: {
    slime: { id: 'slime', name: 'スライム', imageId: '', hp: 30, initialMoveId: '', heightTier: 2 },
  },
  units: {
    wisp: {
      id: 'wisp', name: 'ウィスプ', imageId: '', hp: 30,
      initialMoveId: '', lifetimeTurns: null, heightTier: 2,
      skills: [],
    },
  },
  characters: {
    default: { id: 'default', name: '冒険者', maxHp: 80, startingGold: 0, potionSlotCount: 0, heightTier: 5, skills: [] },
  },
  potions: {},
  relics: [],
}

test('participant replay presents the card and progressive shared-resolution attack frames', async ({ page }) => {
  await page.route('**/api/v1/catalog/**', async route => {
    const key = new URL(route.request().url()).pathname.split('/').at(-1) as keyof typeof catalog
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(catalog[key] ?? {}) })
  })
  await page.route('**/api/v1/runs/current/battle/replay?**', async route => {
    const states = await page.evaluate(async () => {
      const fixture = await import('/e2e/participant-replay.fixture.tsx')
      return {
        before: fixture.before,
        afterCard: fixture.afterCard,
        afterResolution: fixture.afterResolution,
      }
    })
    const common = {
      sourceActorInstanceId: 'wisp-1', targetParticipantId: null,
      targetActorInstanceId: 'enemy-1', statusId: null, description: null,
      hitLanded: true,
    }
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        fromSequenceExclusive: 0, latestSequence: 2, returnedThroughSequence: 2,
        turn: 1, participantId: 'cpu:wisp-1', actorInstanceId: 'wisp-1',
        feedState: 'complete', isReady: true, isComplete: true, participantFeeds: [],
        entries: [
          {
            sequence: 1, entryId: 'decision', turn: 1, commandId: 'decision',
            kind: 'playCard', viewKind: 'action', participantId: 'cpu:wisp-1',
            actorInstanceId: 'wisp-1', revisionBefore: 1, revisionAfter: 2,
            card: {
              cardInstanceId: 'spark-1', definitionId: 'wisp_spark', displayName: '火花',
              cardType: 'Attack', rarity: 'Token', isUpgraded: false,
              originalCost: 1, currentCost: 1, paidCost: 1,
              description: '敵単体に9アタック。', costChanges: [],
            },
            targetActorInstanceIds: ['enemy-1'], effects: [], intents: [], feedChanges: [],
            isResolution: false, note: null, stateBefore: states.before, stateAfter: states.afterCard,
            source: { actorInstanceId: 'wisp-1', actorDisplayName: 'ウィスプ', actionKind: 'card', actionDefinitionId: 'wisp_spark', actionDisplayName: '火花' },
          },
          {
            sequence: 2, entryId: 'resolution', turn: 1, commandId: 'resolution',
            kind: 'resolution', viewKind: 'resolution', participantId: null,
            actorInstanceId: null, revisionBefore: 2, revisionAfter: 3, card: null,
            targetActorInstanceIds: ['enemy-1'],
            effects: [
              { ...common, kind: 'AttackFire', amount: 9, hitIndex: 0 },
              { ...common, kind: 'DealDamage', amount: 7, actualHpLoss: 7, blockedAmount: 2, hitIndex: 0 },
              { ...common, kind: 'AttackFire', amount: 5, hitIndex: 1 },
              { ...common, kind: 'DealDamage', amount: 5, actualHpLoss: 5, blockedAmount: 0, hitIndex: 1 },
            ],
            intents: [], feedChanges: [], isResolution: true, note: null,
            stateBefore: states.afterCard, stateAfter: states.afterResolution,
            source: { actorInstanceId: 'wisp-1', actorDisplayName: 'ウィスプ', actionKind: 'resolution', actionDefinitionId: null, actionDisplayName: '攻撃解決' },
          },
        ],
      }),
    })
  })
  await page.route('**/api/v1/runs/current/battle', async route => {
    const state = await page.evaluate(async () => (await import('/e2e/participant-replay.fixture.tsx')).afterResolution)
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(state) })
  })

  await page.goto('/')
  await page.evaluate(async () => {
    const fixture = await import('/e2e/participant-replay.fixture.tsx')
    await fixture.mountParticipantReplayFixture()
  })
  await page.waitForFunction(() => document.documentElement.dataset.participantReplayReady === 'true')
  await expect(page.getByRole('button', { name: 'ウィスプの行動を見る' })).toBeVisible()

  await page.evaluate(() => {
    const observed = { card: false, attack: false, hit: false, hp: [] as string[] }
    ;(window as typeof window & { __participantReplayObserved?: typeof observed })
      .__participantReplayObserved = observed
    const record = () => {
      observed.card ||= document.querySelector('.playing-card[data-replay-action="card"]') !== null
      observed.attack ||= document.querySelector('.battle__slot.is-attacking-enemy') !== null
      observed.hit ||= document.querySelector('.battle__slot.is-hit') !== null
      for (const node of document.querySelectorAll('.battle__side--enemy .status-hp__num')) {
        const value = node.textContent?.trim() ?? ''
        if (value && !observed.hp.includes(value)) observed.hp.push(value)
      }
    }
    new MutationObserver(record).observe(document.body, {
      subtree: true, childList: true, characterData: true, attributes: true,
      attributeFilter: ['class'],
    })
    record()
  })
  await page.getByRole('button', { name: 'ウィスプの行動を見る' }).click()
  await expect.poll(async () => page.evaluate(() => {
    const observed = (
      window as typeof window & {
        __participantReplayObserved?: { card: boolean; attack: boolean; hit: boolean; hp: string[] }
      }
    ).__participantReplayObserved
    return Boolean(
      observed?.card
      && observed.attack
      && observed.hit
      && observed.hp.includes('23/30')
      && observed.hp.includes('18/30'),
    )
  }), { timeout: 12_000 }).toBe(true)

  const observed = await page.evaluate(() => (
    window as typeof window & {
      __participantReplayObserved?: { card: boolean; attack: boolean; hit: boolean; hp: string[] }
    }
  ).__participantReplayObserved)
  expect(observed).toEqual(expect.objectContaining({ card: true, attack: true, hit: true }))
  expect(observed?.hp).toContain('23/30')
  expect(observed?.hp).toContain('18/30')
})
