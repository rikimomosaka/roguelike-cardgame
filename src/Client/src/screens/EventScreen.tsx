import { useState } from 'react'
import type { EventInstanceDto } from '../api/types'
import { Button } from '../components/Button'
import { Popup } from '../components/Popup'
import './EventScreen.css'

type Props = {
  event: EventInstanceDto
  onChoose: (choiceIndex: number) => void | Promise<void>
  onClose: () => void
}

export function EventScreen({ event, onChoose, onClose }: Props) {
  const resolved = event.chosenIndex !== null
  const [pendingChoice, setPendingChoice] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)
  const isShadyMerchant = event.eventId === 'shady_merchant'
  const message =
    resolved && event.chosenIndex !== null
      ? event.choices[event.chosenIndex]?.resultMessage ?? event.startMessage
      : event.startMessage

  async function handleChoose(choiceIndex: number) {
    if (pendingChoice !== null || resolved) return
    setError(null)
    setPendingChoice(choiceIndex)
    try {
      await onChoose(choiceIndex)
    } catch {
      setError('選択を確定できませんでした。もう一度お試しください。')
    } finally {
      setPendingChoice(null)
    }
  }

  const phase = resolved ? 'resolved' : pendingChoice !== null ? 'pending' : error ? 'error' : 'choosing'
  return (
    <Popup
      open
      variant="modal"
      title={event.name}
      width={720}
    >
      <div className="ev-frame" data-event={event.eventId} data-state={phase} aria-busy={pendingChoice !== null}>
        <div className="ev-bg" aria-hidden="true" />
        <div className="ev-narrative-wrap">
          <span className="ev-phase" aria-live="polite">
            {phase === 'pending' ? '運命を確定中…' : phase === 'resolved' ? '選択の結果' : '選択してください'}
          </span>
          <p className="ev-narrative">{message}</p>
          {error && <p className="ev-error" role="alert">{error}</p>}
        </div>
        <div className="ev-actions">
          <ul className="ev-choices">
            {event.choices.map((c, i) => {
              const chosen = event.chosenIndex === i
              const pending = pendingChoice === i
              const disabled = resolved || pendingChoice !== null || !c.conditionMet
              return (
                <li key={i}>
                  <button
                    type="button"
                    className={
                      'ev-choice'
                      + (chosen ? ' ev-choice--chosen' : '')
                      + (pending ? ' ev-choice--pending' : '')
                    }
                    onClick={() => void handleChoose(i)}
                    disabled={disabled}
                  >
                    <span className="ev-choice__label">
                      {c.label}
                      {!isShadyMerchant && c.conditionSummary ? ` (${c.conditionSummary})` : ''}
                    </span>
                    {pending && <span className="ev-choice__pending" role="status">処理中…</span>}
                  </button>
                </li>
              )
            })}
          </ul>
          <div className="ev-close-wrap">
            <Button onClick={() => onClose()} aria-label="Close" disabled={pendingChoice !== null}>
              閉じる
            </Button>
          </div>
        </div>
      </div>
    </Popup>
  )
}
