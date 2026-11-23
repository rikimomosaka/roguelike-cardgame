// src/Client/src/screens/SettingsScreen.tsx
import { useAccount } from '../context/AccountContext'
import { useAudio } from '../audio'
import { usePresentationSettings, type MotionLevel, type UiScale } from '../presentation'
import './SettingsScreen.css'

type Props = {
  onBack: () => void
  embedded?: boolean
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

type ChoiceOption<T extends string | number> = {
  value: T
  label: string
}

type ChoiceRowProps<T extends string | number> = {
  label: string
  ariaLabel: string
  value: T
  options: readonly ChoiceOption<T>[]
  onChange: (value: T) => void
}

function ChoiceRow<T extends string | number>({
  label,
  ariaLabel,
  value,
  options,
  onChange,
}: ChoiceRowProps<T>) {
  return (
    <div className="settings-screen__select-row">
      <div className="settings-screen__slider-label">
        <span className="settings-screen__slider-label-mark" aria-hidden="true">◆</span>
        {label}
      </div>
      <div className="settings-screen__select-choices" role="group" aria-label={ariaLabel}>
        {options.map(option => (
          <button
            key={option.value}
            type="button"
            className={`settings-screen__choice${option.value === value ? ' is-on' : ''}`}
            aria-pressed={option.value === value}
            onClick={() => onChange(option.value)}
          >
            {option.label}
          </button>
        ))}
      </div>
    </div>
  )
}

export function SettingsScreen({ onBack, embedded }: Props) {
  const { accountId } = useAccount()

  if (!accountId) {
    const rootClass = embedded
      ? 'settings-screen settings-screen--embedded'
      : 'settings-screen'
    return (
      <main className={rootClass}>
        {!embedded && <div className="settings-screen__pattern" aria-hidden="true" />}
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

  return <SettingsScreenInner onBack={onBack} embedded={embedded} />
}

type InnerProps = {
  onBack: () => void
  embedded?: boolean
}

function SettingsScreenInner({ onBack, embedded }: InnerProps) {
  const {
    settings,
    settingsLoaded,
    updateSettings,
    flushSettings,
    saveStatus,
  } = useAudio()
  const {
    settings: presentation,
    systemReducedMotion,
    effectiveMotionLevel,
    updatePresentationSettings,
    resetPresentationSettings,
  } = usePresentationSettings()
  const handleBack = () => {
    void flushSettings()
    onBack()
  }

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

  const rootClass = embedded
    ? 'settings-screen settings-screen--embedded'
    : 'settings-screen'

  return (
    <main className={rootClass}>
      {!embedded && <div className="settings-screen__pattern" aria-hidden="true" />}

      <div className="settings-screen__content">
        {!embedded && (
          <header className="settings-screen__header">
            <h2 className="settings-screen__title">❖ SETTINGS ❖</h2>
            <div className="settings-screen__ornament" aria-hidden="true">✦ ✦ ✦</div>
          </header>
        )}

        <div className="settings-screen__body">
          <section className="settings-screen__section">
            <div className="settings-screen__section-title">
              <span>▸ AUDIO</span>
              <span className="settings-screen__section-title-sub">音量設定</span>
            </div>
            {!settingsLoaded ? (
              <p className="settings-screen__placeholder-inline">読み込み中…</p>
            ) : (
              <>
                <SliderRow
                  label="MASTER"
                  value={settings.master}
                  onChange={(v) => updateSettings({ master: v })}
                />
                <SliderRow
                  label="BGM"
                  value={settings.bgm}
                  onChange={(v) => updateSettings({ bgm: v })}
                />
                <SliderRow
                  label="SE"
                  value={settings.se}
                  onChange={(v) => updateSettings({ se: v })}
                />
                <SliderRow
                  label="VOICE"
                  value={settings.voice}
                  onChange={(v) => updateSettings({ voice: v })}
                />
              </>
            )}
          </section>

          <section className="settings-screen__section" aria-label="演出設定">
            <div className="settings-screen__section-title">
              <span>▸ PRESENTATION</span>
              <span className="settings-screen__section-title-sub">表示・演出設定</span>
            </div>
            <ChoiceRow<MotionLevel>
              label="MOTION"
              ariaLabel="モーション量"
              value={presentation.motionLevel}
              options={[
                { value: 'full', label: '標準' },
                { value: 'reduced', label: '軽減' },
                { value: 'off', label: '停止' },
              ]}
              onChange={motionLevel => updatePresentationSettings({ motionLevel })}
            />
            <ChoiceRow<number>
              label="SHAKE"
              ariaLabel="画面揺れ"
              value={presentation.screenShake ? 1 : 0}
              options={[
                { value: 1, label: 'ON' },
                { value: 0, label: 'OFF' },
              ]}
              onChange={value => updatePresentationSettings({ screenShake: value === 1 })}
            />
            <ChoiceRow<number>
              label="FLASH"
              ariaLabel="閃光表現"
              value={presentation.flashes ? 1 : 0}
              options={[
                { value: 1, label: 'ON' },
                { value: 0, label: 'OFF' },
              ]}
              onChange={value => updatePresentationSettings({ flashes: value === 1 })}
            />
            <ChoiceRow<UiScale>
              label="UI SCALE"
              ariaLabel="UIサイズ"
              value={presentation.uiScale}
              options={[
                { value: 90, label: '90%' },
                { value: 100, label: '100%' },
                { value: 110, label: '110%' },
              ]}
              onChange={uiScale => updatePresentationSettings({ uiScale })}
            />
            <div className="settings-screen__presentation-note" aria-live="polite">
              {systemReducedMotion
                ? `OSの「視差効果を減らす」を優先中（現在：${effectiveMotionLevel === 'off' ? '停止' : '軽減'}）`
                : '演出設定はこの端末に自動保存されます。'}
              <button
                type="button"
                className="settings-screen__reset"
                onClick={resetPresentationSettings}
              >
                初期値に戻す
              </button>
            </div>
          </section>

        </div>

        <footer className="settings-screen__footer">
          <span className={statusClass} aria-live="polite">
            <span className="settings-screen__status-dot" aria-hidden="true">●</span>
            {statusText}
          </span>
          <button type="button" className="settings-screen__back" onClick={handleBack}>
            <span className="settings-screen__back-mark" aria-hidden="true">◂</span>
            メニューへ戻る
          </button>
        </footer>
      </div>
    </main>
  )
}
