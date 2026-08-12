import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { CardCatalog } from '../api/catalog'
import type {
  BattleActionResponseDto,
  BattleCardInstanceDto,
  BattleStateDto,
  CombatActorDto,
  RunSnapshotDto,
} from '../api/types'
import { TooltipHost } from '../components/Tooltip'
import { BattleScreen } from './BattleScreen'

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
  useCharacterCatalog: () => ({ catalog: null, names: {}, loading: false }),
}))

vi.mock('../hooks/useEnemyCatalog', () => ({
  useEnemyCatalog: () => ({ catalog: null, names: {}, loading: false }),
}))

vi.mock('../hooks/useUnitCatalog', () => ({
  useUnitCatalog: () => ({ catalog: null, names: {}, loading: false }),
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
    vi.unstubAllGlobals()
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
      name: '奥義（コンボゲージ10消費）',
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
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/runs/current/battle/use-ultimate-skill',
      expect.objectContaining({ method: 'POST', body: '{}' }),
    )
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
      name: '奥義（コンボゲージ10消費）',
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
      name: '奥義（コンボゲージ10消費）',
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
    const endTurnButton = await screen.findByRole('button', { name: 'TURN END' })
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
    const endTurnButton = await screen.findByRole('button', { name: 'TURN END' })
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
