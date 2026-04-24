import { type RefObject, useEffect, useState } from 'react'
import { abandonRun, heartbeat } from '../api/runs'
import type { RunResultDto } from '../api/types'
import { Popup } from '../components/Popup'
import { useAccount } from '../context/AccountContext'
import { SettingsScreen } from './SettingsScreen'
import './InGameMenuScreen.css'

type Props = {
  onClose: () => void
  onExitToMenu: () => void
  onAbandon: (result: RunResultDto | null) => void
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
      // 送信後は起点をリセット。これを怠ると MapScreen unmount の cleanup heartbeat が
      // 同区間をもう一度加算してしまう (二重加算防止)。
      elapsedSecondsRef.current = performance.now()
      onExitToMenu()
    } finally {
      setBusy(false)
    }
  }

  async function confirmAbandon() {
    if (!accountId || busy) return
    setBusy(true)
    try {
      const result = await abandonRun(accountId, currentElapsed()).catch(() => null)
      // 同上 — abandon 後の cleanup heartbeat を 409 で空打ちさせないためリセット。
      elapsedSecondsRef.current = performance.now()
      onAbandon(result)
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    if (mode !== 'main') return
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.ctrlKey || e.metaKey || e.altKey) return
      const tag = (e.target as HTMLElement | null)?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return
      const key = e.key.toLowerCase()
      if (key === 's') {
        e.preventDefault()
        setMode('settings')
      } else if (key === 'q') {
        if (busy) return
        e.preventDefault()
        void exit()
      } else if (key === 'x') {
        e.preventDefault()
        setMode('confirm-abandon')
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [mode, busy, accountId])

  if (mode === 'settings') {
    return (
      <Popup
        open
        variant="modal"
        title="設 定"
        width={560}
        closeOnEsc={false}
      >
        <div className="im-settings-wrap">
          <SettingsScreen onBack={() => setMode('main')} embedded />
        </div>
      </Popup>
    )
  }

  if (mode === 'confirm-abandon') {
    return (
      <Popup
        open
        variant="confirm"
        title="放棄の確認"
        width={420}
        closeOnEsc={false}
        footer={
          <div className="im-confirm-foot">
            <button
              type="button"
              className="im-confirm-btn im-confirm-btn--cancel"
              onClick={() => setMode('main')}
              disabled={busy}
            >
              キャンセル
            </button>
            <button
              type="button"
              className="im-confirm-btn im-confirm-btn--danger"
              onClick={() => void confirmAbandon()}
              disabled={busy}
            >
              放棄する
            </button>
          </div>
        }
      >
        <p className="im-confirm-body">
          本当にこのランを放棄しますか？<br />
          この操作は <em>取り消せません</em>。<br />
          履歴に「放棄」として記録されます。
        </p>
      </Popup>
    )
  }

  return (
    <Popup
      open
      variant="modal"
      title="ゲームメニュー"
      width={420}
      onClose={onClose}
      closeOnEsc
    >
      <ul className="im-items">
        <li>
          <button
            type="button"
            className="im-item im-item--resume"
            onClick={onClose}
            aria-label="続ける"
          >
            <span className="im-item-icon" aria-hidden="true">▸</span>
            <span className="im-item-body">
              <span className="im-item-name">続ける</span>
              <span className="im-item-desc">ゲームに戻る</span>
            </span>
            <span className="im-item-hotkey" aria-hidden="true">ESC</span>
          </button>
        </li>
        <li>
          <button
            type="button"
            className="im-item im-item--settings"
            onClick={() => setMode('settings')}
            aria-label="音量設定"
          >
            <span className="im-item-icon" aria-hidden="true">⚙</span>
            <span className="im-item-body">
              <span className="im-item-name">音量設定</span>
              <span className="im-item-desc">音量 / 表示 / 操作ヒント</span>
            </span>
            <span className="im-item-hotkey" aria-hidden="true">S</span>
          </button>
        </li>
        <li>
          <button
            type="button"
            className="im-item im-item--save"
            onClick={() => void exit()}
            disabled={busy}
            aria-label="メニューに戻る"
          >
            <span className="im-item-icon" aria-hidden="true">◈</span>
            <span className="im-item-body">
              <span className="im-item-name">メニューに戻る</span>
              <span className="im-item-desc">タイトルに戻る（ラン継続保存）</span>
            </span>
            <span className="im-item-hotkey" aria-hidden="true">Q</span>
          </button>
        </li>
        <li>
          <button
            type="button"
            className="im-item im-item--danger"
            onClick={() => setMode('confirm-abandon')}
            aria-label="あきらめる"
          >
            <span className="im-item-icon" aria-hidden="true">✕</span>
            <span className="im-item-body">
              <span className="im-item-name">あきらめる</span>
              <span className="im-item-desc">このランを破棄してタイトルへ（履歴に放棄として記録）</span>
            </span>
            <span className="im-item-hotkey" aria-hidden="true">X</span>
          </button>
        </li>
      </ul>
    </Popup>
  )
}
