import { useEffect, useState } from 'react'
import { getCurrentRun, startNewRun } from '../api/runs'
import type { RunSnapshotDto } from '../api/types'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'

type Props = {
  onOpenSettings: () => void
  onLogout: () => void
  onStartRun?: (snapshot: RunSnapshotDto) => void
  hasCurrentRun?: boolean
}

type ComingSoonKind = 'multi' | 'achievements' | 'quit' | null

export function MainMenuScreen({ onOpenSettings, onLogout, onStartRun, hasCurrentRun }: Props) {
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
      <header className="main-menu__header">
        <span className="main-menu__account">{accountId}</span>
        <button className="btn btn--secondary" onClick={onLogout}>ログアウト</button>
      </header>

      <nav className="main-menu__buttons">
        <Button onClick={handleSingle}>シングルプレイ</Button>
        <Button onClick={() => setDialog('multi')}>マルチプレイ</Button>
        <Button onClick={onOpenSettings}>設定</Button>
        <Button onClick={() => setDialog('achievements')}>実績</Button>
        <Button variant="danger" onClick={() => setDialog('quit')}>終了</Button>
      </nav>

      {effectiveHasRun && <p className="main-menu__badge">保存済みラン有り</p>}

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
