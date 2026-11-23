import { useEffect, useState } from 'react'
import { getCurrentRun } from '../api/runs'
import type { RunSnapshotDto } from '../api/types'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'
import './MainMenuScreen.css'

type Props = {
  onOpenSettings: () => void
  onLogout: () => void
  onStartRun?: (snapshot: RunSnapshotDto) => void | Promise<void>
  onPickCharacter?: (force: boolean) => void
  hasCurrentRun?: boolean
  onAchievements: () => void
}

type ComingSoonKind = 'multi' | null

export function MainMenuScreen({
  onOpenSettings,
  onLogout,
  onStartRun,
  onPickCharacter,
  hasCurrentRun,
  onAchievements,
}: Props) {
  const { accountId } = useAccount()
  const [snapshot, setSnapshot] = useState<RunSnapshotDto | null>(null)
  const [dialog, setDialog] = useState<ComingSoonKind>(null)
  const [singleDialog, setSingleDialog] = useState(false)
  const [runLookup, setRunLookup] = useState<'loading' | 'ready' | 'error'>('loading')
  const [transitioning, setTransitioning] = useState(false)

  useEffect(() => {
    if (!accountId) return
    let cancelled = false
    getCurrentRun(accountId)
      .then((snap) => {
        if (cancelled) return
        setSnapshot(snap)
        setRunLookup('ready')
      })
      .catch(() => { if (!cancelled) setRunLookup('error') })
    return () => { cancelled = true }
  }, [accountId])

  const effectiveHasRun =
    hasCurrentRun ?? (snapshot !== null && snapshot.run.progress === 'InProgress')

  function openCharacterPick(force: boolean) {
    setSingleDialog(false)
    onPickCharacter?.(force)
  }

  function handleSingle() {
    if (effectiveHasRun && snapshot) {
      setSingleDialog(true)
    } else {
      openCharacterPick(false)
    }
  }

  async function continueRun() {
    if (!snapshot || transitioning) return
    setTransitioning(true)
    setSingleDialog(false)
    try {
      await onStartRun?.(snapshot)
    } finally {
      setTransitioning(false)
    }
  }

  const currentNode = snapshot?.map.nodes.find(node => node.id === snapshot.run.currentNodeId)
  const hpPercent = snapshot?.run.maxHp
    ? Math.max(0, Math.min(100, snapshot.run.currentHp / snapshot.run.maxHp * 100))
    : 0

  return (
    <main className="main-menu">
      <div className="main-menu__pattern" aria-hidden="true" />

      <header className="main-menu__header">
        <span>
          <span className="main-menu__account-mark" aria-hidden="true">▸</span>
          <span className="main-menu__account">{accountId}</span>
        </span>
        <div className="main-menu__save-state" aria-live="polite">
          {runLookup === 'loading' && <span>RUN DATA 確認中…</span>}
          {runLookup === 'error' && <span>RUN DATAを確認できません</span>}
          {effectiveHasRun && <p className="main-menu__badge">保存済みラン有り</p>}
        </div>
      </header>

      <div className="main-menu__center">
        <div className="main-menu__title-block">
          <div className="main-menu__ornament-top" aria-hidden="true">✦ ✦ ✦</div>
          <h1 className="main-menu__title">ROGUELIKE</h1>
          <div className="main-menu__subtitle">CARD GAME</div>
        </div>

        <div className="main-menu__divider" aria-hidden="true" />

        {effectiveHasRun && snapshot && (
          <section className="main-menu__run-card" aria-label="進行中のラン">
            <div className="main-menu__run-head">
              <strong>ACT {snapshot.run.currentAct ?? 1}</strong>
              <span>FLOOR {currentNode?.row ?? snapshot.run.currentNodeId ?? 0}</span>
              <span>{snapshot.run.characterId || 'PARTY'}</span>
            </div>
            <div className="main-menu__run-stats">
              <span>HP {snapshot.run.currentHp ?? 0}/{snapshot.run.maxHp ?? 0}</span>
              <span>GOLD {snapshot.run.gold ?? 0}</span>
              <span>DECK {snapshot.run.deck?.length ?? 0}</span>
              <span>TIME {formatPlayTime(snapshot.run.playSeconds ?? 0)}</span>
            </div>
            <span className="main-menu__run-hp" aria-hidden="true">
              <span style={{ width: `${hpPercent}%` }} />
            </span>
          </section>
        )}

        <nav className="main-menu__buttons">
          <button
            type="button"
            className="main-menu__btn main-menu__btn--primary"
            onClick={effectiveHasRun ? () => void continueRun() : () => openCharacterPick(false)}
            disabled={transitioning}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            {transitioning ? '読み込み中…' : effectiveHasRun ? 'つづきから' : 'はじめから'}
          </button>
          <button
            type="button"
            className="main-menu__btn"
            onClick={handleSingle}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            ソロ
          </button>
          <button
            type="button"
            className="main-menu__btn"
            onClick={() => setDialog('multi')}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            マルチ
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
            className="main-menu__btn"
            onClick={onOpenSettings}
          >
            <span className="main-menu__btn-mark" aria-hidden="true">▸</span>
            設定
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
          <Button variant="secondary" onClick={() => setDialog(null)}>閉じる</Button>
        </div>
      )}

      {singleDialog && (
        <div role="dialog" aria-label="ソロプレイ" className="main-menu__dialog">
          <p>進行中のランがあります。どうしますか？</p>
          <Button onClick={() => void continueRun()} disabled={transitioning}>続きから</Button>
          <Button variant="danger" onClick={() => openCharacterPick(true)}>新規で上書き</Button>
          <Button variant="secondary" onClick={() => setSingleDialog(false)}>キャンセル</Button>
        </div>
      )}
    </main>
  )
}

function formatPlayTime(totalSeconds: number): string {
  const minutes = Math.floor(Math.max(0, totalSeconds) / 60)
  const seconds = Math.max(0, totalSeconds) % 60
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}
