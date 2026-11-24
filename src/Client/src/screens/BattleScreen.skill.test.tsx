import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CardCatalog, CharacterCatalog } from '../api/catalog'
import type {
  BattleActionResponseDto,
  BattleCardInstanceDto,
  BattleParticipantDto,
  BattleReplayCardDto,
  BattleStateDto,
  CombatActorDto,
  RunSnapshotDto,
} from '../api/types'
import { TooltipHost } from '../components/Tooltip'
import { BattleScreen } from './BattleScreen'
import { characterSkillCutinDurationMs } from './battleScreen/characterSkillCutinPresentation'

describe('characterSkillCutinDurationMs', () => {
  it('keeps authored pacing at normal speed and readable floors at fast playback', () => {
    expect(characterSkillCutinDurationMs('tactical')).toBe(1900)
    expect(characterSkillCutinDurationMs('ultimate')).toBe(2400)
    expect(characterSkillCutinDurationMs('tactical', 2)).toBe(1400)
    expect(characterSkillCutinDurationMs('ultimate', 2)).toBe(1600)
  })

  it('keeps a readable static-fade duration when motion is disabled', () => {
    expect(characterSkillCutinDurationMs('tactical', 1, false)).toBe(1400)
    expect(characterSkillCutinDurationMs('ultimate', 1, false)).toBe(1600)
  })
})

const cardCatalog: CardCatalog = {
  expensive_a: {
    id: 'expensive_a',
    name: '高コストA',
    displayName: '高コストA',
    rarity: 1,
    cardType: 'attack',
    cost: 5,
    upgradedCost: 4,
    upgradable: true,
    description: '敵単体に5ダメージ。',
    upgradedDescription: '敵単体に7ダメージ。',
  },
  expensive_b: {
    id: 'expensive_b',
    name: '高コストB',
    displayName: '高コストB',
    rarity: 1,
    cardType: 'skill',
    cost: 4,
    upgradedCost: 3,
    upgradable: true,
    description: '4ブロック。',
    upgradedDescription: '6ブロック。',
  },
}

const unitCatalog = {
  wisp: {
    id: 'wisp',
    name: 'ウィスプ',
    imageId: 'wisp',
    hp: 30,
    initialMoveId: 'spark',
    lifetimeTurns: null,
    heightTier: 2,
    skills: [
      {
        id: 'tactical',
        name: '燐火の構え',
        energyCost: 0,
        comboGaugeCost: 2,
        oncePerTurn: true,
        usesPerTurn: 1,
        description: 'ターン中1度だけ使用可能(0/1)。\n自身に筋力1を付与。',
        requiresCardsInHand: false,
      },
      {
        id: 'ultimate',
        name: '星火爆発',
        energyCost: 0,
        comboGaugeCost: 5,
        oncePerTurn: true,
        usesPerTurn: 1,
        description: '敵全体に10アタック。',
        requiresCardsInHand: false,
      },
    ],
  },
}

let characterCatalogMock: CharacterCatalog | null = null

vi.mock('../hooks/useCardCatalog', () => ({
  useCardCatalog: () => ({
    catalog: cardCatalog,
    names: Object.fromEntries(Object.entries(cardCatalog).map(([id, card]) => [id, card.name])),
  }),
  usePotionCatalog: () => ({ catalog: null, names: {} }),
}))

vi.mock('../hooks/useRelicCatalog', () => ({
  useRelicCatalog: () => ({ catalog: null, names: {} }),
}))

vi.mock('../hooks/useCharacterCatalog', () => ({
  useCharacterCatalog: () => ({
    catalog: characterCatalogMock,
    names: characterCatalogMock
      ? Object.fromEntries(Object.entries(characterCatalogMock).map(([id, entry]) => [id, entry.name]))
      : {},
    loading: false,
  }),
}))

afterEach(() => {
  characterCatalogMock = null
})

vi.mock('../hooks/useEnemyCatalog', () => ({
  useEnemyCatalog: () => ({ catalog: null, names: {}, loading: false }),
}))

vi.mock('../hooks/useUnitCatalog', () => ({
  useUnitCatalog: () => ({ catalog: unitCatalog, names: { wisp: 'ウィスプ' }, loading: false }),
}))

function actor(
  instanceId: string,
  definitionId: string,
  side: 'Ally' | 'Enemy',
): CombatActorDto {
  return {
    instanceId,
    definitionId,
    side,
    slotIndex: 0,
    currentHp: side === 'Ally' ? 80 : 30,
    maxHp: side === 'Ally' ? 80 : 30,
    blockDisplay: 0,
    attackSingleDisplay: 0,
    attackRandomDisplay: 0,
    attackAllDisplay: 0,
    statuses: {},
    currentMoveId: null,
    remainingLifetimeTurns: null,
    associatedSummonHeldInstanceId: null,
    intent: null,
  }
}

function card(
  instanceId: string,
  cardDefinitionId: string,
  currentCost?: number,
): BattleCardInstanceDto {
  const originalCost = cardCatalog[cardDefinitionId].cost
  return {
    instanceId,
    cardDefinitionId,
    isUpgraded: false,
    originalCost,
    currentCost: currentCost ?? originalCost,
    costOverride: currentCost !== undefined && currentCost !== originalCost
      ? currentCost
      : null,
    adjustedDescription: null,
    adjustedUpgradedDescription: null,
  }
}

function battleState(overrides: Partial<BattleStateDto> = {}): BattleStateDto {
  return {
    turn: 1,
    phase: 'PlayerInput',
    outcome: 'Pending',
    allies: [actor('hero-1', 'hero', 'Ally')],
    enemies: [actor('enemy-1', 'slime', 'Enemy')],
    targetAllyIndex: 0,
    targetEnemyIndex: 0,
    energy: 3,
    energyPerTurn: 3,
    drawPile: [],
    hand: [
      card('hand-a', 'expensive_a'),
      card('hand-b', 'expensive_b'),
    ],
    discardPile: [],
    exhaustPile: [],
    summonHeld: [],
    powerCards: [],
    comboCount: 0,
    maxComboThisTurn: 0,
    comboGauge: 10,
    usedCharacterSkillIdsThisTurn: [],
    lastPlayedOrigCost: null,
    nextCardComboFreePass: false,
    ownedRelicIds: [],
    potions: [],
    encounterId: 'test-encounter',
    pendingCardPlay: null,
    ...overrides,
  }
}

function participant(overrides: Partial<BattleParticipantDto> = {}): BattleParticipantDto {
  return {
    participantId: 'player:0',
    actorInstanceId: 'hero-1',
    controllerKind: 'Human',
    ownerPlayerSlot: 0,
    characterId: 'ironclad',
    targetAllyIndex: 0,
    targetEnemyIndex: 0,
    energy: 3,
    energyPerTurn: 3,
    drawPerTurn: 5,
    drawPile: [],
    hand: [],
    discardPile: [],
    exhaustPile: [],
    summonHeld: [],
    powerCards: [],
    comboCount: 0,
    maxComboThisTurn: 0,
    comboGauge: 0,
    lastPlayedOrigCost: null,
    nextCardComboFreePass: false,
    wildUsedInCurrentCombo: false,
    usedCharacterSkillIdsThisTurn: [],
    characterSkillUsesThisTurn: {},
    cardUsesThisTurn: {},
    cardUseLockedThisTurn: false,
    ownedRelicIds: [],
    potions: [],
    potionUsesThisTurn: {},
    pendingCardPlay: null,
    characterSkillsRestricted: false,
    cpuCardPlaysThisTurn: 0,
    isReady: false,
    ...overrides,
  }
}

