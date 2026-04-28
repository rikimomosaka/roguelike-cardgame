import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useRef } from 'react'
import { AccountProvider } from '../context/AccountContext'
import type { RunResultDto } from '../api/types'
import { InGameMenuScreen } from './InGameMenuScreen'

function Wrapper({ onExitToMenu, onAbandon, onClose, requireExitConfirm }: {
  onExitToMenu: () => void
  onAbandon: (result: RunResultDto | null) => void
  onClose: () => void
  requireExitConfirm?: boolean
}) {
  const ref = useRef(performance.now())
  return (
    <AccountProvider>
      <InGameMenuScreen
        onClose={onClose}
        onExitToMenu={onExitToMenu}
        onAbandon={onAbandon}
        elapsedSecondsRef={ref}
        requireExitConfirm={requireExitConfirm}
      />
    </AccountProvider>
  )
}

describe('InGameMenuScreen', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    localStorage.setItem('rcg.accountId', 'alice')
    fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    vi.stubGlobal('fetch', fetchMock)
  })
  afterEach(() => vi.unstubAllGlobals())

  it('calls onClose when continue is clicked', () => {
    const onClose = vi.fn()
    render(<Wrapper onExitToMenu={() => {}} onAbandon={() => {}} onClose={onClose} />)
    fireEvent.click(screen.getByRole('button', { name: '続ける' }))
    expect(onClose).toHaveBeenCalled()
  })

  it('sends heartbeat and calls onExitToMenu when exit clicked', async () => {
    const onExit = vi.fn()
    render(<Wrapper onExitToMenu={onExit} onAbandon={() => {}} onClose={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'メニューに戻る' }))
    await waitFor(() => {
      const urls = fetchMock.mock.calls.map((c) => c[0] as string)
      expect(urls.some((u) => u.includes('/runs/current/heartbeat'))).toBe(true)
    })
    await waitFor(() => expect(onExit).toHaveBeenCalled())
  })

  it('asks confirmation before abandon', async () => {
    const onAbandon = vi.fn()
    render(<Wrapper onExitToMenu={() => {}} onAbandon={onAbandon} onClose={() => {}} />)
    fireEvent.click(screen.getByRole('button', { name: 'あきらめる' }))
    expect(screen.getByText(/本当にこのランを放棄/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '放棄する' }))
    await waitFor(() => {
      const urls = fetchMock.mock.calls.map((c) => c[0] as string)
      expect(urls.some((u) => u.includes('/runs/current/abandon'))).toBe(true)
    })
    await waitFor(() => expect(onAbandon).toHaveBeenCalled())
  })

  it('S キーで設定画面へ遷移する', () => {
    render(<Wrapper onExitToMenu={() => {}} onAbandon={() => {}} onClose={() => {}} />)
    fireEvent.keyDown(window, { key: 's' })
    expect(screen.getByText('設 定')).toBeInTheDocument()
  })

  it('Q キーで exit を呼ぶ', async () => {
    const onExit = vi.fn()
    render(<Wrapper onExitToMenu={onExit} onAbandon={() => {}} onClose={() => {}} />)
    fireEvent.keyDown(window, { key: 'q' })
    await waitFor(() => expect(onExit).toHaveBeenCalled())
  })

  it('X キーで放棄確認ダイアログを開く', () => {
    render(<Wrapper onExitToMenu={() => {}} onAbandon={() => {}} onClose={() => {}} />)
    fireEvent.keyDown(window, { key: 'x' })
    expect(screen.getByText(/本当にこのランを放棄/)).toBeInTheDocument()
  })

  it('requireExitConfirm=true のとき メニューに戻る で警告 confirm を出す', () => {
    const onExit = vi.fn()
    render(
      <Wrapper
        onExitToMenu={onExit}
        onAbandon={() => {}}
        onClose={() => {}}
        requireExitConfirm
      />
    )
    fireEvent.click(screen.getByRole('button', { name: 'メニューに戻る' }))
    expect(screen.getByText(/戦闘進行は/)).toBeInTheDocument()
    expect(onExit).not.toHaveBeenCalled()
  })

  it('requireExitConfirm: confirm-exit でキャンセルすると main に戻る', () => {
    render(
      <Wrapper
        onExitToMenu={() => {}}
        onAbandon={() => {}}
        onClose={() => {}}
        requireExitConfirm
      />
    )
    fireEvent.click(screen.getByRole('button', { name: 'メニューに戻る' }))
    fireEvent.click(screen.getByRole('button', { name: 'キャンセル' }))
    // main に戻ると「あきらめる」ボタンが再度見える
    expect(screen.getByRole('button', { name: 'あきらめる' })).toBeInTheDocument()
  })

  it('requireExitConfirm: confirm-exit の OK で heartbeat + onExitToMenu', async () => {
    const onExit = vi.fn()
    render(
      <Wrapper
        onExitToMenu={onExit}
        onAbandon={() => {}}
        onClose={() => {}}
        requireExitConfirm
      />
    )
    fireEvent.click(screen.getByRole('button', { name: 'メニューに戻る' }))
    fireEvent.click(screen.getByRole('button', { name: 'タイトルへ戻る' }))
    await waitFor(() => {
      const urls = fetchMock.mock.calls.map((c) => c[0] as string)
      expect(urls.some((u) => u.includes('/runs/current/heartbeat'))).toBe(true)
    })
    await waitFor(() => expect(onExit).toHaveBeenCalled())
  })

  it('requireExitConfirm: Q ホットキーも confirm-exit に分岐する', () => {
    const onExit = vi.fn()
    render(
      <Wrapper
        onExitToMenu={onExit}
        onAbandon={() => {}}
        onClose={() => {}}
        requireExitConfirm
      />
    )
    fireEvent.keyDown(window, { key: 'q' })
    expect(screen.getByText(/戦闘進行は/)).toBeInTheDocument()
    expect(onExit).not.toHaveBeenCalled()
  })
})
