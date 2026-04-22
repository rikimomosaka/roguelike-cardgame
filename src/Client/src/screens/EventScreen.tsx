import type { EventInstanceDto } from '../api/types'
import { Button } from '../components/Button'

type Props = {
  event: EventInstanceDto
  onChoose: (choiceIndex: number) => void | Promise<void>
  onClose: () => void
}

export function EventScreen({ event, onChoose, onClose }: Props) {
  const resolved = event.chosenIndex !== null
  return (
    <div className="event-screen" role="dialog" aria-modal="true">
      <h2>{event.name}</h2>
      <p>{event.description}</p>
      <ul className="event-choices">
        {event.choices.map((c, i) => {
          const chosen = event.chosenIndex === i
          return (
            <li key={i} className={chosen ? 'event-choice--chosen' : undefined}>
              <Button
                onClick={() => onChoose(i)}
                disabled={resolved || !c.conditionMet}
                aria-disabled={resolved || !c.conditionMet}
              >
                {chosen ? '✓ ' : ''}
                {c.label}
                {c.conditionSummary ? ` (${c.conditionSummary})` : ''}
              </Button>
            </li>
          )
        })}
      </ul>
      {resolved && (
        <Button onClick={() => onClose()} aria-label="Close">
          閉じる
        </Button>
      )}
    </div>
  )
}
