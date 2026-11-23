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

const PARTICIPANT_RESOURCE_ACTIONS = new Set([
  'draw',
  'drawCards',
  'gainEnergy',
  'gainComboGauge',
  'gainMaxEnergy',
  'modifyCardCost',
])

const PILES_BY_ACTION: Record<string, readonly string[]> = {
  exhaustCard: ['hand', 'draw', 'discard'],
  recoverFromDiscard: ['hand', 'exhaust'],
  recoverFromExhaust: ['hand', 'draw', 'discard'],
  upgrade: ['hand', 'draw', 'discard', 'exhaust'],
  addCard: ['hand', 'draw', 'discard', 'exhaust'],
  modifyCardCost: ['hand', 'draw', 'discard', 'exhaust'],
}

export function EffectEditor({ effect, meta, allCardIds, onChange, onRemove, excludeFields }: Props) {
  const allFields = EFFECT_ACTION_FIELDS[effect.action] ?? []
  const fields = excludeFields && excludeFields.length > 0
    ? allFields.filter((f) => !excludeFields.includes(f))
    : allFields

  const set = (patch: Partial<CardEffect>) => onChange({ ...effect, ...patch })
  const groupedStatuses = effect.statusChanges ?? (
    effect.name
      ? [{ name: effect.name, amount: effect.amount }]
      : []
  )
  const statusKind = effect.action === 'debuff' ? 'debuff' : 'buff'
  const availableStatuses = meta.statuses.filter(
    status => status.kind.toLowerCase() === statusKind,
  )
  const unusedStatuses = availableStatuses.filter(status =>
    !groupedStatuses.some(change => change.name === status.id),
  )
  const setGroupedStatuses = (statusChanges: CardEffect['statusChanges']) => set({
    statusChanges,
    name: null,
    amount: 0,
    amountSource: null,
  })

  // scope を持たない action は self 固定。参加者リソースを操作する action は
  // self のほか味方参加者を対象にできるため、読み込んだ scope/side を維持する。
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
              if (newAction !== 'modifyCardCost' && effect.amountSource === 'currentCost') {
                patch.amountSource = null
                if (effect.amount === 0) patch.amount = 1
              }
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
              } else if (PARTICIPANT_RESOURCE_ACTIONS.has(newAction)) {
                // 別 action の enemy target を引き継がず、安全な self を初期値にする。
                patch.scope = 'self'
                patch.side = null
              }
              if (newAction === 'modifyCardCost') {
                patch.pile = validPiles(newAction, meta.piles).includes(effect.pile ?? '')
                  ? effect.pile
                  : 'hand'
                patch.select = effect.select ?? 'choose'
                patch.cardCount = effect.cardCount ?? 1
                patch.costDuration = effect.costDuration ?? 'untilPlayed'
                patch.amount = effect.amount === 0 ? -1 : effect.amount
              } else if (newFields.includes('pile')) {
                const piles = validPiles(newAction, meta.piles)
                patch.pile = piles.includes(effect.pile ?? '') ? effect.pile : piles[0] ?? null
              }
              if (newFields.includes('select') && newAction !== 'modifyCardCost') {
                patch.select = effect.select ?? 'choose'
              }
              if (newAction === 'buff' || newAction === 'debuff') {
                const kind = newAction === 'debuff' ? 'debuff' : 'buff'
                const first = meta.statuses.find(status => status.kind.toLowerCase() === kind)
                patch.statusChanges = first ? [{ name: first.id, amount: 1 }] : []
                patch.name = null
                patch.amount = 0
                patch.amountSource = null
              } else {
                patch.statusChanges = null
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
              onChange={(e) => {
                const scope = e.target.value || null
                if (PARTICIPANT_RESOURCE_ACTIONS.has(effect.action)) {
                  const nonSelf = scope !== 'self' && scope !== 'Self'
                  set({
                    scope,
                    side: nonSelf ? 'ally' : null,
                    ...(effect.action === 'modifyCardCost' && nonSelf && effect.select === 'choose'
                      ? { select: 'all', cardCount: null }
                      : {}),
                  })
                } else {
                  set({ scope })
                }
              }}
              aria-label="effect scope"
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
              aria-label="effect side"
            >
              <option value="">(なし)</option>
              {meta.effectSides
                .filter((s) => !PARTICIPANT_RESOURCE_ACTIONS.has(effect.action)
                  || s.toLowerCase() === 'ally')
                .map((s) => (
                <option key={s} value={s}>
                  {sideJp(s)}
                </option>
                ))}
            </select>
          </label>
        )}
        {fields.includes('statusChanges') && (
          <div className="effect-editor__status-group">
            <span className="effect-editor__status-group-title">
              {effect.action === 'debuff' ? '付与するデバフ' : '付与するバフ'}
            </span>
            {groupedStatuses.map((change, index) => (
              <div className="effect-editor__status-change" key={`${change.name}:${index}`}>
                <select
                  aria-label={`status change ${index + 1}`}
                  value={change.name}
                  onChange={(event) => setGroupedStatuses(groupedStatuses.map((entry, itemIndex) =>
                    itemIndex === index ? { ...entry, name: event.target.value } : entry,
                  ))}
                >
                  {availableStatuses
                    .filter(status => status.id === change.name
                      || !groupedStatuses.some((entry, itemIndex) =>
                        itemIndex !== index && entry.name === status.id))
                    .map(status => (
                    <option key={status.id} value={status.id}>{status.jp}</option>
                    ))}
                </select>
                <input
                  aria-label={`status amount ${index + 1}`}
                  type="number"
                  value={change.amount}
                  onChange={(event) => setGroupedStatuses(groupedStatuses.map((entry, itemIndex) =>
                    itemIndex === index
                      ? { ...entry, amount: Number.parseInt(event.target.value, 10) || 0 }
                      : entry,
                  ))}
                />
                <button
                  type="button"
                  aria-label={`remove status change ${index + 1}`}
                  disabled={groupedStatuses.length <= 1}
                  onClick={() => setGroupedStatuses(groupedStatuses.filter((_, itemIndex) => itemIndex !== index))}
                >
                  削除
                </button>
              </div>
            ))}
            <button
              type="button"
              className="content-json-add"
              disabled={unusedStatuses.length === 0}
              onClick={() => {
                const unused = unusedStatuses[0]
                if (unused) setGroupedStatuses([...groupedStatuses, { name: unused.id, amount: 1 }])
              }}
            >
              ＋ ステータスを追加
            </button>
          </div>
        )}
        {fields.includes('amount') && (
          <label className="effect-editor__label">
            数値
            <input
              type="number"
              value={effect.amount}
              disabled={effect.amountSource === 'currentCost'}
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
              onChange={(e) => {
                const amountSource = e.target.value || null
                set({
                  amountSource,
                  ...(amountSource === 'currentCost'
                    ? { amount: 0 }
                    : effect.amountSource === 'currentCost' ? { amount: -1 } : {}),
                })
              }}
              aria-label="effect amount source"
            >
              <option value="">(数値リテラル)</option>
              {meta.amountSources
                .filter(source => source !== 'currentCost' || effect.action === 'modifyCardCost')
                .map((s) => (
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
              aria-label="effect pile"
            >
              <option value="">(なし)</option>
              {validPiles(effect.action, meta.piles).map((p) => (
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
              onChange={(e) => {
                const select = e.target.value || null
                set({
                  select,
                  ...(select === 'all' ? { cardCount: null } : {}),
                })
              }}
              aria-label="effect select"
            >
              <option value="">(なし)</option>
              {meta.selectModes
                .filter(mode => mode !== 'choose'
                  || effect.action !== 'modifyCardCost'
                  || ((effect.scope === 'self' || effect.scope === 'Self') && !effect.trigger))
                .map((m) => (
                <option key={m} value={m}>
                  {selectJp(m)}
                </option>
              ))}
            </select>
          </label>
        )}
        {fields.includes('cardCount') && effect.select !== 'all' && (
          <label className="effect-editor__label">
            対象カード枚数
            <input
              type="number"
              min={1}
              value={effect.cardCount ?? 1}
              onChange={(e) => set({
                cardCount: Math.max(1, parseInt(e.target.value, 10) || 1),
              })}
              aria-label="effect card count"
            />
          </label>
        )}
        {fields.includes('costDuration') && (
          <label className="effect-editor__label">
            コスト変更の期限
            <select
              value={effect.costDuration ?? 'untilPlayed'}
              onChange={(e) => set({ costDuration: e.target.value || null })}
              aria-label="effect cost duration"
            >
              <option value="untilPlayed">使用するまで</option>
              <option value="untilTurnEnd">このターン終了まで</option>
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
              onChange={(e) => {
                const trigger = e.target.value || null
                set({
                  trigger,
                  chancePercent: trigger === 'OnHit' ? effect.chancePercent ?? 100 : null,
                  ...(effect.action === 'modifyCardCost' && trigger && effect.select === 'choose'
                    ? { select: 'all', cardCount: null }
                    : {}),
                })
              }}
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
        {fields.includes('trigger') && effect.trigger === 'OnHit' && (
          <label className="effect-editor__label">
            発動確率（%）
            <input
              type="number"
              min={0}
              max={100}
              value={effect.chancePercent ?? 100}
              onChange={(e) => {
                const value = Math.max(0, Math.min(100, parseInt(e.target.value, 10) || 0))
                set({ chancePercent: value })
              }}
              aria-label="effect chance percent"
            />
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
        {fields.includes('maxComboMin') && (
          <label className="effect-editor__label">
            乗勢（最大コンボ最小値）
            <input
              type="number"
              min={1}
              value={effect.maxComboMin ?? ''}
              placeholder="(なし)"
              onChange={(e) => set({
                maxComboMin: e.target.value
                  ? Math.max(1, parseInt(e.target.value, 10) || 1)
                  : null,
              })}
              aria-label="effect maximum combo minimum"
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
        {fields.includes('battleOnly') && (
          <label className="effect-editor__label">
            戦闘中のみ
            <input
              type="checkbox"
              checked={effect.battleOnly}
              onChange={(e) => set({ battleOnly: e.target.checked })}
              aria-label="effect battle only"
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
    case 'OnHit': return '攻撃命中時'
    case 'OnCombo': return 'コンボ達成時'
    case 'OnMapTileResolved': return 'マスイベント解決後'
    case 'OnCardDiscarded': return 'カード捨て時'
    case 'OnCardExhausted': return 'カード除外時'
    case 'OnEnterShop': return 'ショップ訪問時'
    case 'OnEnterRestSite': return '休憩所訪問時'
    case 'OnRest': return '休息時'
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
    case 'gainComboGauge':    return `コンボゲージ獲得 (${a})`
    case 'gainMaxEnergy':     return `ターン開始時エナジー追加量増 (${a})`
    case 'exhaustCard':       return `カード消去 (${a})`
    case 'upgrade':           return `カード強化 (${a})`
    case 'summon':            return `召喚 (${a})`
    case 'selfDamage':        return `自傷 (${a})`
    case 'addCard':           return `カード追加 (${a})`
    case 'recoverFromDiscard':return `捨札から戻す (${a})`
    case 'recoverFromExhaust':return `除外から戻す (${a})`
    case 'modifyCardCost':     return `カードコスト変更 (${a})`
    case 'gainMaxHp':          return `最大HP増加 (${a})`
    case 'gainGold':           return `ゴールド獲得 (${a})`
    case 'healHp':             return `HP回復 (${a})`
    // Phase 10.6.B Passive modifier 系
    case 'energyPerTurnBonus':       return `ターン開始時エナジー回復量 + (${a})`
    case 'cardsDrawnPerTurnBonus':   return `毎ターン手札枚数 + (${a})`
    case 'goldRewardMultiplier':     return `戦闘終了時のゴールド報酬 % (${a})`
    case 'shopPriceMultiplier':      return `ショップ価格 % (${a})`
    case 'rewardCardChoicesBonus':   return `カード報酬選択肢 + (${a})`
    case 'rewardRerollAvailable':    return `カード報酬リロール可 (${a})`
    case 'unknownTileWeightDelta':   return `ハテナマス重み補正 (${a})`
    case 'restHealBonus':            return `休息時の回復量 + (${a})`
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
    case 'maxComboThisTurn': return 'このターンの最大コンボ'
    case 'energy': return '現在のエナジー'
    case 'powerCardCount': return 'パワーカード数'
    case 'selfAttack': return '自身の攻撃力'
    case 'selfDefense': return '自身の守備力'
    case 'currentCost': return '対象カードの現在コスト（0にする）'
    default: return s
  }
}

function validPiles(action: string, allPiles: string[]): string[] {
  const allowed = PILES_BY_ACTION[action]
  return allowed
    ? allPiles.filter(pile => allowed.includes(pile))
    : allPiles
}
