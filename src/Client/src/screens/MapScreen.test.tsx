import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountProvider } from '../context/AccountContext'
import type { RunSnapshotDto } from '../api/types'
import { MapScreen } from './MapScreen'

function sampleSnapshot(): RunSnapshotDto {
  return {
    run: {
      schemaVersion: 3,
      currentAct: 1,
      currentNodeId: 0,
      visitedNodeIds: [0],
      unknownResolutions: {},
      characterId: 'ironclad',
      currentHp: 80, maxHp: 80, gold: 99,
      deck: [], relics: [], potions: [],
      potionSlotCount: 3,
      activeBattle: null,
      activeReward: null,
      playSeconds: 0,
      savedAtUtc: '2026-04-21T00:00:00Z',
      progress: 'InProgress',
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
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
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
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toContain('/runs/current/move')
    expect(init.body).toContain('"nodeId":1')
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
})