function snapshot(): RunSnapshotDto {
  return {
    run: {
      schemaVersion: 3,
      currentAct: 1,
      currentNodeId: 1,
      visitedNodeIds: [0, 1],
      unknownResolutions: {},
      characterId: 'ironclad',
      currentHp: 80,
      maxHp: 80,
      gold: 99,
      deck: [],
      potions: [],
      potionSlotCount: 0,
      activeBattle: null,
      activeReward: null,
      activeMerchant: null,
      activeEvent: null,
      activeRestPending: false,
      activeRestCompleted: false,
      relics: [],
      playSeconds: 0,
      progress: 'InProgress',
      savedAtUtc: '2026-08-10T00:00:00Z',
      activeActStartRelicChoice: null,
    },
    map: {
      startNodeId: 0,
      bossNodeId: 2,
      nodes: [],
    },
  }
}

function renderBattle() {
  return render(
    <TooltipHost>
      <BattleScreen
        accountId="alice"
        snapshot={snapshot()}
        onBattleResolved={() => {}}
      />
    </TooltipHost>,
  )
}

describe('BattleScreen character skill integration', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('signals armed Contingencies on the Exhaust pile and its card face', async () => {
    const base = battleState()
    const initial = battleState({
      exhaustPile: [
        {
          ...base.hand[0],
          instanceId: 'armed-contingency',
          isContingencyArmed: true,
          contingencyTriggerText: '相手のアタック時',
        },
      ],
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    renderBattle()
    const exhaust = await screen.findByRole('button', {
      name: '除外 (1枚、伏策待機中1枚) を表示',
    })
    expect(exhaust).toHaveClass('has-armed-contingency')
    expect(exhaust).toHaveTextContent('伏策 1')

    fireEvent.click(exhaust)
    expect(await screen.findByText('伏策待機中：相手のアタック時')).toBeTruthy()
  })

  it('shows the selected character trait from the battle sprite', async () => {
    characterCatalogMock = {
      ironclad: {
        id: 'ironclad',
        name: '守護騎士ミレイユ',
        maxHp: 92,
        startingGold: 99,
        potionSlotCount: 3,
        heightTier: 5,
        characterClass: 'Guardian',
        deck: [],
        traitName: '積層装甲',
        traitDescription: 'ターン開始時、自身に堅持1を付与する。',
        traitEffects: 'ターン開始時、自身に[S:steadfast][N:1]を付与する。',
      },
    }
    const initial = battleState()
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    renderBattle()
    const sprite = await screen.findByLabelText(/の特性 積層装甲$/)
    fireEvent.focus(sprite)

    const tooltip = await screen.findByRole('tooltip')
    expect(tooltip).toHaveTextContent('積層装甲')
    expect(tooltip).toHaveTextContent('堅持')
  })

  it('opens saved Finisher cards and shows the Afterimage-only planned scopes', async () => {
    const initial = battleState({
      finisherRecord: {
        afterimageStacks: 2,
        savedByFinisher: true,
        isPlannedAfterimageIntentAvailable: true,
        plannedAfterimageIntent: {
          attackSingle: 20,
          attackSingleHits: 2,
          attackRandom: 8,
          attackRandomHits: 1,
          attackAll: null,
          attackAllHits: 0,
          attackHits: 3,
          block: 0,
          hasBuff: false,
          hasDebuff: false,
          hasHeal: false,
        },
        cards: [{
          playSequence: 4,
          card: {
            cardInstanceId: 'saved-finisher',
            definitionId: 'saved-finisher',
            displayName: '決着斬り',
            cardType: 'Attack',
            rarity: 'Common',
            isUpgraded: false,
            originalCost: 3,
            currentCost: 3,
            paidCost: 3,
            description: '敵単体に7アタック。',
            costChanges: [],
            poolClass: 'Striker',
          },
        }],
      },
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    fireEvent.click(await screen.findByRole('button', {
      name: 'このターンのプレイ履歴を表示（0枚、残光保存1枚）',
    }))

    expect(await screen.findByRole('dialog', { name: 'このターンのプレイ履歴' })).toBeInTheDocument()
    expect(container.querySelector('.finisher-record__cards .card__name')).toHaveTextContent('決着斬り')
    expect(container.querySelector('[data-afterimage-order="1"]')).not.toBeNull()
    expect(container.querySelector('[data-attack-summary="20(2HIT)"]')).not.toBeNull()
    expect(container.querySelector('[data-attack-summary="8(1HIT)"]')).not.toBeNull()
  })

  it('applies an ultimate ActionResponse through playSteps and refreshes gauge, hand costs, and disabled state', async () => {
    const initial = battleState()
    const afterUltimate = battleState({
      comboGauge: 0,
      hand: initial.hand.map(entry => ({
        ...entry,
        currentCost: 0,
        costOverride: 0,
      })),
    })
    const actionResponse: BattleActionResponseDto = { state: afterUltimate, steps: [] }

    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/use-ultimate-skill') && init?.method === 'POST') {
        return new Response(JSON.stringify(actionResponse), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const ultimate = await screen.findByRole('button', {
      name: '奥義（コンボゲージ6消費）',
    })
    expect(ultimate).toBeEnabled()

    fireEvent.click(ultimate)

    await screen.findByRole('meter', { name: 'コンボゲージ 0/10' })
    await waitFor(() => expect(ultimate).toBeDisabled())
    const currentCosts = [...container.querySelectorAll<HTMLElement>(
      '.hand .card__cost--current .card__cost-n',
    )]
    const originalCosts = [...container.querySelectorAll<HTMLElement>(
      '.hand .card__cost--orig .card__cost-n',
    )]
    expect(currentCosts.map(element => element.textContent)).toEqual(['0', '0'])
    expect(originalCosts.map(element => element.textContent)).toEqual(['5', '4'])
    const ultimateCall = fetchMock.mock.calls.find(([url]) =>
      String(url).endsWith('/use-ultimate-skill'))
    expect(ultimateCall).toBeDefined()
    expect(ultimateCall?.[1]).toEqual(expect.objectContaining({ method: 'POST' }))
    expect(JSON.parse(ultimateCall?.[1]?.body as string)).toEqual(expect.objectContaining({
      commandId: expect.stringMatching(/^use-ultimate-skill:/),
    }))
  })

  it('shows an essential portrait cut-in with the character and ultimate names', async () => {
    characterCatalogMock = {
      guardian_mireille: {
        id: 'guardian_mireille',
        name: '守護騎士ミレイユ',
        maxHp: 92,
        startingGold: 99,
        potionSlotCount: 3,
        heightTier: 5,
        characterClass: 'Guardian',
        deck: [],
        skills: [{
          id: 'ultimate',
          name: '鉄壁反攻',
          energyCost: 0,
          comboGaugeCost: 6,
          oncePerTurn: true,
          usesPerTurn: 1,
          description: '敵単体に自身のブロック分アタック。',
          requiresCardsInHand: false,
        }],
      },
    }
    const guardian = participant({
      characterId: 'guardian_mireille',
      comboGauge: 10,
      hand: battleState().hand,
    })
    const initial = battleState({
      comboGauge: 10,
      participants: [guardian],
    })
    const afterUltimate = battleState({
      ...initial,
      comboGauge: 4,
      allies: [{
        ...initial.allies[0],
        statuses: { strength: 1 },
      }],
      participants: [{
        ...guardian,
        comboGauge: 4,
        usedCharacterSkillIdsThisTurn: ['ultimate'],
      }],
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/use-ultimate-skill') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          state: afterUltimate,
          steps: [{
            event: {
              kind: 'UseCharacterSkill',
              order: 0,
              casterInstanceId: 'hero-1',
              targetInstanceId: null,
              amount: 6,
              cardId: null,
              note: 'ultimate',
              blockedAmount: null,
              hitIndex: null,
            },
            snapshotAfter: afterUltimate,
          }, {
            event: {
              kind: 'ApplyStatus',
              order: 1,
              casterInstanceId: 'hero-1',
              targetInstanceId: 'hero-1',
              amount: 1,
              cardId: null,
              note: 'strength',
              blockedAmount: null,
              hitIndex: null,
            },
            snapshotAfter: afterUltimate,
          }],
        } satisfies BattleActionResponseDto), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    fireEvent.click(await screen.findByRole('button', {
      name: '奥義：鉄壁反攻（コンボゲージ6消費）',
    }))

    const cutin = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.character-skill-burst--ultimate')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    expect(cutin).toHaveAttribute('data-character-id', 'guardian_mireille')
    expect(cutin).toHaveAttribute('data-skill-kind', 'ultimate')
    expect(cutin).not.toHaveAttribute('data-presentation', 'flash')
    expect(cutin.style.getPropertyValue('--skill-burst-duration')).toBe('2400ms')
    expect(cutin).toHaveTextContent('奥義')
    expect(cutin).toHaveTextContent('鉄壁反攻')
    const portrait = cutin.querySelector<HTMLImageElement>('.character-skill-burst__portrait')
    expect(portrait).toHaveAttribute('src', '/characters/portraits/guardian_mireille.png')
    expect(screen.queryByText('筋力を1付与')).toBeNull()

    fireEvent.error(portrait!)
    expect(portrait).toHaveAttribute('src', '/characters/portraits/default.png')

    expect(await screen.findByText('筋力を1付与', {}, { timeout: 3_500 })).toBeInTheDocument()
    await waitFor(() => expect(container.querySelector('.character-skill-burst')).toBeNull())
  })

  it('shows a concrete 400 ProblemDetails title and releases busy so the skill can be retried', async () => {
    const initial = battleState()
    let ultimateRequests = 0
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/use-ultimate-skill') && init?.method === 'POST') {
        ultimateRequests++
        return new Response(JSON.stringify({
          type: 'https://example.test/problems/ultimate',
          title: '奥義を使えません。',
          status: 400,
        }), {
          status: 400,
          headers: { 'Content-Type': 'application/problem+json' },
        })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    renderBattle()
    const ultimate = await screen.findByRole('button', {
      name: '奥義（コンボゲージ6消費）',
    })
    fireEvent.click(ultimate)

    expect(await screen.findByText('奥義を使えません。')).toHaveClass('battle__error')
    await waitFor(() => expect(ultimate).toBeEnabled())

    fireEvent.click(ultimate)
    await waitFor(() => expect(ultimateRequests).toBe(2))
    await waitFor(() => expect(ultimate).toBeEnabled())
  })

  it('sends only one skill command when the button is clicked twice in the same render', async () => {
    const initial = battleState()
    const afterUltimate = battleState({ comboGauge: 0 })
    let releaseUltimate!: (response: Response) => void
    const pendingUltimate = new Promise<Response>((resolve) => {
      releaseUltimate = resolve
    })
    let ultimateRequests = 0

    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/use-ultimate-skill') && init?.method === 'POST') {
        ultimateRequests++
        return pendingUltimate
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    renderBattle()
    const ultimate = await screen.findByRole('button', {
      name: '奥義（コンボゲージ6消費）',
    })

    // Keep both native clicks in one React batch. The state update is deliberately
    // not committed between them, so this verifies the synchronous ref guard.
    act(() => {
      ultimate.click()
      ultimate.click()
    })

    expect(ultimateRequests).toBe(1)
    releaseUltimate(new Response(JSON.stringify({
      state: afterUltimate,
      steps: [],
    } satisfies BattleActionResponseDto), { status: 200 }))

    await screen.findByRole('meter', { name: 'コンボゲージ 0/10' })
  })

  it('clears the moving card and releases the command lock after a rejected card play', async () => {
    const initial = battleState({ energy: 10 })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/play-card') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          title: 'カードを使用できません。',
          status: 400,
        }), {
          status: 400,
          headers: { 'Content-Type': 'application/problem+json' },
        })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      queueMicrotask(() => callback(0))
      return 1
    })

    const { container } = renderBattle()
    const endTurnButton = await screen.findByRole('button', { name: 'READY' })
    const handCard = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.hand .card[role="button"]')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })

    fireEvent.click(handCard)
    expect(container.querySelector('.playing-card')).not.toBeNull()
    expect(await screen.findByText('カードを使用できません。')).toHaveClass('battle__error')
    await waitFor(() => expect(container.querySelector('.playing-card')).toBeNull())
    await waitFor(() => expect(endTurnButton).toBeEnabled())
  })

  it('shows an unavailable reason only after the player attempts the card', async () => {
    const initial = battleState({ energy: 0 })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const unavailableCard = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.hand .card[role="button"]')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })

    fireEvent.mouseEnter(unavailableCard)
    expect(container.querySelector('.hand-card-unavailable')).toBeNull()

    // Finish the genuine one-shot hand-entry animation before exercising the
    // rejection animation. Removing the rejection class later must not
    // re-apply card-enter and masquerade as a delayed draw.
    fireEvent.animationEnd(unavailableCard, { animationName: 'card-enter' })
    expect(unavailableCard).not.toHaveClass('is-hand-entering')

    vi.useFakeTimers()
    try {
      fireEvent.click(unavailableCard)
      const reason = container.querySelector('.hand-card-unavailable')
      expect(reason).not.toBeNull()
      expect(reason).toHaveClass('is-visible')
      expect(fetchMock).toHaveBeenCalledTimes(1)

      await act(async () => {
        await vi.advanceTimersByTimeAsync(1_500)
      })
      expect(container.querySelector('.hand-card-unavailable')).toBeNull()
      expect(unavailableCard).not.toHaveClass('is-hand-entering')
      expect(container.querySelector('.draw-overlay')).toBeNull()
    } finally {
      vi.useRealTimers()
    }
  })

  it('renders legacy status notes with official Japanese combat feedback', async () => {
    const initial = battleState({ comboGauge: 10 })
    const afterUltimate = battleState({
      comboGauge: 4,
      allies: [{
        ...initial.allies[0],
        statuses: { strength: -2 },
      }],
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/use-ultimate-skill') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          state: afterUltimate,
          steps: [{
            event: {
              kind: 'ApplyStatus',
              order: 0,
              casterInstanceId: 'hero-1',
              targetInstanceId: 'hero-1',
              amount: -2,
              cardId: null,
              note: 'strength:start',
              blockedAmount: null,
              hitIndex: null,
            },
            snapshotAfter: afterUltimate,
          }],
        } satisfies BattleActionResponseDto), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    renderBattle()
    fireEvent.click(await screen.findByRole('button', {
      name: '奥義（コンボゲージ6消費）',
    }))

    expect(await screen.findByText('筋力が2低下')).toBeInTheDocument()
    expect(screen.queryByText(/strength|start/i)).toBeNull()
  })

  it('keeps the complete card face while playing and centres a randomly exhausted card', async () => {
    const initial = battleState({ energy: 10 })
    const afterPlay = battleState({
      energy: 5,
      hand: [],
      discardPile: [initial.hand[0]],
      exhaustPile: [initial.hand[1]],
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/play-card') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          state: afterPlay,
          steps: [],
        } satisfies BattleActionResponseDto), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })
    vi.stubGlobal('requestAnimationFrame', (callback: FrameRequestCallback) => {
      queueMicrotask(() => callback(0))
      return 1
    })

    const { container } = renderBattle()
    const sourceCard = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.hand .card[role="button"]')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    fireEvent.click(sourceCard)

    const playing = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.playing-card')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    expect(playing.querySelector('.card__desc')).toHaveTextContent('敵単体に5ダメージ。')
    expect(playing.querySelector('.card__type')).toHaveTextContent('ATTACK')

    const exhausted = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.effect-exhaust-card')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    expect(exhausted).toHaveAttribute('data-animation-kind', 'effect-exhaust')
    expect(exhausted.style.getPropertyValue('--exhaust-center-x')).toBe('640px')
    expect(exhausted.style.getPropertyValue('--exhaust-center-y')).toBe('360px')
    expect(exhausted.querySelector('.card__desc')).toHaveTextContent('4ブロック。')
    expect(exhausted.querySelector('.card__type')).toHaveTextContent('SKILL')
  })

  it('uses the same centred exhaust overlay after a manual card choice', async () => {
    const base = battleState({ energy: 10 })
    const initial = battleState({
      energy: 10,
      pendingCardPlay: {
        cardInstanceId: 'hand-a',
        effectIndex: 0,
        choice: {
          action: 'exhaustCard',
          pile: 'hand',
          count: 1,
          candidateInstanceIds: ['hand-b'],
        },
      },
    })
    const afterChoice = battleState({
      energy: 5,
      hand: [],
      discardPile: [base.hand[0]],
      exhaustPile: [base.hand[1]],
      pendingCardPlay: null,
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/resolve-card-choice') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          state: afterChoice,
          steps: [],
        } satisfies BattleActionResponseDto), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const candidate = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.ccm-hand__card.is-candidate')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    fireEvent.click(candidate)
    fireEvent.click(screen.getByRole('button', { name: '確定' }))

    const exhausted = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.effect-exhaust-card')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    expect(exhausted.style.getPropertyValue('--exhaust-center-x')).toBe('640px')
    expect(exhausted.style.getPropertyValue('--exhaust-center-y')).toBe('360px')
    expect(exhausted.querySelector('.card__name')).toHaveTextContent('高コストB')
    expect(exhausted.querySelector('.card__desc')).toHaveTextContent('4ブロック。')
  })

  it('holds a triggered Contingency through reconnect and choice, then moves it on resolution', async () => {
    const base = battleState({ energy: 10 })
    const contingencyCard: BattleCardInstanceDto = {
      instanceId: 'pending-contingency',
      cardDefinitionId: 'catalog-not-loaded-contingency',
      isUpgraded: false,
      originalCost: 2,
      currentCost: 2,
      costOverride: null,
      adjustedDescription: '[K:contingency]：相手のアタック時。1枚捨てる。',
      adjustedUpgradedDescription: null,
    }
    const candidate = base.hand[1]
    const initial = battleState({
      energy: 10,
      hand: [candidate],
      exhaustPile: [contingencyCard],
      pendingCardPlay: {
        cardInstanceId: contingencyCard.instanceId,
        effectIndex: 0,
        choice: {
          action: 'discard',
          pile: 'hand',
          count: 1,
          candidateInstanceIds: [candidate.instanceId],
        },
      },
    })
    const completed = battleState({
      energy: 10,
      hand: [],
      discardPile: [candidate, contingencyCard],
      exhaustPile: [],
      pendingCardPlay: null,
    })
    const replayCard: BattleReplayCardDto = {
      cardInstanceId: contingencyCard.instanceId,
      definitionId: contingencyCard.cardDefinitionId,
      displayName: '迎撃伏策',
      cardType: 'Skill',
      rarity: 'Common',
      isUpgraded: false,
      originalCost: 2,
      currentCost: 2,
      paidCost: 0,
      description: contingencyCard.adjustedDescription!,
      costChanges: [],
      poolClass: 'Tactician',
    }
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/resolve-card-choice') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          state: completed,
          steps: [{
            event: {
              kind: 'ContingencyResolved',
              order: 0,
              casterInstanceId: 'hero-1',
              targetInstanceId: null,
              amount: null,
              cardId: contingencyCard.cardDefinitionId,
              note: 'discard',
              blockedAmount: null,
              hitIndex: null,
              damageType: null,
              source: null,
              causeChain: [],
              actualHpLoss: null,
              overkill: null,
              hitLanded: null,
              card: replayCard,
            },
            snapshotAfter: completed,
          }],
        } satisfies BattleActionResponseDto), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const held = await waitFor(() => {
      const element = container.querySelector<HTMLElement>(
        '[data-reenact-card="pending-contingency"]',
      )
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    expect(held).toHaveAttribute('data-reenact-stage', 'holding')
    expect(held).toHaveAttribute('data-reenact-movement', 'contingency-discard')

    const candidateElement = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.ccm-hand__card.is-candidate')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    fireEvent.click(candidateElement)
    expect(container.querySelector('[data-reenact-card="pending-contingency"]')).not.toBeNull()
    fireEvent.click(screen.getByRole('button', { name: '確定' }))

    const leaving = await waitFor(() => {
      const element = container.querySelector<HTMLElement>(
        '[data-reenact-card="pending-contingency"]',
      )
      expect(element).toHaveAttribute('data-reenact-stage', 'leaving')
      return element as HTMLElement
    })
    expect(leaving).toHaveAttribute('data-reenact-movement', 'contingency-discard')
    await waitFor(() => {
      expect(container.querySelector('[data-reenact-card="pending-contingency"]')).toBeNull()
    })
  })

  it('uses the DTO description to spin a Contingency into Exhaust before catalog data exists', async () => {
    const fallbackCard: BattleCardInstanceDto = {
      instanceId: 'dto-only-contingency',
      cardDefinitionId: 'catalog-not-loaded-contingency',
      isUpgraded: false,
      originalCost: 1,
      currentCost: 1,
      costOverride: null,
      adjustedDescription: '[K:contingency]：ターン開始時。1ブロック。',
      adjustedUpgradedDescription: null,
    }
    const initial = battleState({ energy: 10, hand: [fallbackCard] })
    const completed = battleState({
      energy: 9,
      hand: [],
      exhaustPile: [{ ...fallbackCard, isContingencyArmed: true }],
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/play-card') && init?.method === 'POST') {
        return new Response(JSON.stringify({
          state: completed,
          steps: [],
        } satisfies BattleActionResponseDto), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const handCard = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.hand .card[role="button"]')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    fireEvent.click(handCard)

    const playing = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.playing-card')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    expect(playing).toHaveAttribute('data-leaving-mode', 'exhaust')

    // Let the asynchronous play pipeline finish before this test releases the
    // shared fetch mock. Otherwise its delayed POST can leak into the next
    // command-lock test while the card is still in the centering animation.
    await waitFor(() => {
      expect(fetchMock.mock.calls.some(([input, init]) =>
        String(input).endsWith('/runs/current/battle/play-card')
          && (init as RequestInit | undefined)?.method === 'POST',
      )).toBe(true)
    })
    await waitFor(() => {
      expect(container.querySelector('.playing-card')).toBeNull()
    })
  })

  it('blocks end-turn and skill commands from the first frame of a card animation', async () => {
    const initial = battleState({ energy: 10 })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })
    // Hold the card at its first animation frame. This isolates the exact window
    // that previously existed before withBusy acquired its lock.
    vi.stubGlobal('requestAnimationFrame', vi.fn(() => 1))

    const { container, unmount } = renderBattle()
    const endTurnButton = await screen.findByRole('button', { name: 'READY' })
    const tacticalButton = screen.getByRole('button', {
      name: '戦技（コンボゲージ3消費）',
    })
    const handCard = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('.hand .card[role="button"]')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })

    act(() => {
      handCard.click()
      endTurnButton.click()
      tacticalButton.click()
    })

    const postRequests = fetchMock.mock.calls.filter(
      ([, init]) => (init as RequestInit | undefined)?.method === 'POST',
    )
    expect(postRequests).toHaveLength(0)
    expect(container.querySelector('.playing-card')).not.toBeNull()
    unmount()
  })
})

