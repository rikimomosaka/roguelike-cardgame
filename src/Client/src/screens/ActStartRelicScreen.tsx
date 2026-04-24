import { Popup } from '../components/Popup'
import './ActStartRelicScreen.css'

type Props = {
  choices: string[]
  relicNames: Record<string, string>
  onChoose: (relicId: string) => void
}

export function ActStartRelicScreen({ choices, relicNames, onChoose }: Props) {
  return (
    <Popup
      open
      variant="modal"
      title="層開始のレリックを選ぶ"
      subtitle="1 つを選べ"
      width={900}
      closeOnEsc={false}
      footer={
        <span className="ar-hint">1 つを選んでください</span>
      }
    >
      <ul className="ar-slots">
        {choices.map(id => {
          const name = relicNames[id] ?? id
          return (
            <li key={id}>
              <button
                type="button"
                className="ar-slot"
                onClick={() => onChoose(id)}
                aria-label={name}
              >
                <span className="ar-icon" aria-hidden="true">◆</span>
                <span className="ar-name">{name}</span>
              </button>
            </li>
          )
        })}
      </ul>
    </Popup>
  )
}
