import { useEffect, useState } from 'react'
import { getCurrentRun, startNewRun } from '../api/runs'
import type { RunSnapshotDto } from '../api/types'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'
import './MainMenuScreen.css'

type Props = {
  onOpenSettings: () => void
  onLogout: () => void
  onStartRun?: (snapshot: RunSnapshotDto) => void
  hasCurrentRun?: boolean
  onAchievements: () => void
}

type ComingSoonKind = 'multi' | 'quit' | null

export function MainMenuScreen({ onOpenSettings, onLogout, onStartRun, hasCurrentRun, onAchievements }: Props) {
  const { accountId } = useAccount()
  const [snapshot, setSnapshot] = useState<RunSnapshotDto | null>(null)
  const [dialog, setDialog] = useState<ComingSoonKind>(null)
  const [singleDialog, setSingleDialog] = useState(false)
  const [pending, setPending] = useState(false)

  useEffect(() => {
    if (!accountId) return
    let cancelled = false
    getCurrentRun(accountId)
      .then((snap) => { if (!cancelled) setSnapshot(snap) })
      .catch(() => { /* hasRun=false のまま */ })
    return () => { cancelled = true }
  }, [accountId])

  const effectiveHasRun =
    hasCurrentRun ?? (snapshot !== null && snapshot.run.progress === 'InProgress')

  async function startFresh(force: boolean) {
    if (!accountId || pending) return
    setPending(true)
    try {
      const snap = await startNewRun(accountId, force)
      onStartRun?.(snap)
    } finally {
      setPending(false)
      setSingleDialog(false)
    }
  }

  function handleSingle() {
    if (effectiveHasRun && snapshot) {
      setSingleDialog(true)
    } else {
      void startFresh(false)
    }
  }

  function continueRun() {
    if (snapshot) onStartRun?.(snapshot)
    setSingleDialog(false)
  }

  return (
    <main className="main-menu">
      <div className="main-menu__pattern" aria-hidden="true" />

      <header className="main-menu__header">
        <span>
          <span className="main-menu__account-mark" aria-hidden="true">▸</span>
          <span className="main-menu__account">{accountId}</span>
        </span>
        {effectiveHasRun && <p className="main-menu__badge">保存済みラン有り</p>}
      </header>

      <div className="main-menu__center">
        <div className="main-menu__title-block">
          <div className="main-menu__ornament-top" aria-hidden="true">✦ ✦ ✦</div>
          <h1 className="main-menu__title">ROGUELIKE</h1>
          <div className="main-menu__subtitle">CARD GAME</div>
        </div>

        <div className="main-menu__divider" aria-hidden="true" />

        <nav className="main-menu__buttons">
          <button
            type="button"
            className="main-menu__btn main-menu__btn--primary"
            onClick={handleSingle}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            シングルプレイ
          </button>
          <button
            type="button"
            className="main-menu__btn"
            onClick={() => setDialog('multi')}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            マルチプレイ
          </button>
          <button
            type="button"
            className="main-menu__btn"
            onClick={onOpenSettings}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            設定
          </button>
          <button
            type="button"
            className="main-menu__btn"
            onClick={onAchievements}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            実績
          </button>
          <button
            type="button"
            className="main-menu__btn main-menu__btn--danger"
            onClick={() => setDialog('quit')}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            終了
          </button>
        </nav>
      </div>

      <div className="main-menu__footer">
        <span className="main-menu__footer-version">v0.8.0 · PHASE 08</span>
        <button type="button" className="main-menu__logout" onClick={onLogout}>
          ログアウト<span aria-hidden="true"> ▸</span>
        </button>
      </div>

      {dialog && (
        <div role="dialog" aria-label="準備中" className="main-menu__dialog">
          <p>準備中です。</p>
          {dialog === 'quit' && <p>このタブを閉じてください。</p>}
          <Button variant="secondary" onClick={() => setDialog(null)}>閉じる</Button>
        </div>
      )}

      {singleDialog && (
        <div role="dialog" aria-label="シングルプレイ" className="main-menu__dialog">
          <p>進行中のランがあります。どうしますか？</p>
          <Button onClick={continueRun}>続きから</Button>
          <Button variant="danger" onClick={() => void startFresh(true)}>新規で上書き</Button>
          <Button variant="secondary" onClick={() => setSingleDialog(false)}>キャンセル</Button>
        </div>
      )}
    </main>
  )
}
