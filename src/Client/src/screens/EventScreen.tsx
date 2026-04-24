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
  return (
    <Popup
      open
      variant="modal"
      title={event.name}
      width={720}
      footer={
        resolved ? (
          <Button onClick={() => onClose()} aria-label="Close">
            閉じる
          </Button>
        ) : undefined
      }
    >
      <div className="ev-art" aria-hidden="true">
        ✦
      </div>
      <p className="ev-narrative">{event.description}</p>
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
                aria-disabled={disabled ? 'true' : 'false'}
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
    </Popup>
  )
}
