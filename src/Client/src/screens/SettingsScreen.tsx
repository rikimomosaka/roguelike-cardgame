// src/Client/src/screens/SettingsScreen.tsx
import { useAccount } from '../context/AccountContext'
import { useAudioSettings } from '../hooks/useAudioSettings'
import './SettingsScreen.css'

type Props = {
  onBack: () => void
}

type SliderRowProps = {
  label: string
  value: number
  onChange: (v: number) => void
}

function SliderRow({ label, value, onChange }: SliderRowProps) {
  const clamped = Math.max(0, Math.min(100, Math.round(value)))
  return (
    <div className="settings-screen__slider-row">
      <div className="settings-screen__slider-label">
        <span className="settings-screen__slider-label-mark" aria-hidden="true">♪</span>
        {label}
      </div>
      <div
        className="settings-screen__slider-track"
        style={{ '--fill': `${clamped}%` } as React.CSSProperties}
      >
        <div className="settings-screen__slider-fill" aria-hidden="true" />
        <div className="settings-screen__slider-thumb" aria-hidden="true" />
        <input
          type="range"
          min={0}
          max={100}
          step={1}
          value={clamped}
          aria-label={label}
          className="settings-screen__slider-input"
          onChange={(e) => onChange(Number(e.target.value))}
        />
      </div>
      <div className="settings-screen__slider-value">{clamped}</div>
    </div>
  )
}

export function SettingsScreen({ onBack }: Props) {
  const { accountId } = useAccount()

  if (!accountId) {
    return (
      <main className="settings-screen">
        <div className="settings-screen__pattern" aria-hidden="true" />
        <div className="settings-screen__placeholder">
          <p>ログインが必要です。</p>
          <button type="button" className="settings-screen__back" onClick={onBack}>
            <span className="settings-screen__back-mark" aria-hidden="true">◂</span>
            メニューへ戻る
          </button>
        </div>
      </main>
    )
  }

  return <SettingsScreenInner onBack={onBack} accountId={accountId} />
}

type InnerProps = {
  onBack: () => void
  accountId: string
}

function SettingsScreenInner({ onBack, accountId }: InnerProps) {
  const { settings, update, saveStatus } = useAudioSettings(accountId)

  const statusClass =
    saveStatus === 'error'
      ? 'settings-screen__status settings-screen__status--error'
      : saveStatus === 'idle'
        ? 'settings-screen__status settings-screen__status--idle'
        : 'settings-screen__status'

  const statusText =
    saveStatus === 'saving'
      ? '保存中…'
      : saveStatus === 'saved'
        ? '保存済み'
        : saveStatus === 'error'
          ? '保存に失敗しました'
          : '未変更'

  return (
    <main className="settings-screen">
      <div className="settings-screen__pattern" aria-hidden="true" />

      <div className="settings-screen__content">
        <header className="settings-screen__header">
          <h2 className="settings-screen__title">❖ SETTINGS ❖</h2>
          <div className="settings-screen__ornament" aria-hidden="true">✦ ✦ ✦</div>
        </header>

        <div className="settings-screen__body">
          <section className="settings-screen__section">
            <div className="settings-screen__section-title">
              <span>▸ AUDIO</span>
              <span className="settings-screen__section-title-sub">音量設定</span>
            </div>
            {settings === null ? (
              <p className="settings-screen__placeholder-inline">読み込み中…</p>
            ) : (
              <>
                <SliderRow
                  label="MASTER"
                  value={settings.master}
                  onChange={(v) => update({ master: v })}
                />
                <SliderRow
                  label="BGM"
                  value={settings.bgm}
                  onChange={(v) => update({ bgm: v })}
                />
                <SliderRow
                  label="SE"
                  value={settings.se}
                  onChange={(v) => update({ se: v })}
                />
                <SliderRow
                  label="AMBIENT"
                  value={settings.ambient}
                  onChange={(v) => update({ ambient: v })}
                />
              </>
            )}
          </section>

        </div>

        <footer className="settings-screen__footer">
          <span className={statusClass} aria-live="polite">
            <span className="settings-screen__status-dot" aria-hidden="true">●</span>
            {statusText}
          </span>
          <button type="button" className="settings-screen__back" onClick={onBack}>
            <span className="settings-screen__back-mark" aria-hidden="true">◂</span>
            メニューへ戻る
          </button>
        </footer>
      </div>
    </main>
  )
}
