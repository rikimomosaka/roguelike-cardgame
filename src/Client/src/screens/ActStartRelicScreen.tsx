import { Button } from '../components/Button'

type Props = {
  choices: string[]
  relicNames: Record<string, string>
  onChoose: (relicId: string) => void
}

export function ActStartRelicScreen({ choices, relicNames, onChoose }: Props) {
  return (
    <div className="act-start-relic-screen" role="dialog" aria-modal="true">
      <h2>層開始のレリックを選ぶ</h2>
      <ul>
        {choices.map(id => (
          <li key={id}>
            <Button onClick={() => onChoose(id)} aria-label={relicNames[id] ?? id}>
              {relicNames[id] ?? id}
            </Button>
          </li>
        ))}
      </ul>
    </div>
  )
}
