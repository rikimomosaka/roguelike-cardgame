// Phase 10.5.M: action 連動の動的 field を持つ単一 effect エディタ。
// EFFECT_ACTION_FIELDS で action ごとに表示する field を切り替える。

import { useEffect } from 'react'
import type { CardEffect } from './DevSpecTypes'
import { EFFECT_ACTION_FIELDS, PASSIVE_ONLY_ACTIONS, UNKNOWN_TILE_KINDS } from './DevSpecTypes'
import type { DevMeta } from '../../api/dev'

type Props = {
  effect: CardEffect
  meta: DevMeta
  allCardIds: string[]
  onChange: (e: CardEffect) => void
  onRemove: () => void
  /** Why: relic editor では trigger / comboMin を effect レベルで持たないので、
   *  特定 field を非表示にできる。RelicSpecForm から ['trigger', 'comboMin'] を渡す。 */
  excludeFields?: ReadonlyArray<keyof CardEffect>
}

export function EffectEditor({ effect, meta, allCardIds, onChange, onRemove, excludeFields }: Props) {
  const allFields = EFFECT_ACTION_FIELDS[effect.action] ?? []
  const fields = excludeFields && excludeFields.length > 0
    ? allFields.filter((f) => !excludeFields.includes(f))
    : allFields

  const set = (patch: Partial<CardEffect>) => onChange({ ...effect, ...patch })

  // Phase 10.6.B follow-up: 既存 effect が壊れたデータ (scope や trigger が新ルールで正しくない)
  // で読み込まれた場合に自動修正。例: gainEnergy の scope='single' (default のまま) を 'self' に直す。
  useEffect(() => {
    const patch: Partial<CardEffect> = {}
    let needsUpdate = false
    if (!allFields.includes('scope') && effect.scope !== 'self') {
      patch.scope = 'self'
      patch.side = null
      needsUpdate = true
    }
    if (PASSIVE_ONLY_ACTIONS.has(effect.action) && effect.trigger !== 'Passive') {
      patch.trigger = 'Passive'
      needsUpdate = true
    }
    if (needsUpdate) onChange({ ...effect, ...patch })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [effect.action])

  return (
    <div className="effect-editor">
      <div className="effect-editor__row effect-editor__row--header">
        <label className="effect-editor__label">
          アクション
          <select
            value={effect.action}
            onChange={(e) => {
              const newAction = e.target.value
              const patch: Partial<CardEffect> = { action: newAction }
              // Phase 10.6.B: passive-only action を選んだ時は trigger を自動的に "Passive" にセット
              if (PASSIVE_ONLY_ACTIONS.has(newAction)) {
                patch.trigger = 'Passive'
              }
              // Phase 10.6.B follow-up: 'scope' を edit field に持たない action は
              // engine 側で Self を要求するため (例: gainEnergy / draw / summon 等)、
              // scope='self' を強制セットして 400 エラーを防ぐ。
              const newFields = EFFECT_ACTION_FIELDS[newAction] ?? []
              if (!newFields.includes('scope')) {
                patch.scope = 'self'
                patch.side = null
              }
              set(patch)
            }}
            aria-label="effect action"
          >
            {meta.effectActions.map((a) => (
              <option key={a} value={a}>
                {actionJp(a)}
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
        {fields.includes('name') && effect.action === 'unknownTileWeightDelta' && (
          <>
            <label className="effect-editor__label">
              タイル種別
              <select
                value={effect.name ?? ''}
                onChange={(e) => set({ name: e.target.value || null })}
              >
                <option value="">(なし)</option>
                {UNKNOWN_TILE_KINDS.map((k) => (
                  <option key={k} value={k}>
                    {tileKindJp(k)}
                  </option>
                ))}
              </select>
            </label>
            <div className="effect-editor__hint">
              <div><strong>Act 1 ベース重み</strong> (合計 100): 敵 25 / エリート 10 / ショップ 15 / 休憩 25 / 宝箱 <strong>0</strong> / イベント 25</div>
              <div>delta +N で重み加算。<strong>確率 = (base + delta) / (100 + Σdelta)</strong></div>
              <div>例: 宝箱 +20 → 20/120 ≈ <strong>16.7%</strong>、宝箱 +100 → 100/200 = <strong>50%</strong>、敵 +50 → 75/150 = <strong>50%</strong></div>
            </div>
          </>
        )}
        {fields.includes('name') && effect.action !== 'unknownTileWeightDelta' && (
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
    // Server meta endpoint は lowercase で返す。後方互換のため capital も拾う。
    case 'self': case 'Self': return '自分 (self)'
    case 'single': case 'Single': return '単体 (single)'
    case 'random': case 'Random': return 'ランダム (random)'
    case 'all': case 'All': return '全体 (all)'
    default: return s
  }
}

function sideJp(s: string): string {
  switch (s) {
    case 'enemy': case 'Enemy': return '敵 (enemy)'
    case 'ally': case 'Ally': return '味方 (ally)'
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
  // Phase 10.5.L1.5: relic + power 統合 unified trigger の JP ラベル。
  switch (t) {
    case 'OnPickup': return 'レリック取得時'
    case 'OnBattleStart': return '戦闘開始時'
    case 'OnBattleEnd': return '戦闘終了時'
    case 'OnTurnStart': return 'ターン開始時'
    case 'OnTurnEnd': return 'ターン終了時'
    case 'OnPlayCard': return 'カードプレイ時'
    case 'OnEnemyDeath': return '敵撃破時'
    case 'OnDamageReceived': return 'ダメージ受け時'
    case 'OnCombo': return 'コンボ達成時'
    case 'OnMapTileResolved': return 'マスイベント解決後'
    case 'OnCardDiscarded': return 'カード捨て時'
    case 'OnCardExhausted': return 'カード除外時'
    case 'OnEnterShop': return 'ショップ訪問時'
    case 'OnEnterRestSite': return '休憩所訪問時'
    case 'OnRest': return '休憩時'
    case 'OnRewardGenerated': return '報酬生成時'
    case 'OnCardAddedToDeck': return 'デッキ追加時'
    case 'Passive': return '常時'
    default: return t
  }
}

function actionJp(a: string): string {
  switch (a) {
    // バトル系 (card / power / relic 共通)
    case 'attack':            return `アタック (${a})`
    case 'block':             return `ブロック (${a})`
    case 'buff':              return `バフ付与 (${a})`
    case 'debuff':            return `デバフ付与 (${a})`
    case 'heal':              return `回復 (${a})`
    case 'draw':              return `ドロー (${a})`
    case 'discard':           return `カード捨て (${a})`
    case 'gainEnergy':        return `エナジー獲得 (${a})`
    case 'gainMaxEnergy':     return `最大エナジー増 (${a})`
    case 'exhaustCard':       return `カード消去 (${a})`
    case 'upgrade':           return `カード強化 (${a})`
    case 'summon':            return `召喚 (${a})`
    case 'selfDamage':        return `自傷 (${a})`
    case 'addCard':           return `カード追加 (${a})`
    case 'recoverFromDiscard':return `捨札から戻す (${a})`
    // Phase 10.6.B Passive modifier 系
    case 'energyPerTurnBonus':       return `エナジー最大値 + (${a})`
    case 'cardsDrawnPerTurnBonus':   return `毎ターン手札枚数 + (${a})`
    case 'goldRewardMultiplier':     return `戦闘ゴールド報酬 % (${a})`
    case 'shopPriceMultiplier':      return `ショップ価格 % (${a})`
    case 'rewardCardChoicesBonus':   return `カード報酬選択肢 + (${a})`
    case 'rewardRerollAvailable':    return `カード報酬リロール可 (${a})`
    case 'unknownTileWeightDelta':   return `ハテナマス重み補正 (${a})`
    case 'restHealBonus':            return `休憩所での回復 + (${a})`
    default: return a
  }
}

function tileKindJp(k: string): string {
  switch (k) {
    case 'enemy':    return '敵戦闘 (enemy)'
    case 'elite':    return 'エリート戦闘 (elite)'
    case 'merchant': return 'ショップ (merchant)'
    case 'rest':     return '休憩所 (rest)'
    case 'treasure': return '宝箱 (treasure)'
    case 'event':    return 'イベント (event)'
    default: return k
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
