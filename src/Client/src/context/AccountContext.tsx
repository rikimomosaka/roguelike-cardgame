import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import type { ReactNode } from 'react'

const STORAGE_KEY = 'rcg.accountId'

type AccountContextValue = {
  accountId: string | null
  login: (id: string) => void
  logout: () => void
}

const AccountContext = createContext<AccountContextValue | null>(null)

export function AccountProvider({ children }: { children: ReactNode }) {
  const [accountId, setAccountId] = useState<string | null>(null)

  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored) setAccountId(stored)
  }, [])

  const login = useCallback((id: string) => {
    localStorage.setItem(STORAGE_KEY, id)
    setAccountId(id)
  }, [])

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY)
    setAccountId(null)
  }, [])

  return (
    <AccountContext.Provider value={{ accountId, login, logout }}>
      {children}
    </AccountContext.Provider>
  )
}

export function useAccount(): AccountContextValue {
  const ctx = useContext(AccountContext)
  if (!ctx) throw new Error('useAccount must be used within AccountProvider')
  return ctx
}
