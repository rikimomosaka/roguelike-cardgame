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
  const message =
    resolved && event.chosenIndex !== null
      ? event.choices[event.chosenIndex]?.resultMessage ?? event.startMessage
      : event.startMessage
  return (
    <Popup
      open
      variant="modal"
      title={event.name}
      width={720}
    >
      <div className="ev-frame" data-event={event.eventId}>
        <div className="ev-bg" aria-hidden="true" />
        <div className="ev-narrative-wrap">
          <p className="ev-narrative">{message}</p>
        </div>
        <div className="ev-actions">
          <ul className="ev-choices">
            {event.choices.map((c, i) => {
              const chosen = event.chosenIndex === i
              const disabled = resolved || !c.conditionMet
              return (
                <li key={i}>
                  <button
                    type="button"
                    className={
                      'ev-choice' + (chosen ? ' ev-choice--chosen' : '')
                    }
                    onClick={() => onChoose(i)}
                    disabled={disabled}
                  >
                    <span className="ev-choice__label">
                      {c.label}
                      {c.conditionSummary ? ` (${c.conditionSummary})` : ''}
                    </span>
                  </button>
                </li>
              )
            })}
          </ul>
          <div className="ev-close-wrap">
            <Button onClick={() => onClose()} aria-label="Close">
              閉じる
            </Button>
          </div>
        </div>
      </div>
    </Popup>
  )
}
