import { createContext, useCallback, useContext, useState } from 'react'
import type { ReactNode } from 'react'

const STORAGE_KEY = 'rcg.accountId'

type AccountContextValue = {
  accountId: string | null
  login: (id: string) => void
  logout: () => void
}

const AccountContext = createContext<AccountContextValue | null>(null)

export function AccountProvider({ children }: { children: ReactNode }) {
  const [accountId, setAccountId] = useState<string | null>(() =>
    typeof window === 'undefined' ? null : window.localStorage.getItem(STORAGE_KEY),
  )

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

// This hook intentionally shares the provider's module so both use the same
// private context instance. It is not a React component Fast Refresh boundary.
// eslint-disable-next-line react-refresh/only-export-components
export function useAccount(): AccountContextValue {
  const ctx = useContext(AccountContext)
  if (!ctx) throw new Error('useAccount must be used within AccountProvider')
  return ctx
}
