// src/Client/src/App.tsx
import { useEffect, useState } from 'react'
import { getAccount } from './api/accounts'
import { ApiError } from './api/client'
import type { RunResultDto, RunSnapshotDto } from './api/types'
import { Button } from './components/Button'
import { useAccount } from './context/AccountContext'
import { AchievementsScreen } from './screens/AchievementsScreen'
import { DevCardsScreen } from './screens/DevCardsScreen'
import { DevHomeScreen } from './screens/DevHomeScreen'
import { DevRelicsScreen } from './screens/DevRelicsScreen'
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
  | { kind: 'run-result'; result: RunResultDto; snapshot?: RunSnapshotDto }
  | { kind: 'bootstrap-error'; message: string }
  | { kind: 'dev-home' }
  | { kind: 'dev-cards' }
  | { kind: 'dev-relics' }

// Why: 3 段ゲートのうち UI ゲート。`import.meta.env.DEV` は本番ビルド時に false で
//   DCE 対象になるため、本番 bundle から DevHome/DevCards のコードが消える。
//   実際の route 判定は ?dev=1 / ?dev=cards の query param で行う。
function readDevParamOnce(): string | null {
  if (!import.meta.env.DEV) return null
  if (typeof window === 'undefined') return null
  try {
    const sp = new URLSearchParams(window.location.search)
    return sp.get('dev')
  } catch {
    return null
  }
}

export default function App() {
  const { accountId, logout } = useAccount()
  const [screen, setScreen] = useState<Screen>({ kind: 'bootstrapping' })

  useEffect(() => {
    let cancelled = false
    async function bootstrap() {
      // DEV 専用 ?dev=... ショートカットは認証 / ラン状態に関係なく即座に dev 画面へ遷移する。
      const devParam = readDevParamOnce()
      if (devParam !== null) {
        if (cancelled) return
        if (devParam === 'cards') {
          setScreen({ kind: 'dev-cards' })
        } else if (devParam === 'relics') {
          setScreen({ kind: 'dev-relics' })
        } else {
          setScreen({ kind: 'dev-home' })
        }
        return
      }
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
        onAbandon={(r, snapshot) => setScreen(r ? { kind: 'run-result', result: r, snapshot } : { kind: 'main-menu', hasCurrentRun: false })}
        onRunFinished={(r, snapshot) => setScreen({ kind: 'run-result', result: r, snapshot })}
      />
    )
  }
  if (screen.kind === 'run-result') {
    return (
      <RunResultScreen
        result={screen.result}
        snapshot={screen.snapshot}
        onReturnToMenu={() => setScreen({ kind: 'main-menu', hasCurrentRun: false })}
      />
    )
  }
  // DEV ガード: import.meta.env.DEV が false なら本番 build で dev branch は DCE される。
  if (import.meta.env.DEV && screen.kind === 'dev-home') {
    return (
      <DevHomeScreen
        onOpenCards={() => setScreen({ kind: 'dev-cards' })}
        onOpenRelics={() => setScreen({ kind: 'dev-relics' })}
        onClose={() => setScreen({ kind: 'main-menu' })}
      />
    )
  }
  if (import.meta.env.DEV && screen.kind === 'dev-cards') {
    return <DevCardsScreen onBack={() => setScreen({ kind: 'dev-home' })} />
  }
  if (import.meta.env.DEV && screen.kind === 'dev-relics') {
    return <DevRelicsScreen onBack={() => setScreen({ kind: 'dev-home' })} />
  }
  return <SettingsScreen onBack={() => setScreen({ kind: 'main-menu' })} />
}
