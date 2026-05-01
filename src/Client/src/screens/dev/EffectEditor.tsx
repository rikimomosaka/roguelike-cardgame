// Phase 10.5.M: action 連動の動的 field を持つ単一 effect エディタ。
// EFFECT_ACTION_FIELDS で action ごとに表示する field を切り替える。

import type { CardEffect } from './DevSpecTypes'
import { EFFECT_ACTION_FIELDS } from './DevSpecTypes'
import type { DevMeta } from '../../api/dev'

type Props = {
  effect: CardEffect
  meta: DevMeta
  allCardIds: string[]
  onChange: (e: CardEffect) => void
  onRemove: () => void
}

export function EffectEditor({ effect, meta, allCardIds, onChange, onRemove }: Props) {
  const fields = EFFECT_ACTION_FIELDS[effect.action] ?? []

  const set = (patch: Partial<CardEffect>) => onChange({ ...effect, ...patch })

  return (
    <div className="effect-editor">
      <div className="effect-editor__row effect-editor__row--header">
        <label className="effect-editor__label">
          Action
          <select
            value={effect.action}
            onChange={(e) => set({ action: e.target.value })}
            aria-label="effect action"
          >
            {meta.effectActions.map((a) => (
              <option key={a} value={a}>
                {a}
              </option>
            ))}
          </select>
        </label>
        <button
          type="button"
          className="effect-editor__remove"
          onClick={onRemove}
          aria-label="remove effect"
        >
          ×
        </button>
      </div>
      <div className="effect-editor__fields">
        {fields.includes('scope') && (
          <label className="effect-editor__label">
            Scope
            <select
              value={effect.scope ?? ''}
              onChange={(e) => set({ scope: e.target.value || null })}
            >
              <option value="">(none)</option>
              {meta.effectScopes.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('side') && (
          <label className="effect-editor__label">
            Side
            <select
              value={effect.side ?? ''}
              onChange={(e) => set({ side: e.target.value || null })}
            >
              <option value="">(none)</option>
              {meta.effectSides.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('amount') && (
          <label className="effect-editor__label">
            Amount
            <input
              type="number"
              value={effect.amount}
              onChange={(e) => set({ amount: parseInt(e.target.value, 10) || 0 })}
              aria-label="effect amount"
            />
          </label>
        )}
        {fields.includes('amountSource') && (
          <label className="effect-editor__label">
            Amount Source
            <select
              value={effect.amountSource ?? ''}
              onChange={(e) => set({ amountSource: e.target.value || null })}
            >
              <option value="">(literal)</option>
              {meta.amountSources.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('name') && (
          <label className="effect-editor__label">
            Status
            <select
              value={effect.name ?? ''}
              onChange={(e) => set({ name: e.target.value || null })}
            >
              <option value="">(none)</option>
              {meta.statuses.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.id} ({s.jp})
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('pile') && (
          <label className="effect-editor__label">
            Pile
            <select
              value={effect.pile ?? ''}
              onChange={(e) => set({ pile: e.target.value || null })}
            >
              <option value="">(none)</option>
              {meta.piles.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('select') && (
          <label className="effect-editor__label">
            Select
            <select
              value={effect.select ?? ''}
              onChange={(e) => set({ select: e.target.value || null })}
            >
              <option value="">(none)</option>
              {meta.selectModes.map((m) => (
                <option key={m} value={m}>
                  {m}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('cardRefId') && (
          <label className="effect-editor__label">
            Card Ref
            <select
              value={effect.cardRefId ?? ''}
              onChange={(e) => set({ cardRefId: e.target.value || null })}
            >
              <option value="">(none)</option>
              {allCardIds.map((id) => (
                <option key={id} value={id}>
                  {id}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('trigger') && (
          <label className="effect-editor__label">
            Trigger
            <select
              value={effect.trigger ?? ''}
              onChange={(e) => set({ trigger: e.target.value || null })}
            >
              <option value="">(immediate)</option>
              {meta.triggers.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('comboMin') && (
          <label className="effect-editor__label">
            Combo Min
            <input
              type="number"
              value={effect.comboMin ?? ''}
              placeholder="(none)"
              onChange={(e) =>
                set({ comboMin: e.target.value ? parseInt(e.target.value, 10) : null })
              }
            />
          </label>
        )}
        {fields.includes('unitId') && (
          <label className="effect-editor__label">
            Unit ID
            <input
              type="text"
              value={effect.unitId ?? ''}
              placeholder="ally id"
              onChange={(e) => set({ unitId: e.target.value || null })}
            />
          </label>
        )}
      </div>
    </div>
  )
}
