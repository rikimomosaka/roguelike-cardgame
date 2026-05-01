// Phase 10.5.M: effects[] / upgradedEffects[] の add/remove/reorder + 各行の EffectEditor。

import type { CardEffect } from './DevSpecTypes'
import { emptyEffect } from './DevSpecTypes'
import type { DevMeta } from '../../api/dev'
import { EffectEditor } from './EffectEditor'

type Props = {
  effects: CardEffect[]
  meta: DevMeta
  allCardIds: string[]
  label: string
  onChange: (next: CardEffect[]) => void
}

export function EffectListEditor({
  effects,
  meta,
  allCardIds,
  label,
  onChange,
}: Props) {
  const updateAt = (i: number, eff: CardEffect) => {
    const next = effects.slice()
    next[i] = eff
    onChange(next)
  }
  const removeAt = (i: number) => onChange(effects.filter((_, j) => j !== i))
  const addNew = () => onChange([...effects, emptyEffect()])
  const moveUp = (i: number) => {
    if (i <= 0) return
    const next = effects.slice()
    ;[next[i - 1], next[i]] = [next[i], next[i - 1]]
    onChange(next)
  }
  const moveDown = (i: number) => {
    if (i >= effects.length - 1) return
    const next = effects.slice()
    ;[next[i + 1], next[i]] = [next[i], next[i + 1]]
    onChange(next)
  }

  return (
    <div className="effect-list">
      <h4 className="effect-list__heading">
        {label} ({effects.length})
      </h4>
      {effects.map((eff, i) => (
        <div key={i} className="effect-row">
          <div className="effect-row__order">
            <button
              type="button"
              onClick={() => moveUp(i)}
              disabled={i === 0}
              aria-label={`move ${label} effect ${i} up`}
            >
              ↑
            </button>
            <button
              type="button"
              onClick={() => moveDown(i)}
              disabled={i === effects.length - 1}
              aria-label={`move ${label} effect ${i} down`}
            >
              ↓
            </button>
          </div>
          <EffectEditor
            effect={eff}
            meta={meta}
            allCardIds={allCardIds}
            onChange={(e) => updateAt(i, e)}
            onRemove={() => removeAt(i)}
          />
        </div>
      ))}
      <button type="button" className="effect-list__add" onClick={addNew}>
        + 効果を追加
      </button>
    </div>
  )
}
