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
})
