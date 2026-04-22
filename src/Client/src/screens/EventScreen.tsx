import type { EventInstanceDto } from '../api/types'
import { Button } from '../components/Button'

type Props = {
  event: EventInstanceDto
  onChoose: (choiceIndex: number) => void | Promise<void>
}

export function EventScreen({ event, onChoose }: Props) {
  return (
    <div className="event-screen" role="dialog" aria-modal="true">
      <h2>{event.name}</h2>
      <p>{event.description}</p>
      <ul className="event-choices">
        {event.choices.map((c, i) => (
          <li key={i}>
            <Button
              onClick={() => onChoose(i)}
              disabled={!c.conditionMet}
              aria-disabled={!c.conditionMet}
            >
              {c.label}
              {c.conditionSummary ? ` (${c.conditionSummary})` : ''}
            </Button>
          </li>
        ))}
      </ul>
    </div>
  )
}
