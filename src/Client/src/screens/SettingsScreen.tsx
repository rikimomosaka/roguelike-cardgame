// src/Client/src/screens/SettingsScreen.tsx
import { Button } from '../components/Button'
import { Slider } from '../components/Slider'
import { useAccount } from '../context/AccountContext'
import { useAudioSettings } from '../hooks/useAudioSettings'

type Props = {
  onBack: () => void
}

export function SettingsScreen({ onBack }: Props) {
  const { accountId } = useAccount()
  if (!accountId) {
    return (
      <main className="settings-screen">
        <p>ログインが必要です。</p>
        <Button onClick={onBack}>戻る</Button>
      </main>
    )
  }
  const { settings, update, saveStatus } = useAudioSettings(accountId)

  return (
    <main className="settings-screen">
      <header><h2>設定</h2></header>
      {settings === null ? (
        <p>読み込み中…</p>
      ) : (
        <div className="settings-screen__sliders">
          <Slider label="Master" value={settings.master} onChange={(v) => update({ master: v })} />
          <Slider label="BGM" value={settings.bgm} onChange={(v) => update({ bgm: v })} />
          <Slider label="SE" value={settings.se} onChange={(v) => update({ se: v })} />
          <Slider label="Ambient" value={settings.ambient} onChange={(v) => update({ ambient: v })} />
        </div>
      )}
      <footer className="settings-screen__footer">
        <span aria-live="polite">
          {saveStatus === 'saving' && '保存中…'}
          {saveStatus === 'saved' && '保存済み ✓'}
          {saveStatus === 'error' && '保存に失敗しました'}
        </span>
        <Button onClick={onBack}>メニューへ戻る</Button>
      </footer>
    </main>
  )
}
