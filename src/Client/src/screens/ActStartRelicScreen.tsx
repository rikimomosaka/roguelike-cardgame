import { useMemo } from 'react'
import { Popup } from '../components/Popup'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import './ActStartRelicScreen.css'

type Props = {
  choices: string[]
  relicNames: Record<string, string>
  relicDescriptions?: Record<string, string>
  onChoose: (relicId: string) => void
}

export function ActStartRelicScreen({ choices, relicNames, relicDescriptions, onChoose }: Props) {
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
          const desc = relicDescriptions?.[id] ?? null
          return (
            <li key={id}>
              <RelicChoice
                id={id}
                name={name}
                desc={desc}
                onChoose={onChoose}
              />
            </li>
          )
        })}
      </ul>
    </Popup>
  )
}

type ChoiceProps = {
  id: string
  name: string
  desc: string | null
  onChoose: (id: string) => void
}

function RelicChoice({ id, name, desc, onChoose }: ChoiceProps) {
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!desc) return null
    return { name, desc }
  }, [name, desc])
  const tip = useTooltipTarget(tooltipContent)

  return (
    <button
      type="button"
      className="ar-slot"
      onClick={() => onChoose(id)}
      aria-label={name}
      onMouseEnter={tip.onMouseEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={tip.onMouseLeave}
    >
      <span className="ar-icon" aria-hidden="true">◆</span>
      <span className="ar-name">{name}</span>
    </button>
  )
}
