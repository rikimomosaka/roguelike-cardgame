import { useMemo, useState } from 'react'
import { Button } from '../components/Button'
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
  const [selected, setSelected] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  function toggle(id: string) {
    if (busy) return
    setSelected(prev => (prev === id ? null : id))
  }

  async function handleConfirm() {
    if (!selected || busy) return
    setBusy(true)
    try {
      await onChoose(selected)
      onClose()
    } finally {
      setBusy(false)
    }
  }

  return (
    <Popup
      open
      variant="modal"
      title="レリックを選ぶ"
      width={900}
      closeOnEsc={false}
      footerAlign="center"
      footer={
        selected !== null ? (
          <Button onClick={() => void handleConfirm()} disabled={busy} aria-label="決定">
            決定
          </Button>
        ) : (
          <Button onClick={onClose} aria-label="閉じる">
            閉じる
          </Button>
        )
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
                disabled={busy}
                isSelected={selected === id}
                onClick={toggle}
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
  isSelected: boolean
  onClick: (id: string) => void
}

function RelicChoice({ id, name, desc, disabled, isSelected, onClick }: ChoiceProps) {
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!desc) return null
    return { name, desc }
  }, [name, desc])
  const tip = useTooltipTarget(tooltipContent)

  const className = ['ar-slot', isSelected ? 'is-chosen' : '']
    .filter(Boolean)
    .join(' ')

  return (
    <button
      type="button"
      className={className}
      onClick={() => onClick(id)}
      aria-label={name}
      aria-pressed={isSelected}
      disabled={disabled}
      onMouseEnter={tip.onMouseEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={tip.onMouseLeave}
    >
      <span className="ar-icon" aria-hidden="true">
        <img src={`/icons/relics/${id}.png`} alt="" draggable={false} />
      </span>
      <span className="ar-name">{name}</span>
    </button>
  )
}
