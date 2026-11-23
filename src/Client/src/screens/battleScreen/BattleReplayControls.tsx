import type { ReactNode } from 'react'
import type { BattleReplayEntryDto } from '../../api/types'
import './BattleReplayControls.css'

export type ReplaySpeed = 0.5 | 1 | 2

type Props = {
  subjectName: string
  feedState: string
  entries: BattleReplayEntryDto[]
  position: number
  playing: boolean
  loading?: boolean
  speed: ReplaySpeed
  onBack: () => void
  onRestart: () => void
  onTogglePlaying: () => void
  onSkip: () => void
  onStepBack?: () => void
  onStepForward?: () => void
  onSeek?: (position: number) => void
  onSpeedChange: (speed: ReplaySpeed) => void
  renderCard?: (entry: BattleReplayEntryDto) => ReactNode
}

function normalizedFeedLabel(feedState: string): string {
  if (feedState.toLowerCase() === 'ready') return 'READY'
  if (feedState.toLowerCase() === 'complete') return 'COMPLETE'
  return 'LIVE'
}

function describeEffect(entry: BattleReplayEntryDto): string[] {
  return entry.effects.map(effect => {
    if (effect.description) return effect.description
    const status = effect.statusId ? ` ${effect.statusId}` : ''
    const amount = effect.amount === null ? '' : ` ${effect.amount}`
    return `${effect.kind}${status}${amount}`
  })
}

function describeIntent(entry: BattleReplayEntryDto): string[] {
  return entry.intents.map(intent => {
    const hits = intent.hits > 1 ? `×${intent.hits}` : ''
    return `${intent.scope} ${intent.amount}${hits}`
  })
}

export function BattleReplayControls({
  subjectName,
  feedState,
  entries,
  position,
  playing,
  loading,
  speed,
  onBack,
  onRestart,
  onTogglePlaying,
  onSkip,
  onStepBack,
  onStepForward,
  onSeek,
  onSpeedChange,
  renderCard,
}: Props) {
  const clampedPosition = Math.max(0, Math.min(position, entries.length))
  const current = clampedPosition > 0 ? entries[clampedPosition - 1] : null
  const feedLabel = normalizedFeedLabel(feedState)
  const effectLines = current ? describeEffect(current) : []
  const intentLines = current ? describeIntent(current) : []
  const actionDisplayName = current
    ? current.card?.displayName
      ?? current.ability?.displayName
      ?? current.source?.actionDisplayName
      ?? current.kind
    : ''

  return (
    <section className="battle-replay" aria-label={`${subjectName}の行動再生`}>
      {current ? (
        <div
          className={`battle-replay__entry battle-replay__entry--${current.viewKind}`}
          aria-live="polite"
        >
          {current.card && renderCard ? (
            <div className="battle-replay__card" data-replay-view="source-card">
              {renderCard(current)}
            </div>
          ) : null}
          <div className="battle-replay__entry-body">
            <div className="battle-replay__entry-title">
              {current.viewKind === 'externalEffect'
                ? `${actionDisplayName}から受けた効果`
                : current.isResolution
                  ? '行動を解決'
                  : actionDisplayName}
            </div>
            {current.viewKind === 'externalEffect' ? (
              <div className="battle-replay__source">
                発生元: {current.source?.actorDisplayName
                  ?? current.actorInstanceId
                  ?? '不明'}
                {current.source?.actionDisplayName
                  ? `（${current.source.actionDisplayName}）`
                  : null}
              </div>
            ) : null}
            {current.ability?.description ? (
              <div className="battle-replay__effect">{current.ability.description}</div>
            ) : null}
            {effectLines.map((line, index) => (
              <div className="battle-replay__effect" key={`effect-${index}`}>{line}</div>
            ))}
            {intentLines.map((line, index) => (
              <div className="battle-replay__intent" key={`intent-${index}`}>予定: {line}</div>
            ))}
            {current.note ? <div className="battle-replay__note">{current.note}</div> : null}
          </div>
        </div>
      ) : (
        <div className="battle-replay__entry battle-replay__entry--empty" aria-live="polite">
          {loading ? '行動を読み込み中…' : '再生を開始できます。'}
        </div>
      )}

      <div className="battle-replay__toolbar">
        <button type="button" className="battle-replay__button" onClick={onBack}>戻る</button>
        <button
          type="button"
          className="battle-replay__button"
          onClick={onRestart}
          disabled={entries.length === 0}
        >
          最初から再生
        </button>
        <button
          type="button"
          className="battle-replay__button battle-replay__button--play"
          onClick={onTogglePlaying}
          disabled={entries.length === 0 || clampedPosition >= entries.length}
        >
          {playing ? '停止' : '再生'}
        </button>
        <span className="battle-replay__progress" aria-label={`再生位置 ${clampedPosition}/${entries.length}`}>
          {clampedPosition}/{entries.length}
        </span>
        <button
          type="button"
          className="battle-replay__button"
          onClick={onStepBack}
          disabled={!onStepBack || clampedPosition <= 0 || playing}
          aria-label="1行動戻る"
        >
          ◀ 1
        </button>
        <button
          type="button"
          className="battle-replay__button"
          onClick={onStepForward}
          disabled={!onStepForward || clampedPosition >= entries.length || playing}
          aria-label="1行動進む"
        >
          1 ▶
        </button>
        <span
          className={`battle-replay__feed battle-replay__feed--${feedLabel.toLowerCase()}`}
          aria-label={`行動状態 ${feedLabel}`}
        >
          {feedLabel}
        </span>
        <label className="battle-replay__speed">
          <span>速度</span>
          <select
            aria-label="再生速度"
            value={String(speed)}
            onChange={event => onSpeedChange(Number(event.target.value) as ReplaySpeed)}
          >
            <option value="0.5">0.5×</option>
            <option value="1">1×</option>
            <option value="2">2×</option>
          </select>
        </label>
        <button
          type="button"
          className="battle-replay__button"
          onClick={onSkip}
          disabled={entries.length === 0 || clampedPosition >= entries.length}
        >
          スキップ
        </button>
        {entries.length > 0 ? (
          <label className="battle-replay__timeline">
            <span>行動位置</span>
            <input
              type="range"
              min={0}
              max={entries.length}
              step={1}
              value={clampedPosition}
              disabled={playing || !onSeek}
              onChange={event => onSeek?.(Number(event.target.value))}
              aria-label="行動タイムライン"
            />
          </label>
        ) : null}
      </div>
    </section>
  )
}
