import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountProvider } from '../context/AccountContext'
import type { BattleStateDto, CardInstanceDto, RewardStateDto, RunSnapshotDto } from '../api/types'
import { MapScreen } from './MapScreen'

function sampleSnapshot(
  overrides: Partial<RunSnapshotDto['run']> = {},
): RunSnapshotDto {
  return {
    run: {
      schemaVersion: 3,
      currentAct: 1,
      currentNodeId: 0,
      visitedNodeIds: [0],
      unknownResolutions: {},
      characterId: 'ironclad',
      currentHp: 80, maxHp: 80, gold: 99,
      deck: [] as CardInstanceDto[], relics: [], potions: ['', '', ''],
      potionSlotCount: 3,
      activeBattle: null,
      activeReward: null,
      activeMerchant: null,
      activeEvent: null,
      activeRestPending: false,
      activeRestCompleted: false,
      playSeconds: 0,
      savedAtUtc: '2026-04-21T00:00:00Z',
      progress: 'InProgress',
      ...overrides,
    },
    map: {
      startNodeId: 0,
      bossNodeId: 2,
      nodes: [
        { id: 0, row: 0, column: 2, kind: 'Start', outgoingNodeIds: [1] },
        { id: 1, row: 1, column: 2, kind: 'Enemy', outgoingNodeIds: [2] },
        { id: 2, row: 16, column: 2, kind: 'Boss', outgoingNodeIds: [] },
      ],
    },
  }
}

function sampleBattle(): BattleStateDto {
  return {
    encounterId: 'enc_w_jaw_worm',
    outcome: 'Pending',
    enemies: [
      {
        enemyDefinitionId: 'jaw_worm',
        name: 'Jaw Worm',
        imageId: 'jaw_worm',
        currentHp: 42, maxHp: 42,
        currentMoveId: 'chomp',
      },
    ],
  }
}

function sampleReward(): RewardStateDto {
  return {
    gold: 15, goldClaimed: false,
    potionId: null, potionClaimed: false,
    cardChoices: ['card_strike', 'card_bash', 'card_defend'],
    cardStatus: 'Pending',
    relicId: null,
    relicClaimed: false,
  }
}

describe('MapScreen', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    localStorage.setItem('rcg.accountId', 'alice')
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('renders nodes from snapshot and highlights current node', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot()}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByTestId('map-node-0')).toHaveAttribute('data-current', 'true')
    expect(screen.getByTestId('map-node-1')).toHaveAttribute('data-selectable', 'true')
    expect(screen.getByTestId('map-node-2')).toHaveAttribute('data-selectable', 'false')
  })

  it('calls move API when clicking a selectable node', async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 })) // move
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(sampleSnapshot()), { status: 200 }),
    ) // refresh
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot()}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByTestId('map-node-1'))
    await waitFor(() => {
      const call = fetchMock.mock.calls.find((args) =>
        String(args[0]).includes('/runs/current/move'),
      )
      expect(call).toBeDefined()
    })
    const moveCall = fetchMock.mock.calls.find((args) =>
      String(args[0]).includes('/runs/current/move'),
    )!
    expect((moveCall[1] as RequestInit).body).toContain('"nodeId":1')
  })

  it('opens in-game menu when gear icon is clicked', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot()}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: 'メニュー' }))
    expect(screen.getByRole('dialog', { name: 'ゲームメニュー' })).toBeInTheDocument()
  })

  it('shows TopBar with HP/Gold', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot()}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByText('HP 80/80')).toBeDefined()
    expect(screen.getByText('Gold 99')).toBeDefined()
  })

  it('overlays BattleOverlay when run has activeBattle', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeBattle: sampleBattle() })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByText('Jaw Worm')).toBeDefined()
    expect(screen.getByText('勝利')).toBeDefined()
  })

  it('overlays RewardPopup when run has activeReward', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeReward: sampleReward() })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByText('報酬')).toBeDefined()
    expect(screen.getByText('＋ 15 Gold')).toBeDefined()
  })

  it('blocks node selection while battle is active', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeBattle: sampleBattle() })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByTestId('map-node-1')).toHaveAttribute('data-selectable', 'false')
  })

  it('renders MerchantScreen when snapshot has activeMerchant', () => {
    const inventory = {
      cards: [],
      relics: [],
      potions: [],
      discardSlotUsed: false,
      discardPrice: 75,
      leftSoFar: false,
    }
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeMerchant: inventory })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByText(/商人/)).toBeInTheDocument()
  })

  it('renders EventScreen when snapshot has activeEvent', () => {
    const event = {
      eventId: 'blessing_fountain',
      name: 'Blessing Fountain',
      description: 'A mystical fountain offers its gifts.',
      choices: [
        { label: 'Drink', conditionSummary: null, conditionMet: true },
        { label: 'Walk by', conditionSummary: null, conditionMet: true },
      ],
      chosenIndex: null,
    }
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeEvent: event })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByText('Blessing Fountain')).toBeInTheDocument()
  })

  it('renders RestScreen when snapshot has activeRestPending', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeRestPending: true })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByText('休息所')).toBeInTheDocument()
  })
})
