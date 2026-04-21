// src/Client/src/screens/MainMenuScreen.tsx
import { useEffect, useState } from 'react'
import { getLatestRun } from '../api/runs'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'

type Props = {
  onOpenSettings: () => void
  onLogout: () => void
}

type ComingSoonKind = 'single' | 'multi' | 'achievements' | 'quit' | null

export function MainMenuScreen({ onOpenSettings, onLogout }: Props) {
  const { accountId } = useAccount()
  const [hasRun, setHasRun] = useState<boolean>(false)
  const [dialog, setDialog] = useState<ComingSoonKind>(null)

  useEffect(() => {
    if (!accountId) return
    let cancelled = false
    getLatestRun(accountId)
      .then((run) => { if (!cancelled) setHasRun(run !== null) })
      .catch(() => { /* ignore: UI に hasRun=false のまま */ })
    return () => { cancelled = true }
  }, [accountId])

  function showSoon(kind: ComingSoonKind) {
    setDialog(kind)
  }

  return (
    <main className="main-menu">
      <header className="main-menu__header">
        <span className="main-menu__account">{accountId}</span>
        <button className="btn btn--secondary" onClick={onLogout}>ログアウト</button>
      </header>

      <nav className="main-menu__buttons">
        <Button onClick={() => showSoon('single')}>シングルプレイ</Button>
        <Button onClick={() => showSoon('multi')}>マルチプレイ</Button>
        <Button onClick={onOpenSettings}>設定</Button>
        <Button onClick={() => showSoon('achievements')}>実績</Button>
        <Button variant="danger" onClick={() => showSoon('quit')}>終了</Button>
      </nav>

      {hasRun && <p className="main-menu__badge">保存済みラン有り</p>}

      {dialog && (
        <div role="dialog" aria-label="準備中" className="main-menu__dialog">
          <p>準備中です。</p>
          {dialog === 'quit' && <p>このタブを閉じてください。</p>}
          <Button variant="secondary" onClick={() => setDialog(null)}>閉じる</Button>
        </div>
      )}
    </main>
  )
}
