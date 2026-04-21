import { type RefObject, useState } from 'react'
import { abandonRun, heartbeat } from '../api/runs'
import { Button } from '../components/Button'
import { useAccount } from '../context/AccountContext'
import { SettingsScreen } from './SettingsScreen'

type Props = {
  onClose: () => void
  onExitToMenu: () => void
  onAbandon: () => void
  elapsedSecondsRef: RefObject<number>
}

type Mode = 'main' | 'settings' | 'confirm-abandon'

export function InGameMenuScreen({ onClose, onExitToMenu, onAbandon, elapsedSecondsRef }: Props) {
  const { accountId } = useAccount()
  const [mode, setMode] = useState<Mode>('main')
  const [busy, setBusy] = useState(false)

  function currentElapsed(): number {
    return Math.max(0, Math.floor((performance.now() - (elapsedSecondsRef.current ?? performance.now())) / 1000))
  }

  async function exit() {
    if (!accountId || busy) return
    setBusy(true)
    const e = currentElapsed()
    try {
      await heartbeat(accountId, e).catch(() => {})
      onExitToMenu()
    } finally {
      setBusy(false)
    }
  }

  async function confirmAbandon() {
    if (!accountId || busy) return
    setBusy(true)
    try {
      await abandonRun(accountId, currentElapsed()).catch(() => {})
      onAbandon()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="ゲームメニュー"
      className="in-game-menu"
    >
      <div className="in-game-menu__card">
        {mode === 'main' && (
          <>
            <Button onClick={onClose}>続ける</Button>
            <Button onClick={() => setMode('settings')}>音量設定</Button>
            <Button onClick={() => void exit()}>メニューに戻る</Button>
            <Button variant="danger" onClick={() => setMode('confirm-abandon')}>あきらめる</Button>
          </>
        )}
        {mode === 'settings' && (
          <SettingsScreen onBack={() => setMode('main')} />
        )}
        {mode === 'confirm-abandon' && (
          <>
            <p>本当にこのランを放棄しますか？</p>
            <Button variant="danger" onClick={() => void confirmAbandon()}>放棄する</Button>
            <Button variant="secondary" onClick={() => setMode('main')}>キャンセル</Button>
          </>
        )}
      </div>
    </div>
  )
}
