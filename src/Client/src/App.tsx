// src/Client/src/App.tsx
import { useEffect, useState } from 'react'
import { getAccount } from './api/accounts'
import { ApiError } from './api/client'
import { Button } from './components/Button'
import { useAccount } from './context/AccountContext'
import { LoginScreen } from './screens/LoginScreen'
import { MainMenuScreen } from './screens/MainMenuScreen'
import { SettingsScreen } from './screens/SettingsScreen'

type Screen =
  | { kind: 'bootstrapping' }
  | { kind: 'login' }
  | { kind: 'main-menu' }
  | { kind: 'settings' }
  | { kind: 'bootstrap-error'; message: string }

export default function App() {
  const { accountId, logout } = useAccount()
  const [screen, setScreen] = useState<Screen>({ kind: 'bootstrapping' })

  useEffect(() => {
    let cancelled = false
    async function bootstrap() {
      if (!accountId) {
        if (!cancelled) setScreen({ kind: 'login' })
        return
      }
      try {
        await getAccount(accountId)
        if (!cancelled) setScreen({ kind: 'main-menu' })
      } catch (e) {
        if (cancelled) return
        if (e instanceof ApiError && e.status === 404) {
          logout()
          setScreen({ kind: 'login' })
        } else {
          setScreen({ kind: 'bootstrap-error', message: 'サーバに接続できませんでした。' })
        }
      }
    }
    void bootstrap()
    return () => { cancelled = true }
  }, [accountId, logout])

  if (screen.kind === 'bootstrapping') {
    return <main className="bootstrap"><p>起動中…</p></main>
  }
  if (screen.kind === 'bootstrap-error') {
    return (
      <main className="bootstrap-error">
        <p>{screen.message}</p>
        <Button onClick={() => setScreen({ kind: 'bootstrapping' })}>再試行</Button>
      </main>
    )
  }
  if (screen.kind === 'login') {
    return <LoginScreen onLoggedIn={() => setScreen({ kind: 'main-menu' })} />
  }
  if (screen.kind === 'main-menu') {
    return (
      <MainMenuScreen
        onOpenSettings={() => setScreen({ kind: 'settings' })}
        onLogout={() => { logout(); setScreen({ kind: 'login' }) }}
      />
    )
  }
  return <SettingsScreen onBack={() => setScreen({ kind: 'main-menu' })} />
}