describe('BattleScreen participant Ready and replay integration', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('opens a CPU ally feed and switches the resource HUD to its frozen participant view', async () => {
    const wispActor = { ...actor('wisp-1', 'wisp', 'Ally'), slotIndex: 1, maxHp: 30, currentHp: 30 }
    const cpuHand = [card('spark-1', 'expensive_a', 1)]
    const cpuBefore = participant({
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
      ownerPlayerSlot: 0,
      characterId: 'wisp',
      energy: 2,
      energyPerTurn: 1,
      drawPerTurn: 3,
      hand: cpuHand,
      comboCount: 0,
      maxComboThisTurn: 0,
      comboGauge: 2,
      isReady: false,
    })
    const cpuAfter = participant({
      ...cpuBefore,
      energy: 1,
      hand: [],
      discardPile: cpuHand,
      comboCount: 1,
      maxComboThisTurn: 2,
      isReady: true,
    })
    const primary = participant({ hand: battleState().hand })
    const wispActorAfter = {
      ...wispActor,
      attackSingleDisplay: 3,
      intent: {
        attackSingle: 3,
        attackRandom: null,
        attackAll: null,
        attackHits: 1,
        attackSingleHits: 1,
        attackRandomHits: null,
        attackAllHits: null,
        block: null,
        hasBuff: false,
        hasDebuff: false,
        hasHeal: false,
      },
    }
    const initial = battleState({
      allies: [actor('hero-1', 'hero', 'Ally'), wispActorAfter],
      // The live state is already later than the replayed action: its target died.
      enemies: [],
      actionRevision: 3,
      participants: [
        primary,
        cpuAfter,
      ],
    })
    const decisionBefore = battleState({
      allies: [actor('hero-1', 'hero', 'Ally'), wispActor],
      enemies: [actor('enemy-1', 'slime', 'Enemy')],
      actionRevision: 2,
      participants: [primary, cpuBefore],
    })
    const replay = {
      fromSequenceExclusive: 0,
      latestSequence: 1,
      returnedThroughSequence: 1,
      turn: 1,
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      feedState: 'ready',
      isReady: true,
      isComplete: false,
      participantFeeds: [],
      entries: [{
        sequence: 1,
        turn: 1,
        commandId: 'cpu-1',
        kind: 'playCard',
        viewKind: 'action',
        participantId: 'cpu:wisp-1',
        actorInstanceId: 'wisp-1',
        revisionBefore: 2,
        revisionAfter: 3,
        card: {
          cardInstanceId: 'spark-1',
          definitionId: 'wisp_spark',
          displayName: '火花',
          cardType: 'Attack',
          rarity: 'Token',
          isUpgraded: false,
          originalCost: 1,
          currentCost: 1,
          paidCost: 1,
          description: '敵単体に3アタック。',
          costChanges: [],
        },
        targetActorInstanceIds: ['enemy-1'],
        effects: [],
        intents: [{
          kind: 'attack',
          scope: 'single',
          amount: 3,
          hits: 1,
          targetParticipantId: null,
          targetActorInstanceId: 'enemy-1',
        }],
        feedChanges: [],
        isResolution: false,
        note: null,
        stateBefore: decisionBefore,
        stateAfter: initial,
        source: {
          actorInstanceId: 'wisp-1',
          actorDisplayName: 'ウィスプ',
          actionKind: 'card',
          actionDefinitionId: 'wisp_spark',
          actionDisplayName: '火花',
        },
      }],
    }

    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.includes('/runs/current/battle/replay?') && init?.method === 'GET') {
        return new Response(JSON.stringify(replay), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    fireEvent.click(await screen.findByRole('button', { name: 'ウィスプの行動を見る' }))
    await screen.findByLabelText('ウィスプの行動再生')
    await waitFor(() => expect(screen.getByLabelText('行動状態 READY')).toBeInTheDocument())
    await waitFor(() => expect(screen.getByRole('button', { name: '停止' })).toBeInTheDocument(), {
      timeout: 2_500,
    })
    fireEvent.click(screen.getByRole('button', { name: '停止' }))

    expect(screen.getByRole('button', {
      name: '戦技：燐火の構え（コンボゲージ2消費）',
    })).toBeDisabled()
    expect(screen.getByRole('button', {
      name: '奥義：星火爆発（コンボゲージ5消費）',
    })).toBeDisabled()

    expect(container.querySelector('.energy__cur')).toHaveTextContent('2')
    expect(container.querySelector('.battle__combo-current')).toHaveTextContent('0')
    expect(screen.getByText('高コストA')).toBeInTheDocument()
    expect(container.querySelectorAll(
      '.battle__side--enemy .battle__slot[data-occupied="1"]',
    )).toHaveLength(1)

    fireEvent.click(screen.getByRole('button', { name: 'スキップ' }))

    expect(container.querySelector('.energy__cur')).toHaveTextContent('1')
    expect(container.querySelector('.battle__combo-current')).toHaveTextContent('1')
    expect(screen.queryByText('高コストA')).not.toBeInTheDocument()
    expect(container.querySelectorAll(
      '.battle__side--enemy .battle__slot[data-occupied="1"]',
    )).toHaveLength(0)
    expect(container.querySelector('[data-replay-view="source-card"]')).not.toBeNull()
    expect(screen.getAllByText('火花')).toHaveLength(2)
    fireEvent.click(screen.getByRole('button', { name: '戻る' }))
    await waitFor(() => expect(screen.queryByLabelText('ウィスプの行動再生')).not.toBeInTheDocument())
  })

  it('pops a reenacted card exactly once without removing its source from the hand', async () => {
    const wispActor = {
      ...actor('wisp-1', 'wisp', 'Ally'),
      slotIndex: 1,
      currentHp: 30,
      maxHp: 30,
    }
    const sourceCard = card('source-stays-in-hand', 'expensive_a', 1)
    const primary = participant({ hand: battleState().hand })
    const cpuOwner = participant({
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
      ownerPlayerSlot: 0,
      characterId: 'wisp',
      hand: [sourceCard],
      isReady: true,
    })
    const frozen = battleState({
      allies: [actor('hero-1', 'hero', 'Ally'), wispActor],
      participants: [primary, cpuOwner],
      actionRevision: 2,
    })
    const reenactedCard = {
      cardInstanceId: sourceCard.instanceId,
      definitionId: sourceCard.cardDefinitionId,
      displayName: '残光の一撃',
      cardType: 'Attack',
      rarity: 'Rare',
      isUpgraded: true,
      originalCost: 4,
      currentCost: 4,
      paidCost: 0,
      description: '敵単体に12アタック。',
      costChanges: [],
      poolClass: 'Striker',
    }
    const replay = {
      fromSequenceExclusive: 0,
      latestSequence: 1,
      returnedThroughSequence: 1,
      turn: 1,
      participantId: cpuOwner.participantId,
      actorInstanceId: cpuOwner.actorInstanceId,
      feedState: 'complete',
      isReady: true,
      isComplete: true,
      participantFeeds: [],
      entries: [{
        sequence: 1,
        turn: 1,
        commandId: 'afterimage-reenact',
        kind: 'resolution',
        viewKind: 'resolution',
        participantId: cpuOwner.participantId,
        actorInstanceId: cpuOwner.actorInstanceId,
        revisionBefore: 2,
        revisionAfter: 2,
        card: null,
        targetActorInstanceIds: [],
        effects: [{
          kind: 'ReenactCardEffects',
          sourceActorInstanceId: cpuOwner.actorInstanceId,
          targetParticipantId: null,
          targetActorInstanceId: null,
          amount: null,
          statusId: null,
          description: '残光',
          cardDefinitionId: sourceCard.cardDefinitionId,
          cardInstanceId: sourceCard.instanceId,
          card: reenactedCard,
        }],
        intents: [],
        feedChanges: [],
        isResolution: true,
        note: null,
        stateBefore: frozen,
        stateAfter: frozen,
        source: {
          actorInstanceId: cpuOwner.actorInstanceId,
          actorDisplayName: 'ウィスプ',
          actionKind: 'resolution',
          actionDefinitionId: null,
          actionDisplayName: '残光',
        },
      }],
    }
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(frozen), { status: 200 })
      }
      if (url.includes('/runs/current/battle/replay?') && init?.method === 'GET') {
        return new Response(JSON.stringify(replay), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    let reenactMounts = 0
    const observer = new MutationObserver(records => {
      for (const record of records) {
        for (const node of record.addedNodes) {
          if (!(node instanceof Element)) continue
          if (node.matches('.reenact-card')) reenactMounts += 1
          reenactMounts += node.querySelectorAll('.reenact-card').length
        }
      }
    })
    observer.observe(container, { childList: true, subtree: true })

    fireEvent.click(await screen.findByRole('button', { name: /行動を見る/ }))
    const reenact = await waitFor(() => {
      const element = container.querySelector<HTMLElement>(
        '[data-reenact-card="source-stays-in-hand"]',
      )
      expect(element).not.toBeNull()
      return element as HTMLElement
    }, { timeout: 2_500 })

    expect(reenact.querySelector('.card__name')).toHaveTextContent('残光の一撃')
    expect(container.querySelector('.playing-card')).toBeNull()
    expect(container.querySelectorAll('.hand .card')).toHaveLength(1)
    await waitFor(() => {
      expect(container.querySelector('.reenact-card')).toBeNull()
    }, { timeout: 2_000 })
    observer.disconnect()
    expect(reenactMounts).toBe(1)
    expect(container.querySelectorAll('.hand .card')).toHaveLength(1)
  })

  it('replays a decision card, then shared multi-hit resolution with progressive motion and HP', async () => {
    const wisp = { ...actor('wisp-1', 'wisp', 'Ally'), slotIndex: 1, currentHp: 30, maxHp: 30 }
    const enemyBefore = { ...actor('enemy-1', 'slime', 'Enemy'), currentHp: 30, blockDisplay: 2 }
    const enemyAfter = { ...enemyBefore, currentHp: 18, blockDisplay: 0 }
    const cpuCard = card('spark-1', 'expensive_a', 1)
    const primary = participant({ hand: battleState().hand })
    const cpuBefore = participant({
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
      ownerPlayerSlot: 0,
      characterId: 'wisp',
      energy: 2,
      energyPerTurn: 1,
      hand: [cpuCard],
    })
    const cpuAfterCard = participant({
      ...cpuBefore,
      energy: 1,
      hand: [],
      discardPile: [cpuCard],
      comboCount: 1,
      maxComboThisTurn: 1,
    })
    const before = battleState({
      allies: [actor('hero-1', 'hero', 'Ally'), wisp],
      enemies: [enemyBefore],
      participants: [primary, cpuBefore],
      actionRevision: 1,
    })
    const afterCard = battleState({
      ...before,
      participants: [primary, cpuAfterCard],
      actionRevision: 2,
    })
    const afterResolution = battleState({
      ...afterCard,
      enemies: [enemyAfter],
      actionRevision: 3,
    })
    const commonEffect = {
      sourceActorInstanceId: 'wisp-1',
      targetParticipantId: null,
      targetActorInstanceId: 'enemy-1',
      statusId: null,
      description: null,
      hitLanded: true,
    }
    const replay = {
      fromSequenceExclusive: 0,
      latestSequence: 2,
      returnedThroughSequence: 2,
      turn: 1,
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      feedState: 'complete',
      isReady: true,
      isComplete: true,
      participantFeeds: [],
      entries: [
        {
          sequence: 1,
          turn: 1,
          commandId: 'cpu-card',
          kind: 'playCard',
          viewKind: 'action',
          participantId: 'cpu:wisp-1',
          actorInstanceId: 'wisp-1',
          revisionBefore: 1,
          revisionAfter: 2,
          card: {
            cardInstanceId: 'spark-1',
            definitionId: 'wisp_spark',
            displayName: '火花',
            cardType: 'Attack',
            rarity: 'Token',
            isUpgraded: false,
            originalCost: 1,
            currentCost: 1,
            paidCost: 1,
            description: '敵単体に9アタック。',
            costChanges: [],
          },
          targetActorInstanceIds: ['enemy-1'],
          effects: [],
          intents: [],
          feedChanges: [],
          isResolution: false,
          note: null,
          stateBefore: before,
          stateAfter: afterCard,
          source: {
            actorInstanceId: 'wisp-1',
            actorDisplayName: 'ウィスプ',
            actionKind: 'card',
            actionDefinitionId: 'wisp_spark',
            actionDisplayName: '火花',
          },
        },
        {
          sequence: 2,
          turn: 1,
          commandId: 'shared-resolution',
          kind: 'resolution',
          viewKind: 'resolution',
          participantId: null,
          actorInstanceId: null,
          revisionBefore: 2,
          revisionAfter: 3,
          card: null,
          targetActorInstanceIds: ['enemy-1'],
          effects: [
            { ...commonEffect, kind: 'AttackFire', amount: 9, hitIndex: 0 },
            {
              ...commonEffect,
              kind: 'DealDamage',
              amount: 7,
              actualHpLoss: 7,
              blockedAmount: 2,
              hitIndex: 0,
            },
            { ...commonEffect, kind: 'AttackFire', amount: 5, hitIndex: 1 },
            {
              ...commonEffect,
              kind: 'DealDamage',
              amount: 5,
              actualHpLoss: 5,
              blockedAmount: 0,
              hitIndex: 1,
            },
          ],
          intents: [],
          feedChanges: [],
          isResolution: true,
          note: null,
          stateBefore: afterCard,
          stateAfter: afterResolution,
          source: {
            actorInstanceId: 'wisp-1',
            actorDisplayName: 'ウィスプ',
            actionKind: 'resolution',
            actionDefinitionId: null,
            actionDisplayName: '攻撃解決',
          },
        },
      ],
    }

    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(afterResolution), { status: 200 })
      }
      if (url.includes('/runs/current/battle/replay?') && init?.method === 'GET') {
        return new Response(JSON.stringify(replay), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    fireEvent.click(await screen.findByRole('button', { name: 'ウィスプの行動を見る' }))

    const replayCard = await waitFor(() => {
      const element = container.querySelector<HTMLElement>(
        '.playing-card[data-replay-action="card"]',
      )
      expect(element).not.toBeNull()
      return element as HTMLElement
    }, { timeout: 2_500 })
    expect(replayCard.querySelector('.card__name')).toHaveTextContent('火花')
    expect(replayCard.querySelector('.card__desc')).toHaveTextContent('敵単体に9アタック。')
    // The source card is already represented by the central playback card;
    // it must not remain duplicated in the participant hand.
    expect(container.querySelectorAll('.hand .card .card__name')).toHaveLength(0)

    await waitFor(() => {
      expect(container.querySelector(
        '.battle__side--player .battle__slot.is-attacking-enemy',
      )).not.toBeNull()
    }, { timeout: 2_000 })
    await waitFor(() => {
      expect(container.querySelector(
        '.battle__side--enemy .battle__slot.is-hit',
      )).not.toBeNull()
      expect(screen.getByText('23/30')).toBeInTheDocument()
    }, { timeout: 2_000 })
    await waitFor(() => expect(screen.getByText('18/30')).toBeInTheDocument(), {
      timeout: 2_000,
    })
    expect(container.querySelector('.playing-card[data-replay-action="card"]')).toBeNull()
  })

  it('toggles Ready and unready with optimistic action revisions', async () => {
    const human = participant({ isReady: false, hand: battleState().hand })
    const initial = battleState({ actionRevision: 7, participants: [human] })
    const readyState = battleState({
      actionRevision: 8,
      participants: [{ ...human, isReady: true }],
    })
    const liveState = battleState({
      actionRevision: 9,
      participants: [{ ...human, isReady: false }],
    })
    let readyRequests = 0
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/set-ready') && init?.method === 'POST') {
        readyRequests++
        return new Response(JSON.stringify({
          state: readyRequests === 1 ? readyState : liveState,
          steps: [],
        }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    renderBattle()
    fireEvent.click(await screen.findByRole('button', { name: 'READY' }))
    const unready = await screen.findByRole('button', { name: 'READY 解除' })
    expect(unready).toHaveAttribute('aria-pressed', 'true')
    expect(JSON.parse(fetchMock.mock.calls[1][1].body as string)).toEqual(expect.objectContaining({
      participantId: 'player:0',
      actorInstanceId: 'hero-1',
      isReady: true,
      expectedRevision: 7,
      commandId: expect.stringMatching(/^ready:/),
    }))

    fireEvent.click(unready)
    await screen.findByRole('button', { name: 'READY' })
    expect(JSON.parse(fetchMock.mock.calls[2][1].body as string)).toEqual(expect.objectContaining({
      participantId: 'player:0',
      actorInstanceId: 'hero-1',
      isReady: false,
      expectedRevision: 8,
      commandId: expect.stringMatching(/^ready:/),
    }))
  })

  it('keeps the player marked READY while same-turn resolution is animating', async () => {
    const human = participant({ isReady: false, hand: battleState().hand })
    const initial = battleState({ actionRevision: 7, participants: [human] })
    // The server may already have reset the participant flag by the time the
    // response reaches the client, while the current turn's attack steps are
    // still being shown. The local READY intent must bridge that interval.
    const resolving = battleState({
      phase: 'PlayerInput',
      actionRevision: 8,
      participants: [{ ...human, isReady: false }],
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/set-ready') && init?.method === 'POST') {
        return new Response(JSON.stringify({ state: resolving, steps: [] }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    fireEvent.click(await screen.findByRole('button', { name: 'READY' }))

    await waitFor(() => {
      const readiness = container.querySelector<HTMLElement>('.status-readiness')
      expect(readiness).not.toBeNull()
      expect(readiness).toHaveAttribute('data-ready', 'true')
      expect(readiness).toHaveTextContent('READY')
    })
  })

  it('keeps a slot gap but sends the surviving slot-2 ally as DTO array index 1', async () => {
    const human = participant({ hand: battleState().hand })
    const survivingSummon = {
      ...actor('wisp-slot-2', 'wisp', 'Ally'),
      slotIndex: 2,
      currentHp: 30,
      maxHp: 30,
    }
    const initial = battleState({
      allies: [actor('hero-1', 'hero', 'Ally'), survivingSummon],
      targetAllyIndex: 0,
      actionRevision: 4,
      participants: [human],
    })
    const selected = battleState({
      ...initial,
      targetAllyIndex: 1,
      actionRevision: 5,
      participants: [{ ...human, targetAllyIndex: 1 }],
    })

    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.endsWith('/runs/current/battle/set-target') && init?.method === 'POST') {
        return new Response(JSON.stringify(selected), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const slots = await waitFor(() => {
      const result = container.querySelectorAll<HTMLElement>(
        '.battle__side--player > .battle__slot',
      )
      expect(result).toHaveLength(4)
      return result
    })
    expect(slots[0]).toHaveAttribute('data-occupied', '1')
    expect(slots[1]).toHaveAttribute('data-occupied', '0')
    expect(slots[2]).toHaveAttribute('data-occupied', '1')

    fireEvent.click(slots[2])
    await waitFor(() => {
      const targetCall = fetchMock.mock.calls.find(([input, init]) =>
        String(input).endsWith('/runs/current/battle/set-target')
        && (init as RequestInit | undefined)?.method === 'POST')
      expect(targetCall).toBeDefined()
      expect(JSON.parse((targetCall![1] as RequestInit).body as string)).toEqual(
        expect.objectContaining({
          side: 'Ally',
          // Core/DTO target contract is array index; stable visual SlotIndex is 2.
          slotIndex: 1,
        }),
      )
    })
  })

  it('labels actor size classes and lifts an airborne summon without moving its shadow', async () => {
    const wisp = {
      ...actor('wisp-airborne', 'wisp', 'Ally'),
      slotIndex: 1,
      currentHp: 30,
      maxHp: 30,
    }
    const initial = battleState({
      allies: [actor('hero-1', 'hero', 'Ally'), wisp],
    })
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const airborne = await waitFor(() => {
      const element = container.querySelector<HTMLElement>('[data-actor-id="wisp-airborne"]')
      expect(element).not.toBeNull()
      return element as HTMLElement
    })
    const hero = container.querySelector<HTMLElement>('[data-actor-id="hero-1"]')

    expect(hero).toHaveAttribute('data-size-class', 'medium')
    expect(hero).toHaveAttribute('data-airborne', 'false')
    expect(airborne).toHaveAttribute('data-size-class', 'small')
    expect(airborne).toHaveAttribute('data-airborne', 'true')
    expect(airborne.style.getPropertyValue('--sprite-lift')).toBe('31px')
    expect(airborne.querySelector('.sprite-shadow')).not.toBeNull()
  })

  it('resumes tail-follow playback when a READY feed reopens LIVE with an appended action', async () => {
    const wispActor = {
      ...actor('wisp-1', 'wisp', 'Ally'),
      slotIndex: 1,
      currentHp: 30,
      maxHp: 30,
    }
    const primary = participant({ hand: battleState().hand })
    const cpuOwner = participant({
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
      ownerPlayerSlot: 0,
      characterId: 'wisp',
      isReady: true,
    })
    const initial = battleState({
      allies: [actor('hero-1', 'hero', 'Ally'), wispActor],
      participants: [primary, cpuOwner],
      actionRevision: 1,
    })
    const after = battleState({
      ...initial,
      participants: [primary, { ...cpuOwner, energy: 1, isReady: false }],
      actionRevision: 2,
    })
    const appendedEntry = {
      sequence: 2,
      entryId: 'appended-after-ready',
      turn: 1,
      commandId: 'appended-after-ready',
      kind: 'playCard',
      viewKind: 'action',
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      revisionBefore: 1,
      revisionAfter: 2,
      card: {
        cardInstanceId: 'new-card',
        definitionId: 'wisp_spark',
        displayName: '追加の火花',
        cardType: 'Attack',
        rarity: 'Token',
        isUpgraded: false,
        originalCost: 1,
        currentCost: 1,
        paidCost: 1,
        description: '敵単体に3アタック。',
        costChanges: [],
      },
      targetActorInstanceIds: ['enemy-1'],
      effects: [],
      intents: [],
      feedChanges: [],
      isResolution: false,
      note: null,
      stateBefore: initial,
      stateAfter: after,
      source: {
        actorInstanceId: 'wisp-1',
        actorDisplayName: 'ウィスプ',
        actionKind: 'card',
        actionDefinitionId: 'wisp_spark',
        actionDisplayName: '追加の火花',
      },
    }
    let replayRequests = 0
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(initial), { status: 200 })
      }
      if (url.includes('/runs/current/battle/replay?') && init?.method === 'GET') {
        replayRequests += 1
        const hasAppend = replayRequests > 1
        return new Response(JSON.stringify({
          fromSequenceExclusive: hasAppend ? 0 : 0,
          latestSequence: hasAppend ? 2 : 0,
          returnedThroughSequence: hasAppend ? 2 : 0,
          turn: 1,
          participantId: 'cpu:wisp-1',
          actorInstanceId: 'wisp-1',
          feedState: hasAppend ? 'live' : 'ready',
          isReady: !hasAppend,
          isComplete: false,
          participantFeeds: [],
          entries: hasAppend ? [appendedEntry] : [],
        }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const openReplay = await screen.findByRole('button', { name: 'ウィスプの行動を見る' })
    vi.useFakeTimers()
    await act(async () => {
      fireEvent.click(openReplay)
      await Promise.resolve()
      await Promise.resolve()
    })
    expect(screen.getByRole('button', { name: '再生' })).toBeInTheDocument()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(900)
      await Promise.resolve()
    })
    expect(replayRequests).toBeGreaterThanOrEqual(2)
    expect(screen.getByRole('button', { name: '停止' })).toBeInTheDocument()
    expect(container.querySelector(
      '.playing-card[data-replay-action="card"]',
    )).not.toBeNull()

    fireEvent.click(screen.getByRole('button', { name: '戻る' }))
  })

  it('auto-plays appended COMPLETE resolution and stages multi-hit hero HP in the TopBar', async () => {
    const hero = actor('hero-1', 'hero', 'Ally')
    const wispActor = {
      ...actor('wisp-1', 'wisp', 'Ally'),
      slotIndex: 1,
      currentHp: 30,
      maxHp: 30,
    }
    const enemy = actor('enemy-1', 'slime', 'Enemy')
    const primary = participant({ hand: battleState().hand })
    const cpuOwner = participant({
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
      ownerPlayerSlot: 0,
      characterId: 'wisp',
      isReady: true,
    })
    const before = battleState({
      allies: [hero, wispActor],
      enemies: [enemy],
      participants: [primary, cpuOwner],
      actionRevision: 1,
    })
    const after = battleState({
      ...before,
      allies: [{ ...hero, currentHp: 70 }, wispActor],
      actionRevision: 2,
    })
    const commonEffect = {
      sourceActorInstanceId: 'enemy-1',
      targetParticipantId: 'player:0',
      targetActorInstanceId: 'hero-1',
      statusId: null,
      description: null,
      hitLanded: true,
    }
    const resolution = {
      sequence: 2,
      entryId: 'complete-resolution',
      turn: 1,
      commandId: 'complete-resolution',
      kind: 'resolution',
      viewKind: 'resolution',
      participantId: null,
      actorInstanceId: null,
      revisionBefore: 1,
      revisionAfter: 2,
      card: null,
      targetActorInstanceIds: ['hero-1'],
      effects: [
        { ...commonEffect, kind: 'AttackFire', amount: 7, hitIndex: 0 },
        {
          ...commonEffect,
          kind: 'DealDamage',
          amount: 5,
          actualHpLoss: 5,
          blockedAmount: 2,
          hitIndex: 0,
        },
        { ...commonEffect, kind: 'AttackFire', amount: 5, hitIndex: 1 },
        {
          ...commonEffect,
          kind: 'DealDamage',
          amount: 5,
          actualHpLoss: 5,
          blockedAmount: 0,
          hitIndex: 1,
        },
      ],
      intents: [],
      feedChanges: [],
      isResolution: true,
      note: null,
      stateBefore: before,
      stateAfter: after,
      source: {
        actorInstanceId: 'enemy-1',
        actorDisplayName: 'スライム',
        actionKind: 'resolution',
        actionDefinitionId: null,
        actionDisplayName: '攻撃解決',
      },
    }
    let replayRequests = 0
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(before), { status: 200 })
      }
      if (url.includes('/runs/current/battle/replay?') && init?.method === 'GET') {
        replayRequests += 1
        const complete = replayRequests > 1
        return new Response(JSON.stringify({
          fromSequenceExclusive: 0,
          latestSequence: complete ? 2 : 0,
          returnedThroughSequence: complete ? 2 : 0,
          turn: 1,
          participantId: 'cpu:wisp-1',
          actorInstanceId: 'wisp-1',
          feedState: complete ? 'complete' : 'ready',
          isReady: true,
          isComplete: complete,
          participantFeeds: [],
          entries: complete ? [resolution] : [],
        }), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    const openReplay = await screen.findByRole('button', { name: 'ウィスプの行動を見る' })
    vi.useFakeTimers()
    await act(async () => {
      fireEvent.click(openReplay)
      await Promise.resolve()
      await Promise.resolve()
    })
    expect(screen.getByRole('button', { name: '再生' })).toBeInTheDocument()

    await act(async () => {
      await vi.advanceTimersByTimeAsync(900)
      await Promise.resolve()
    })
    expect(screen.getByLabelText('行動状態 COMPLETE')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '停止' })).toBeInTheDocument()
    expect(container.querySelector(
      '.battle__side--enemy .battle__slot.is-attacking-ally',
    )).not.toBeNull()

    await act(async () => {
      // The TopBar deliberately eases HP changes. Stop late in the first-hit
      // presentation window, after its tween has visibly reached 75 but
      // before the second hit is applied.
      await vi.advanceTimersByTimeAsync(390)
    })
    expect(container.querySelector(
      '.battle__side--player .status-hp__num',
    )).toHaveTextContent('75/80')
    expect(container.querySelector('.topbar__hp-label')).toHaveTextContent('HP 75/80')

    await act(async () => {
      // Two readable attack beats now include anticipation, contact flash and
      // recovery instead of resolving in the former 240ms slot nudge.
      await vi.advanceTimersByTimeAsync(1_500)
    })
    expect(container.querySelector(
      '.battle__side--player .status-hp__num',
    )).toHaveTextContent('70/80')
    await act(async () => {
      // The replay action has now committed its final snapshot. Let TopBar's
      // normal post-action easing settle on the same authoritative value.
      await vi.advanceTimersByTimeAsync(500)
    })
    expect(container.querySelector('.topbar__hp-label')).toHaveTextContent('HP 70/80')

    fireEvent.click(screen.getByRole('button', { name: '戻る' }))
  })

  it('keeps the opened summon resources and skills after resolution removes its participant', async () => {
    const wispActor = {
      ...actor('wisp-1', 'wisp', 'Ally'),
      slotIndex: 1,
      currentHp: 30,
      maxHp: 30,
    }
    const primary = participant({ hand: [], energy: 3 })
    const cpuHand = [card('wisp-only-card', 'expensive_b', 4)]
    const cpuOwner = participant({
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
      ownerPlayerSlot: 0,
      characterId: 'wisp',
      energy: 8,
      hand: cpuHand,
      isReady: true,
    })
    const before = battleState({
      allies: [actor('hero-1', 'hero', 'Ally'), wispActor],
      participants: [primary, cpuOwner],
      actionRevision: 1,
    })
    const after = battleState({
      ...before,
      allies: [actor('hero-1', 'hero', 'Ally')],
      participants: [primary],
      hand: [],
      energy: 3,
      actionRevision: 2,
    })
    const replay = {
      fromSequenceExclusive: 0,
      latestSequence: 1,
      returnedThroughSequence: 1,
      turn: 1,
      participantId: 'cpu:wisp-1',
      actorInstanceId: 'wisp-1',
      feedState: 'complete',
      isReady: true,
      isComplete: true,
      participantFeeds: [],
      entries: [{
        sequence: 1,
        entryId: 'summon-cleanup',
        turn: 1,
        commandId: 'summon-cleanup',
        kind: 'resolution',
        viewKind: 'resolution',
        participantId: null,
        actorInstanceId: null,
        revisionBefore: 1,
        revisionAfter: 2,
        card: null,
        targetActorInstanceIds: ['wisp-1'],
        effects: [],
        intents: [],
        feedChanges: [],
        isResolution: true,
        note: null,
        stateBefore: before,
        stateAfter: after,
        source: {
          actorInstanceId: null,
          actorDisplayName: '戦闘解決',
          actionKind: 'resolution',
          actionDefinitionId: null,
          actionDisplayName: '召喚退場',
        },
      }],
    }
    fetchMock.mockImplementation(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.endsWith('/runs/current/battle') && init?.method === 'GET') {
        return new Response(JSON.stringify(before), { status: 200 })
      }
      if (url.includes('/runs/current/battle/replay?') && init?.method === 'GET') {
        return new Response(JSON.stringify(replay), { status: 200 })
      }
      throw new Error(`Unexpected request: ${init?.method} ${url}`)
    })

    const { container } = renderBattle()
    fireEvent.click(await screen.findByRole('button', { name: 'ウィスプの行動を見る' }))
    await waitFor(() => expect(screen.getByLabelText('行動状態 READY')).toBeInTheDocument())

    expect(container.querySelector('.energy__cur')).toHaveTextContent('8')
    expect(screen.getByText('高コストB')).toBeInTheDocument()
    expect(screen.getByRole('button', {
      name: '戦技：燐火の構え（コンボゲージ2消費）',
    })).toBeDisabled()
    fireEvent.focus(screen.getByRole('button', {
      name: '戦技：燐火の構え（コンボゲージ2消費）',
    }).parentElement!)
    const tacticalTooltip = screen.getByRole('tooltip')
    expect(tacticalTooltip).toHaveTextContent('自身に筋力1を付与。')
    expect(tacticalTooltip.textContent?.match(/ターン中1度だけ使用可能\(0\/1\)。/g)).toHaveLength(1)
    await waitFor(() => expect(container.querySelectorAll(
      '.battle__side--player .battle__slot[data-occupied="1"]',
    )).toHaveLength(1), { timeout: 4_000 })
  })
})
