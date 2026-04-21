// src/Client/src/screens/MainMenuScreen.test.tsx
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountProvider } from '../context/AccountContext'
import { MainMenuScreen } from './MainMenuScreen'

function renderScreen(handlers: { onOpenSettings?: () => void; onLogout?: () => void } = {}) {
  localStorage.setItem('rcg.accountId', 'alice')
  return render(
    <AccountProvider>
      <MainMenuScreen
        onOpenSettings={handlers.onOpenSettings ?? (() => {})}
        onLogout={handlers.onLogout ?? (() => {})}
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
    for (const label of ['シングルプレイ', 'マルチプレイ', '設定', '実績', '終了']) {
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
    fireEvent.click(screen.getByRole('button', { name: 'マルチプレイ' }))
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

    fireEvent.click(screen.getByRole('button', { name: 'シングルプレイ' }))
    expect(await screen.findByText(/進行中のランがあります/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '続きから' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '新規で上書き' })).toBeInTheDocument()
  })

  it('starts new run directly when no InProgress save exists', async () => {
    const onStart = vi.fn()
    // /runs/current → 204 (no save)
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))
    // /runs/new → 200
    fetchMock.mockResolvedValueOnce(
      new Response(
        JSON.stringify({
          run: { schemaVersion: 2, currentNodeId: 0, progress: 'InProgress' },
          map: { startNodeId: 0, bossNodeId: 1, nodes: [] },
        }),
        { status: 200, headers: { 'Content-Type': 'application/json' } },
      ),
    )
    localStorage.setItem('rcg.accountId', 'alice')
    render(
      <AccountProvider>
        {/* @ts-expect-error — Task 18 adds onStartRun to Props */}
        <MainMenuScreen
          onOpenSettings={() => {}}
          onLogout={() => {}}
          onStartRun={onStart}
        />
      </AccountProvider>,
    )
    fireEvent.click(await screen.findByRole('button', { name: 'シングルプレイ' }))
    await waitFor(() => expect(onStart).toHaveBeenCalled())
  })
})
