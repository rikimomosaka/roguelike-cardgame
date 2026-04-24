// src/Client/src/App.tsx
import { useEffect, useState } from 'react'
import { getAccount } from './api/accounts'
import { ApiError } from './api/client'
import type { RunResultDto, RunSnapshotDto } from './api/types'
import { Button } from './components/Button'
import { TooltipHost } from './components/Tooltip'
import { useAccount } from './context/AccountContext'
import { AchievementsScreen } from './screens/AchievementsScreen'
import { BattleScreen } from './screens/BattleScreen'
import { LoginScreen } from './screens/LoginScreen'
import { MainMenuScreen } from './screens/MainMenuScreen'
import { MapScreen } from './screens/MapScreen'
import { RunResultScreen } from './screens/RunResultScreen'
import { SettingsScreen } from './screens/SettingsScreen'

type Screen =
  | { kind: 'bootstrapping' }
  | { kind: 'login' }
  | { kind: 'main-menu'; hasCurrentRun?: boolean }
  | { kind: 'settings' }
  | { kind: 'achievements' }
  | { kind: 'map'; snapshot: RunSnapshotDto }
  | { kind: 'run-result'; result: RunResultDto }
  | { kind: 'bootstrap-error'; message: string }

export default function App() {
  if (typeof window !== 'undefined' && window.location.search.includes('demo=battle')) {
    return (
      <TooltipHost>
        <BattleScreen />
      </TooltipHost>
    )
  }
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
        hasCurrentRun={screen.hasCurrentRun}
        onOpenSettings={() => setScreen({ kind: 'settings' })}
        onLogout={() => { logout(); setScreen({ kind: 'login' }) }}
        onStartRun={(snap) => setScreen({ kind: 'map', snapshot: snap })}
        onAchievements={() => setScreen({ kind: 'achievements' })}
      />
    )
  }
  if (screen.kind === 'achievements') {
    return (
      <AchievementsScreen
        accountId={accountId!}
        onBack={() => setScreen({ kind: 'main-menu' })}
      />
    )
  }
  if (screen.kind === 'map') {
    return (
      <MapScreen
        snapshot={screen.snapshot}
        onExitToMenu={() => setScreen({ kind: 'main-menu' })}
        onAbandon={(r) => setScreen(r ? { kind: 'run-result', result: r } : { kind: 'main-menu', hasCurrentRun: false })}
        onRunFinished={(r) => setScreen({ kind: 'run-result', result: r })}
      />
    )
  }
  if (screen.kind === 'run-result') {
    return (
      <RunResultScreen
        result={screen.result}
        onReturnToMenu={() => setScreen({ kind: 'main-menu', hasCurrentRun: false })}
      />
    )
  }
  return <SettingsScreen onBack={() => setScreen({ kind: 'main-menu' })} />
}
