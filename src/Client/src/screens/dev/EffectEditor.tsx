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
          アクション
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
            対象範囲
            <select
              value={effect.scope ?? ''}
              onChange={(e) => set({ scope: e.target.value || null })}
            >
              <option value="">(なし)</option>
              {meta.effectScopes.map((s) => (
                <option key={s} value={s}>
                  {scopeJp(s)}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('side') && (
          <label className="effect-editor__label">
            陣営
            <select
              value={effect.side ?? ''}
              onChange={(e) => set({ side: e.target.value || null })}
            >
              <option value="">(なし)</option>
              {meta.effectSides.map((s) => (
                <option key={s} value={s}>
                  {sideJp(s)}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('amount') && (
          <label className="effect-editor__label">
            数値
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
            変数 X
            <select
              value={effect.amountSource ?? ''}
              onChange={(e) => set({ amountSource: e.target.value || null })}
            >
              <option value="">(数値リテラル)</option>
              {meta.amountSources.map((s) => (
                <option key={s} value={s}>
                  {amountSourceJp(s)}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('name') && (
          <label className="effect-editor__label">
            ステータス
            <select
              value={effect.name ?? ''}
              onChange={(e) => set({ name: e.target.value || null })}
            >
              <option value="">(なし)</option>
              {meta.statuses.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.jp}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('pile') && (
          <label className="effect-editor__label">
            山
            <select
              value={effect.pile ?? ''}
              onChange={(e) => set({ pile: e.target.value || null })}
            >
              <option value="">(なし)</option>
              {meta.piles.map((p) => (
                <option key={p} value={p}>
                  {pileJp(p)}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('select') && (
          <label className="effect-editor__label">
            選択方式
            <select
              value={effect.select ?? ''}
              onChange={(e) => set({ select: e.target.value || null })}
            >
              <option value="">(なし)</option>
              {meta.selectModes.map((m) => (
                <option key={m} value={m}>
                  {selectJp(m)}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('cardRefId') && (
          <label className="effect-editor__label">
            参照カード
            <select
              value={effect.cardRefId ?? ''}
              onChange={(e) => set({ cardRefId: e.target.value || null })}
            >
              <option value="">(なし)</option>
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
            発火タイミング
            <select
              value={effect.trigger ?? ''}
              onChange={(e) => set({ trigger: e.target.value || null })}
            >
              <option value="">(即時)</option>
              {meta.triggers.map((t) => (
                <option key={t} value={t}>
                  {triggerJp(t)}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('comboMin') && (
          <label className="effect-editor__label">
            コンボ最小値
            <input
              type="number"
              value={effect.comboMin ?? ''}
              placeholder="(なし)"
              onChange={(e) =>
                set({ comboMin: e.target.value ? parseInt(e.target.value, 10) : null })
              }
            />
          </label>
        )}
        {fields.includes('unitId') && (
          <label className="effect-editor__label">
            召喚ユニット ID
            <input
              type="text"
              value={effect.unitId ?? ''}
              placeholder="例: wisp"
              onChange={(e) => set({ unitId: e.target.value || null })}
            />
          </label>
        )}
      </div>
    </div>
  )
}

// ---- 日本語ラベル変換ヘルパ ----

function scopeJp(s: string): string {
  switch (s) {
    case 'Self': return '自分 (Self)'
    case 'Single': return '対象 1 体 (Single)'
    case 'Random': return 'ランダム 1 体 (Random)'
    case 'All': return '全体 (All)'
    default: return s
  }
}

function sideJp(s: string): string {
  switch (s) {
    case 'Enemy': return '敵 (Enemy)'
    case 'Ally': return '味方 (Ally)'
    default: return s
  }
}

function pileJp(p: string): string {
  switch (p) {
    case 'hand': return '手札 (hand)'
    case 'draw': return '山札 (draw)'
    case 'discard': return '捨札 (discard)'
    case 'exhaust': return '除外 (exhaust)'
    default: return p
  }
}

function selectJp(m: string): string {
  switch (m) {
    case 'random': return 'ランダム (random)'
    case 'choose': return '選択 (choose)'
    case 'all': return '全て (all)'
    default: return m
  }
}

function triggerJp(t: string): string {
  switch (t) {
    case 'OnTurnStart': return 'ターン開始時'
    case 'OnPlayCard': return 'カードプレイ時'
    case 'OnDamageReceived': return 'ダメージ受け時'
    case 'OnCombo': return 'コンボ達成時'
    default: return t
  }
}

function amountSourceJp(s: string): string {
  switch (s) {
    case 'handCount': return '手札の数'
    case 'drawPileCount': return '山札の数'
    case 'discardPileCount': return '捨札の数'
    case 'exhaustPileCount': return '除外の数'
    case 'selfHp': return '自身のHP'
    case 'selfHpLost': return '失った HP'
    case 'selfBlock': return '自身のブロック'
    case 'comboCount': return '現在のコンボ'
    case 'energy': return '現在のエナジー'
    case 'powerCardCount': return 'パワーカード数'
    default: return s
  }
}
