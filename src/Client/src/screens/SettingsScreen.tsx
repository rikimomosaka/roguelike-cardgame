// src/Client/src/screens/SettingsScreen.tsx
import { useState } from 'react'
import { useAccount } from '../context/AccountContext'
import { useAudioSettings } from '../hooks/useAudioSettings'
import './SettingsScreen.css'

type Props = {
  onBack: () => void
}

type Language = 'ja' | 'en'
type TextSpeed = 'slow' | 'normal' | 'fast' | 'instant'

const LANGUAGE_CHOICES: ReadonlyArray<{ value: Language; label: string }> = [
  { value: 'ja', label: '日本語' },
  { value: 'en', label: 'ENGLISH' },
]

const TEXT_SPEED_CHOICES: ReadonlyArray<{ value: TextSpeed; label: string }> = [
  { value: 'slow', label: 'SLOW' },
  { value: 'normal', label: 'NORMAL' },
  { value: 'fast', label: 'FAST' },
  { value: 'instant', label: 'INSTANT' },
]

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

  // Visual-only local state; not wired to any backend.
  const [language, setLanguage] = useState<Language>('ja')
  const [textSpeed, setTextSpeed] = useState<TextSpeed>('normal')

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

  return <SettingsScreenInner
    onBack={onBack}
    accountId={accountId}
    language={language}
    setLanguage={setLanguage}
    textSpeed={textSpeed}
    setTextSpeed={setTextSpeed}
  />
}

type InnerProps = {
  onBack: () => void
  accountId: string
  language: Language
  setLanguage: (l: Language) => void
  textSpeed: TextSpeed
  setTextSpeed: (s: TextSpeed) => void
}

function SettingsScreenInner({
  onBack,
  accountId,
  language,
  setLanguage,
  textSpeed,
  setTextSpeed,
}: InnerProps) {
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

          <section className="settings-screen__section">
            <div className="settings-screen__section-title">
              <span>▸ DISPLAY</span>
              <span className="settings-screen__section-title-sub">言語・表示</span>
            </div>

            <div className="settings-screen__select-row">
              <div className="settings-screen__slider-label">
                <span className="settings-screen__slider-label-mark" aria-hidden="true">❖</span>
                LANGUAGE
              </div>
              <div className="settings-screen__select-choices" role="radiogroup" aria-label="LANGUAGE">
                {LANGUAGE_CHOICES.map((c) => (
                  <button
                    key={c.value}
                    type="button"
                    role="radio"
                    aria-checked={language === c.value}
                    className={
                      'settings-screen__choice' + (language === c.value ? ' is-on' : '')
                    }
                    onClick={() => setLanguage(c.value)}
                  >
                    {c.label}
                  </button>
                ))}
              </div>
            </div>

            <div className="settings-screen__select-row">
              <div className="settings-screen__slider-label">
                <span className="settings-screen__slider-label-mark" aria-hidden="true">❖</span>
                TEXT SPEED
              </div>
              <div className="settings-screen__select-choices" role="radiogroup" aria-label="TEXT SPEED">
                {TEXT_SPEED_CHOICES.map((c) => (
                  <button
                    key={c.value}
                    type="button"
                    role="radio"
                    aria-checked={textSpeed === c.value}
                    className={
                      'settings-screen__choice' + (textSpeed === c.value ? ' is-on' : '')
                    }
                    onClick={() => setTextSpeed(c.value)}
                  >
                    {c.label}
                  </button>
                ))}
              </div>
            </div>
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
