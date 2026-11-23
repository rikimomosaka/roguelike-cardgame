import { useEffect, useRef, useState } from 'react'
import { useAudio } from '../audio'
import {
  BGM_TRACKS,
  SE_CUES,
  selectSe,
  type SeCueName,
} from '../audio/audioManifest'
import './AudioLabScreen.css'

type Props = {
  onBack: () => void
}

type BgmTrackName = keyof typeof BGM_TRACKS

const BGM_LABELS: Record<BgmTrackName, string> = {
  menu: 'メニュー',
  map: 'マップ',
  shop: 'ショップ',
  battleNormal: '通常戦',
  battleElite: 'エリート戦',
  battleBoss: 'ボス戦',
  victory: '勝利',
  gameOver: 'ゲームオーバー',
}

const BGM_NAMES = Object.keys(BGM_TRACKS) as BgmTrackName[]
const SE_NAMES = Object.keys(SE_CUES) as SeCueName[]
const COMBO_STEPS = Array.from({ length: 15 }, (_, index) => index + 1)
const COMBO_INTERVAL_MS = 180

export function AudioLabScreen({ onBack }: Props) {
  const {
    settings,
    playBgm,
    stopBgm,
    playSe,
    playComboTone,
  } = useAudio()
  const [activeBgm, setActiveBgm] = useState<BgmTrackName | null>(null)
  const [comboSequenceStep, setComboSequenceStep] = useState<number | null>(null)
  const seAuditionSeed = useRef(0)

  useEffect(() => {
    if (comboSequenceStep === null) return

    playComboTone(comboSequenceStep)
    const timer = window.setTimeout(() => {
      setComboSequenceStep((current) => {
        if (current === null || current >= 15) return null
        return current + 1
      })
    }, COMBO_INTERVAL_MS)

    return () => window.clearTimeout(timer)
  }, [comboSequenceStep, playComboTone])

  const handlePlayBgm = (name: BgmTrackName) => {
    playBgm(BGM_TRACKS[name], { fadeMs: 300 })
    setActiveBgm(name)
  }

  const handleStopBgm = () => {
    stopBgm({ fadeMs: 200 })
    setActiveBgm(null)
  }

  const handlePlaySe = (name: SeCueName) => {
    seAuditionSeed.current += 1
    void playSe(selectSe(name, seAuditionSeed.current))
  }

  return (
    <main className="audio-lab">
      <header className="audio-lab__header">
        <div>
          <p className="audio-lab__eyebrow">DEVELOPMENT TOOL</p>
          <h1>AUDIO LAB</h1>
        </div>
        <button type="button" className="audio-lab__back" onClick={onBack}>
          戻る
        </button>
      </header>

      <section className="audio-lab__levels" aria-label="現在の音量">
        {([
          ['MASTER', settings.master],
          ['BGM', settings.bgm],
          ['SE', settings.se],
          ['VOICE', settings.voice],
        ] as const).map(([label, value]) => (
          <div className="audio-lab__level" key={label}>
            <span>{label}</span>
            <strong>{value}</strong>
          </div>
        ))}
      </section>

      <div className="audio-lab__content">
        <section className="audio-lab__panel audio-lab__panel--bgm">
          <div className="audio-lab__panel-heading">
            <h2>BGM</h2>
            <button type="button" onClick={handleStopBgm}>停止</button>
          </div>
          <div className="audio-lab__button-grid">
            {BGM_NAMES.map((name) => (
              <button
                type="button"
                className={activeBgm === name ? 'is-active' : undefined}
                aria-pressed={activeBgm === name}
                onClick={() => handlePlayBgm(name)}
                key={name}
              >
                <span>{BGM_LABELS[name]}</span>
                <small>{BGM_TRACKS[name].id}</small>
              </button>
            ))}
          </div>
        </section>

        <section className="audio-lab__panel audio-lab__panel--combo">
          <div className="audio-lab__panel-heading">
            <h2>COMBO 1–15</h2>
            {comboSequenceStep === null ? (
              <button type="button" onClick={() => setComboSequenceStep(1)}>
                連続再生
              </button>
            ) : (
              <button type="button" onClick={() => setComboSequenceStep(null)}>
                停止（{comboSequenceStep}）
              </button>
            )}
          </div>
          <div className="audio-lab__combo-grid">
            {COMBO_STEPS.map((combo) => (
              <button
                type="button"
                className={comboSequenceStep === combo ? 'is-active' : undefined}
                aria-label={`コンボ${combo}を試聴`}
                onClick={() => playComboTone(combo)}
                key={combo}
              >
                {combo}
              </button>
            ))}
          </div>
        </section>

        <section className="audio-lab__panel audio-lab__panel--se">
          <div className="audio-lab__panel-heading">
            <h2>SE CUES</h2>
            <span>{SE_NAMES.length} cues</span>
          </div>
          <div className="audio-lab__se-grid">
            {SE_NAMES.map((name) => (
              <button type="button" onClick={() => handlePlaySe(name)} key={name}>
                {name}
              </button>
            ))}
          </div>
        </section>
      </div>
    </main>
  )
}
