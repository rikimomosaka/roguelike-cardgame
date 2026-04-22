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
      activeActStartRelicChoice: null,
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
    isBossReward: false,
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

  it('shows DEBUG -10HP button and fires onDebugDamage', () => {
    const onDebugDamage = vi.fn()
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot()}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
          onDebugDamage={onDebugDamage}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: /DEBUG -10HP/ }))
    expect(onDebugDamage).toHaveBeenCalled()
  })

  it('clicking 次の層へ on boss reward popup calls /reward/proceed and swaps snapshot', async () => {
    // Regression (previous): handleProceed must hit the server and apply the new snapshot
    // for boss reward (act transition), not just dismiss the popup locally.
    const act2Snap = sampleSnapshot({ currentAct: 2, activeReward: null })
    fetchMock.mockImplementation((input: unknown) => {
      const url = String(input)
      if (url.includes('/runs/current/reward/proceed')) {
        return Promise.resolve(new Response(JSON.stringify(act2Snap), { status: 200 }))
      }
      return Promise.resolve(new Response(null, { status: 204 }))
    })
    const bossReward = { ...sampleReward(), isBossReward: true }
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeReward: bossReward })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByText('次の層へ'))
    await waitFor(() => {
      const call = fetchMock.mock.calls.find((args) =>
        String(args[0]).includes('/runs/current/reward/proceed'),
      )
      expect(call).toBeDefined()
    })
  })

  it('clicking 進む on non-boss reward popup does NOT call server; closes locally and re-opens on tile re-click', async () => {
    // Regression (new Bug ②): non-boss reward must be dismissed locally so the tile is
    // re-enterable with the claim status intact. The server call should happen on move.
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeReward: sampleReward() })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByText('進む'))
    // Reward popup dismissed locally — no /reward/proceed request.
    const proceedCall = fetchMock.mock.calls.find((args) =>
      String(args[0]).includes('/runs/current/reward/proceed'),
    )
    expect(proceedCall).toBeUndefined()
    // Popup should be gone.
    await waitFor(() => {
      expect(screen.queryByText('＋ 15 Gold')).toBeNull()
    })
    // Re-click current tile (node 0 / start). Popup should re-appear.
    fireEvent.click(screen.getByTestId('map-node-0'))
    await waitFor(() => {
      expect(screen.getByText('＋ 15 Gold')).toBeDefined()
    })
  })

  it('renders ActStartRelicScreen when activeActStartRelicChoice is set', () => {
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ activeActStartRelicChoice: { relicIds: ['r1', 'r2', 'r3'] } })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByText(/層開始のレリックを選ぶ/)).toBeDefined()
  })

  it('at act start (Start not visited, choice null), Start is the only clickable tile', () => {
    // Regression: 層開始時はスタートマスが唯一クリック可能で、１マス目は選べない。
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ visitedNodeIds: [], currentNodeId: 0 })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    expect(screen.getByTestId('map-node-0')).toHaveAttribute('data-start-entry', 'true')
    expect(screen.getByTestId('map-node-1')).toHaveAttribute('data-selectable', 'false')
  })

  it('clicking Start when act-start is pending calls /act-start/enter', async () => {
    const entered = sampleSnapshot({
      visitedNodeIds: [],
      currentNodeId: 0,
      activeActStartRelicChoice: { relicIds: ['r1', 'r2', 'r3'] },
    })
    fetchMock.mockImplementation((input: unknown) => {
      const url = String(input)
      if (url.includes('/act-start/enter')) {
        return Promise.resolve(new Response(JSON.stringify(entered), { status: 200 }))
      }
      return Promise.resolve(new Response(null, { status: 204 }))
    })
    render(
      <AccountProvider>
        <MapScreen
          snapshot={sampleSnapshot({ visitedNodeIds: [], currentNodeId: 0 })}
          onExitToMenu={() => {}}
          onAbandon={() => {}}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByTestId('map-node-0'))
    await waitFor(() => {
      const call = fetchMock.mock.calls.find((args) =>
        String(args[0]).includes('/act-start/enter'),
      )
      expect(call).toBeDefined()
    })
    await waitFor(() => {
      expect(screen.getByText(/層開始のレリックを選ぶ/)).toBeDefined()
    })
  })
})
