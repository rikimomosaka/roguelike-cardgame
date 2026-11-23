// src/Client/src/App.tsx
import { useEffect, useRef, useState, type ReactNode } from 'react'
import { getAccount } from './api/accounts'
import { ApiError } from './api/client'
import type { RunResultDto, RunSnapshotDto } from './api/types'
import { useAudio } from './audio'
import { BGM_TRACKS, selectSe } from './audio/audioManifest'
import { Button } from './components/Button'
import { useAccount } from './context/AccountContext'
import { ScreenTransition } from './presentation'
import { AchievementsScreen } from './screens/AchievementsScreen'
import { AudioLabScreen } from './screens/AudioLabScreen'
import { DevCardsScreen } from './screens/DevCardsScreen'
import { DevHomeScreen } from './screens/DevHomeScreen'
import { DevRelicsScreen } from './screens/DevRelicsScreen'
import { ContentWorkbenchScreen } from './screens/ContentWorkbenchScreen'
import { CharacterPickScreen } from './screens/CharacterPickScreen'
import { LoginScreen } from './screens/LoginScreen'
import { MainMenuScreen } from './screens/MainMenuScreen'
import { MapScreen } from './screens/MapScreen'
import { RunResultScreen } from './screens/RunResultScreen'
import { SettingsScreen } from './screens/SettingsScreen'

