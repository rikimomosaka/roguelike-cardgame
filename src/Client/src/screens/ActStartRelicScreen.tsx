import { useMemo, useState } from 'react'
import { Popup } from '../components/Popup'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import './ActStartRelicScreen.css'

type Props = {
  choices: string[]
  relicNames: Record<string, string>
  relicDescriptions?: Record<string, string>
  onChoose: (relicId: string) => Promise<void> | void
  onClose: () => void
}

export function ActStartRelicScreen({ choices, relicNames, relicDescriptions, onChoose, onClose }: Props) {
  const [chosen, setChosen] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleChoose(id: string) {
    if (chosen || busy) return
    setBusy(true)
    try {
      await onChoose(id)
      setChosen(id)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Popup
      open
      variant="modal"
      title="レリックを選ぶ"
      subtitle={chosen ? '選択しました — 閉じるボタンを押してください' : '1 つを選べ'}
      width={900}
      closeOnEsc={false}
      headRight={
        <button
          type="button"
          className="ar-close"
          onClick={onClose}
          aria-label="閉じる"
        >
          ×
        </button>
      }
      footer={
        <span className="ar-hint">
          {chosen
            ? '一覧を見て準備ができたら閉じてください'
            : '1 つを選んでください'}
        </span>
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
                disabled={busy || (chosen !== null && chosen !== id)}
                isChosen={chosen === id}
                onChoose={handleChoose}
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
  disabled: boolean
  isChosen: boolean
  onChoose: (id: string) => void
}

function RelicChoice({ id, name, desc, disabled, isChosen, onChoose }: ChoiceProps) {
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!desc) return null
    return { name, desc }
  }, [name, desc])
  const tip = useTooltipTarget(tooltipContent)

  const className = ['ar-slot', isChosen ? 'is-chosen' : '']
    .filter(Boolean)
    .join(' ')

  return (
    <button
      type="button"
      className={className}
      onClick={() => onChoose(id)}
      aria-label={name}
      disabled={disabled}
      onMouseEnter={tip.onMouseEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={tip.onMouseLeave}
    >
      <span className="ar-icon" aria-hidden="true">◆</span>
      <span className="ar-name">{name}</span>
      {isChosen && <span className="ar-chosen-mark" aria-hidden="true">✓</span>}
    </button>
  )
}
