import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountProvider } from '../context/AccountContext'
import { LoginScreen } from './LoginScreen'

const onLoggedIn = vi.fn()

function renderScreen() {
  return render(
    <AccountProvider>
      <LoginScreen onLoggedIn={onLoggedIn} />
    </AccountProvider>,
  )
}

describe('LoginScreen', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    localStorage.clear()
    onLoggedIn.mockClear()
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('renders both tabs', () => {
    renderScreen()
    expect(screen.getByRole('tab', { name: '新規作成' })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: '既存 ID で続行' })).toBeInTheDocument()
  })

  it('rejects empty id before sending request', () => {
    renderScreen()
    fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(screen.getByText(/入力してください/i)).toBeInTheDocument()
  })

  it('creates new account on success', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ id: 'alice', createdAt: '2026-04-20T12:00:00Z' }), { status: 201 }),
    )
    renderScreen()
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'alice' } })
    fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }))

    await waitFor(() => expect(onLoggedIn).toHaveBeenCalledWith('alice'))
    expect(localStorage.getItem('rcg.accountId')).toBe('alice')
  })

  it('shows conflict message on 409', async () => {
    fetchMock.mockResolvedValue(new Response('dup', { status: 409 }))
    renderScreen()
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'dup' } })
    fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }))

    await waitFor(() =>
      expect(screen.getByText(/すでに使われています/)).toBeInTheDocument(),
    )
    expect(onLoggedIn).not.toHaveBeenCalled()
  })

  it('logs in existing account when GET returns 200', async () => {
    fetchMock.mockResolvedValue(
      new Response(JSON.stringify({ id: 'bob', createdAt: '2026-04-20T12:00:00Z' }), { status: 200 }),
    )
    renderScreen()
    fireEvent.click(screen.getByRole('tab', { name: '既存 ID で続行' }))
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'bob' } })
    fireEvent.click(screen.getByRole('button', { name: 'ログイン' }))

    await waitFor(() => expect(onLoggedIn).toHaveBeenCalledWith('bob'))
  })

  it('shows not-found message on 404 existing flow', async () => {
    fetchMock.mockResolvedValue(new Response('missing', { status: 404 }))
    renderScreen()
    fireEvent.click(screen.getByRole('tab', { name: '既存 ID で続行' }))
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'ghost' } })
    fireEvent.click(screen.getByRole('button', { name: 'ログイン' }))

    await waitFor(() =>
      expect(screen.getByText(/登録されていません/)).toBeInTheDocument(),
    )
  })

  it('rejects id with slash in client-side validation', () => {
    renderScreen()
    fireEvent.change(screen.getByLabelText('アカウント ID'), { target: { value: 'bad/id' } })
    fireEvent.click(screen.getByRole('button', { name: 'アカウント作成' }))
    expect(fetchMock).not.toHaveBeenCalled()
    expect(screen.getByText(/使用できない文字/)).toBeInTheDocument()
  })
})
