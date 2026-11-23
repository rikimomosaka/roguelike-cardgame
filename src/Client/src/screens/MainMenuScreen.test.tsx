// src/Client/src/screens/MainMenuScreen.test.tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RunSnapshotDto } from '../api/types'
import { AccountProvider } from '../context/AccountContext'
import { MainMenuScreen } from './MainMenuScreen'

function renderScreen(handlers: {
  onOpenSettings?: () => void
  onLogout?: () => void
  onAchievements?: () => void
  onStartRun?: (snapshot: RunSnapshotDto) => void
  onPickCharacter?: (force: boolean) => void
} = {}) {
  localStorage.setItem('rcg.accountId', 'alice')
  return render(
    <AccountProvider>
      <MainMenuScreen
        onOpenSettings={handlers.onOpenSettings ?? (() => {})}
        onLogout={handlers.onLogout ?? (() => {})}
        onAchievements={handlers.onAchievements ?? (() => {})}
        onStartRun={handlers.onStartRun}
        onPickCharacter={handlers.onPickCharacter}
      />
    </AccountProvider>,
  )
}

describe('MainMenuScreen', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    localStorage.clear()
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('renders 5 menu buttons and current account id', async () => {
    renderScreen()
    expect(screen.getByText('alice')).toBeInTheDocument()
    for (const label of ['はじめから', 'ソロ', 'マルチ', '実績', '設定']) {
      expect(screen.getByRole('button', { name: label })).toBeInTheDocument()
    }
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
  })

  it('calls onOpenSettings when settings button clicked', () => {
    const onOpenSettings = vi.fn()
    renderScreen({ onOpenSettings })
    fireEvent.click(screen.getByRole('button', { name: '設定' }))
    expect(onOpenSettings).toHaveBeenCalled()
  })

  it('shows coming-soon dialog for multiplayer / achievements', async () => {
    renderScreen()
    fireEvent.click(screen.getByRole('button', { name: 'マルチ' }))
    expect(await screen.findByText(/準備中/)).toBeInTheDocument()
  })

  it('calls onLogout from logout button', () => {
    const onLogout = vi.fn()
    renderScreen({ onLogout })
    fireEvent.click(screen.getByRole('button', { name: 'ログアウト' }))
    expect(onLogout).toHaveBeenCalled()
  })

  it('shows confirm dialog when single-play clicked with existing InProgress run', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          run: { schemaVersion: 2, progress: 'InProgress', currentNodeId: 0 },
          map: { startNodeId: 0, bossNodeId: 1, nodes: [] },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    renderScreen()
    await waitFor(() => expect(screen.getByText('保存済みラン有り')).toBeInTheDocument())

    fireEvent.click(screen.getByRole('button', { name: 'ソロ' }))
    expect(await screen.findByText(/進行中のランがあります/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '続きから' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '新規で上書き' })).toBeInTheDocument()
  })

  it('opens the character picker without overwriting when no save exists', async () => {
    const onPickCharacter = vi.fn()
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    renderScreen({ onPickCharacter })
    fireEvent.click(await screen.findByRole('button', { name: 'ソロ' }))
    expect(onPickCharacter).toHaveBeenCalledWith(false)
    expect(fetchMock).toHaveBeenCalledTimes(1)
  })

  it('進行中ランのAct・HP・ゴールド・デッキ情報を表示する', async () => {
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({
      run: {
        schemaVersion: 8, progress: 'InProgress', currentAct: 2, currentNodeId: 7,
        characterId: 'guardian_mireille', currentHp: 34, maxHp: 72, gold: 123,
        deck: [{ id: 'basic_attack', upgraded: false }], playSeconds: 125,
      },
      map: { startNodeId: 0, bossNodeId: 9, nodes: [{ id: 7, row: 6, column: 0, kind: 'Rest', outgoingNodeIds: [] }] },
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    renderScreen()
    const card = await screen.findByRole('region', { name: '進行中のラン' })
    expect(card).toHaveTextContent('ACT 2')
    expect(card).toHaveTextContent('FLOOR 6')
    expect(card).toHaveTextContent('HP 34/72')
    expect(card).toHaveTextContent('GOLD 123')
    expect(card).toHaveTextContent('TIME 02:05')
  })

  it('keeps continuation unchanged and sends overwrite choice to the picker', async () => {
    const savedSnapshot = {
      run: { schemaVersion: 8, progress: 'InProgress', currentNodeId: 0 },
      map: { startNodeId: 0, bossNodeId: 1, nodes: [] },
    } as unknown as RunSnapshotDto
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(savedSnapshot), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    const onStartRun = vi.fn()
    const onPickCharacter = vi.fn()
    renderScreen({ onStartRun, onPickCharacter })
    await screen.findByText('保存済みラン有り')

    fireEvent.click(screen.getByRole('button', { name: 'つづきから' }))
    expect(onStartRun).toHaveBeenCalledWith(savedSnapshot)

    fireEvent.click(screen.getByRole('button', { name: 'ソロ' }))
    fireEvent.click(await screen.findByRole('button', { name: '新規で上書き' }))
    expect(onPickCharacter).toHaveBeenCalledWith(true)
  })

  it('hides badge when hasCurrentRun is explicitly false', async () => {
    // Even if /runs/current returns a snapshot, hasCurrentRun={false} suppresses badge.
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          run: { schemaVersion: 2, progress: 'InProgress', currentNodeId: 0 },
          map: { startNodeId: 0, bossNodeId: 1, nodes: [] },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    localStorage.setItem('rcg.accountId', 'alice')
    render(
      <AccountProvider>
        <MainMenuScreen
          onOpenSettings={() => {}}
          onLogout={() => {}}
          onAchievements={() => {}}
          hasCurrentRun={false}
        />
      </AccountProvider>,
    )
    // Wait for the fetch to settle.
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(screen.queryByText('保存済みラン有り')).toBeNull()
  })

  it('shows badge when hasCurrentRun is explicitly true', async () => {
    // Even with no snapshot (204), hasCurrentRun={true} shows the badge.
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    localStorage.setItem('rcg.accountId', 'alice')
    render(
      <AccountProvider>
        <MainMenuScreen
          onOpenSettings={() => {}}
          onLogout={() => {}}
          onAchievements={() => {}}
          hasCurrentRun={true}
        />
      </AccountProvider>,
    )
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(screen.getByText('保存済みラン有り')).toBeInTheDocument()
  })
})

describe('MainMenuScreen 実績ボタン', () => {
  let fetchMock: ReturnType<typeof vi.fn>
  beforeEach(() => {
    localStorage.clear()
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
    localStorage.setItem('rcg.accountId', 'alice')
  })
  afterEach(() => vi.unstubAllGlobals())

  it('calls onAchievements when 実績 button clicked', () => {
    const onAchievements = vi.fn()
    render(
      <AccountProvider>
        <MainMenuScreen
          onOpenSettings={() => {}}
          onLogout={() => {}}
          onAchievements={onAchievements}
        />
      </AccountProvider>,
    )
    fireEvent.click(screen.getByRole('button', { name: '実績' }))
    expect(onAchievements).toHaveBeenCalled()
  })
})