type Screen =
  | { kind: 'bootstrapping' }
  | { kind: 'login' }
  | { kind: 'main-menu'; hasCurrentRun?: boolean }
  | { kind: 'character-pick'; force: boolean }
  | { kind: 'settings' }
  | { kind: 'achievements' }
  | { kind: 'map'; snapshot: RunSnapshotDto }
  | { kind: 'run-result'; result: RunResultDto; snapshot?: RunSnapshotDto }
  | { kind: 'bootstrap-error'; message: string }
  | { kind: 'dev-home' }
  | { kind: 'dev-cards' }
  | { kind: 'dev-relics' }
  | { kind: 'dev-content'; initialKind?: string }
  | { kind: 'dev-audio' }

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
  const { playBgm, playSe, stopBgm } = useAudio()
  const [screen, setScreen] = useState<Screen>({ kind: 'bootstrapping' })
  const playedRunResultSoundRef = useRef<string | null>(null)

  // App owns top-level music. MapScreen takes over while a run is active,
  // because battle/shop/event visibility lives below this screen state.
  useEffect(() => {
    if (screen.kind === 'bootstrapping' || screen.kind === 'bootstrap-error') {
      stopBgm({ fadeMs: 250 })
      return
    }
    if (screen.kind === 'map') return

    playBgm(BGM_TRACKS.menu, { fadeMs: 750 })
    // Defeat can originate from battle, map events, or direct HP loss. Owning
    // the game-over stinger here guarantees every route plays it exactly once.
    if (screen.kind === 'run-result' && screen.result.outcome === 'GameOver') {
      const resultKey = `${screen.result.runId}:${screen.result.endedAtUtc}`
      if (playedRunResultSoundRef.current !== resultKey) {
        playedRunResultSoundRef.current = resultKey
        void playSe(selectSe('gameOver', resultKey))
      }
    }
  }, [playBgm, playSe, screen, stopBgm])

  // A quiet physical click gives every conventional control immediate
  // feedback. Cards opt out because their play/zone/combo cues are richer.
  useEffect(() => {
    const onClick = (event: MouseEvent) => {
      const target = event.target
      if (!(target instanceof Element)) return
      const control = target.closest(
        'button, a[href], input[type="button"], input[type="submit"], [role="button"]',
      ) as HTMLElement | null
      if (!control
          || control.matches(':disabled, [aria-disabled="true"]')
          || control.closest('.hand-card, [data-audio-ui="off"]')) return
      void playSe(selectSe('uiClick', `${control.textContent ?? control.getAttribute('aria-label') ?? ''}`))
    }
    document.addEventListener('click', onClick)
    return () => document.removeEventListener('click', onClick)
  }, [playSe])

  useEffect(() => {
    let cancelled = false
    async function bootstrap() {
      // DEV 専用 ?dev=... ショートカットは認証 / ラン状態に関係なく即座に dev 画面へ遷移する。
      const devParam = readDevParamOnce()
      if (devParam !== null) {
        if (cancelled) return
        if (devParam === 'audio') {
          setScreen({ kind: 'dev-audio' })
          return
        }
        const aliases: Record<string, string> = {
          cards: 'cards', relics: 'relics', potions: 'potions', characters: 'characters',
          enemies: 'enemies', units: 'units', encounters: 'encounters', events: 'events',
        }
        setScreen({ kind: 'dev-content', initialKind: aliases[devParam] ?? 'cards' })
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

  let content: ReactNode

  if (screen.kind === 'bootstrapping') {
    content = <main className="bootstrap"><p>起動中…</p></main>
  } else if (screen.kind === 'bootstrap-error') {
    content = (
      <main className="bootstrap-error">
        <p>{screen.message}</p>
        <Button onClick={() => setScreen({ kind: 'bootstrapping' })}>再試行</Button>
      </main>
    )
  } else if (screen.kind === 'login') {
    content = <LoginScreen onLoggedIn={() => setScreen({ kind: 'main-menu' })} />
  } else if (screen.kind === 'main-menu') {
    content = (
      <MainMenuScreen
        hasCurrentRun={screen.hasCurrentRun}
        onOpenSettings={() => setScreen({ kind: 'settings' })}
        onLogout={() => { logout(); setScreen({ kind: 'login' }) }}
        onStartRun={(snap) => setScreen({ kind: 'map', snapshot: snap })}
        onPickCharacter={(force) => setScreen({ kind: 'character-pick', force })}
        onAchievements={() => setScreen({ kind: 'achievements' })}
      />
    )
  } else if (screen.kind === 'character-pick') {
    content = (
      <CharacterPickScreen
        accountId={accountId!}
        force={screen.force}
        selectionLimit={1}
        onStarted={(snapshot) => setScreen({ kind: 'map', snapshot })}
        onBack={() => setScreen({ kind: 'main-menu' })}
      />
    )
  } else if (screen.kind === 'achievements') {
    content = (
      <AchievementsScreen
        accountId={accountId!}
        onBack={() => setScreen({ kind: 'main-menu' })}
      />
    )
  } else if (screen.kind === 'map') {
    content = (
      <MapScreen
        snapshot={screen.snapshot}
        onExitToMenu={() => setScreen({ kind: 'main-menu' })}
        onAbandon={(r, snapshot) => setScreen(r ? { kind: 'run-result', result: r, snapshot } : { kind: 'main-menu', hasCurrentRun: false })}
        onRunFinished={(r, snapshot) => setScreen({ kind: 'run-result', result: r, snapshot })}
      />
    )
  } else if (screen.kind === 'run-result') {
    content = (
      <RunResultScreen
        result={screen.result}
        snapshot={screen.snapshot}
        onReturnToMenu={() => setScreen({ kind: 'main-menu', hasCurrentRun: false })}
      />
    )
  } else if (import.meta.env.DEV && screen.kind === 'dev-home') {
    // DEV ガード: import.meta.env.DEV が false なら本番 build で dev branch は DCE される。
    content = (
      <DevHomeScreen
        onOpenCards={() => setScreen({ kind: 'dev-cards' })}
        onOpenRelics={() => setScreen({ kind: 'dev-relics' })}
        onClose={() => setScreen({ kind: 'main-menu' })}
      />
    )
  } else if (import.meta.env.DEV && screen.kind === 'dev-cards') {
    content = <DevCardsScreen onBack={() => setScreen({ kind: 'dev-home' })} />
  } else if (import.meta.env.DEV && screen.kind === 'dev-relics') {
    content = <DevRelicsScreen onBack={() => setScreen({ kind: 'dev-home' })} />
  } else if (import.meta.env.DEV && screen.kind === 'dev-content') {
    content = (
      <ContentWorkbenchScreen
        initialKind={screen.initialKind}
        onClose={() => window.location.assign(window.location.pathname)}
      />
    )
  } else if (import.meta.env.DEV && screen.kind === 'dev-audio') {
    content = (
      <AudioLabScreen
        onBack={() => window.location.assign(window.location.pathname)}
      />
    )
  } else {
    content = <SettingsScreen onBack={() => setScreen({ kind: 'main-menu' })} />
  }

  return (
    <ScreenTransition transitionKey={screen.kind}>
      {content}
    </ScreenTransition>
  )
}
