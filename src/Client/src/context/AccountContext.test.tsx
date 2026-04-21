import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { AccountProvider, useAccount } from './AccountContext'
import type { ReactNode } from 'react'

function wrapper({ children }: { children: ReactNode }) {
  return <AccountProvider>{children}</AccountProvider>
}

describe('AccountContext', () => {
  beforeEach(() => {
    localStorage.clear()
  })
  afterEach(() => {
    localStorage.clear()
  })

  it('starts with accountId = null when localStorage empty', () => {
    const { result } = renderHook(() => useAccount(), { wrapper })
    expect(result.current.accountId).toBeNull()
  })

  it('hydrates from localStorage on mount', () => {
    localStorage.setItem('rcg.accountId', 'alice')
    const { result } = renderHook(() => useAccount(), { wrapper })
    expect(result.current.accountId).toBe('alice')
  })

  it('login() sets state and writes localStorage', () => {
    const { result } = renderHook(() => useAccount(), { wrapper })
    act(() => {
      result.current.login('bob')
    })
    expect(result.current.accountId).toBe('bob')
    expect(localStorage.getItem('rcg.accountId')).toBe('bob')
  })

  it('logout() clears state and localStorage', () => {
    localStorage.setItem('rcg.accountId', 'carol')
    const { result } = renderHook(() => useAccount(), { wrapper })
    act(() => {
      result.current.logout()
    })
    expect(result.current.accountId).toBeNull()
    expect(localStorage.getItem('rcg.accountId')).toBeNull()
  })

  it('throws outside provider', () => {
    expect(() => renderHook(() => useAccount())).toThrow(/AccountProvider/)
  })
})
