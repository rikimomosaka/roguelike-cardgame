import { useMemo, useState } from 'react'
import { Button } from '../components/Button'
import { Popup } from '../components/Popup'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import './ActStartRelicScreen.css'

type Props = {
  choices: string[]
  relicNames: Record<string, string>
  /** Phase 10.5.M6.3 以降は effectText + flavor 分離が望ましい。merged は backward-compat 用フォールバック。 */
  relicDescriptions?: Record<string, string>
  /** 効果テキスト (markers 含む)。指定された場合 tooltip の desc にこちらを使う。 */
  relicEffectTexts?: Record<string, string>
  /** flavor テキスト (斜体グレー / 点線下表示)。指定された場合 tooltip の flavor にこちらを使う。 */
  relicFlavors?: Record<string, string>
  onChoose: (relicId: string) => Promise<void> | void
  onClose: () => void
}

export function ActStartRelicScreen({ choices, relicNames, relicDescriptions, relicEffectTexts, relicFlavors, onChoose, onClose }: Props) {
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
          // Phase 10.6.B フォローアップ: effectText + flavor を分離して tooltip に渡す。
          // 両方未指定なら relicDescriptions (merged) を desc に fallback。
          const effectText = relicEffectTexts?.[id] ?? null
          const flavor = relicFlavors?.[id] ?? null
          const desc = effectText ?? relicDescriptions?.[id] ?? null
          return (
            <li key={id}>
              <RelicChoice
                id={id}
                name={name}
                desc={desc}
                flavor={flavor}
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
  flavor: string | null
  disabled: boolean
  isSelected: boolean
  onClick: (id: string) => void
}

function RelicChoice({ id, name, desc, flavor, disabled, isSelected, onClick }: ChoiceProps) {
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!desc) return null
    return flavor ? { name, desc, flavor } : { name, desc }
  }, [name, desc, flavor])
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
