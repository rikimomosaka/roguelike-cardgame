// src/Client/src/screens/BattleScreen.tsx
//
// Phase 10.3-MVP Task 14: 本番 API 接続版 BattleScreen。
//
// 旧 demo 実装 (battle-v10.html ポート) の DEFAULT_* と Props ベースの
// データ供給を撤廃し、accountId / onBattleResolved を受け取って
// /battle 系 API を呼び出すコンポーネントに変更。
//
// JSX レイアウト・サブコンポーネント (Relic / Potion / StatusBuff /
// IntentChip / Slot / HandCard) と CSS は demo 版から流用。
// データソースのみ DTO + Catalog → 中間型 (RelicDemo 等) 経由で差し替え。
//
// 中間型 (RelicDemo / CharacterDemo / HandCardDemo / BuffDemo / IntentDemo /
// HpLv) は dtoAdapter から参照できるよう export している。

import { useCallback, useEffect, useRef, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { Card } from '../components/Card'
import type { CardRarity, CardType } from '../components/Card'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import {
  endTurn,
  finalizeBattle,
  getBattle,
  playCard,
  setBattleTarget,
  startBattle,
  usePotion,
} from '../api/battle'
import type {
  ActorSide,
  BattleActionResponseDto,
  BattleStateDto,
  CombatActorDto,
  RunResultDto,
  RunSnapshotDto,
} from '../api/types'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { useEnemyCatalog } from '../hooks/useEnemyCatalog'
import { useUnitCatalog } from '../hooks/useUnitCatalog'
import { TopBar } from '../components/TopBar'
import {
  hpLevel,
  toCharacterDemo,
  toHandCardDemo,
} from './battleScreen/dtoAdapter'
import './BattleScreen.css'

// -------------------- Exported intermediate types --------------------

export type HpLv = '0' | '1' | '2' | '3'

export type RelicDemo = {
  icon: string
  rarity: CardRarity
  name: string
  desc: string
}

export type PotionDemo = {
  icon: string
  rarity?: CardRarity
  name?: string
  desc?: string
  empty?: boolean
  onClick?: () => void
}

export type BuffKind = 'block' | 'buff' | 'debuff'

export type BuffDemo = {
  kind: BuffKind
  icon: string
  num: number
  name: string
  desc: string
}

export type IntentKind = 'attack' | 'defend' | 'buff' | 'heal' | 'unknown'

/** Why: 攻撃チップは通常/ランダム/全体を 1 個に統合し、各数値を別色で表示する。
 *  num/icon は単一値だが、attack 用に分解された breakdown を別フィールドで持つ。 */
export type IntentAttackBreakdown = {
  single?: number
  random?: number
  all?: number
  hits?: number
}

export type IntentDemo = {
  kind: IntentKind
  icon: string
  num?: number
  name: string
  desc: string
  /** kind='attack' のとき per-scope 内訳 (色分け表示用)。 */
  attack?: IntentAttackBreakdown
}

export type SpriteKind = 'hero' | 'ally' | 'enemy' | 'elite'

export type CharacterDemo = {
  occupied: boolean
  name: string
  desc: string
  sprite: string
  spriteKind: SpriteKind
  hpCur: number
  hpMax: number
  hpLv: HpLv
  /** 単一の intent (敵の次行動)。intents が無いときの後方互換。 */
  intent?: IntentDemo
  /** 複数チップ表示 (hero の通常/ランダム/全体予定攻撃を分けて表示する用)。 */
  intents?: IntentDemo[]
  buffs: BuffDemo[]
}

export type HandCardDemo = {
  name: string
  cost: number | string
  /** コンボ軽減で cost と異なる元コスト。未軽減時は null/undefined。 */
  costOrig?: number | null
  type: CardType
  rarity: CardRarity
  art?: ReactNode
  playable?: boolean
  desc: string
}

// -------------------- Props --------------------

type Props = {
  accountId: string
  /** ラン全体の最新 snapshot。TopBar (gold/playSeconds/deck/relics) の表示用。 */
  snapshot: RunSnapshotDto
  onBattleResolved: (result: RunSnapshotDto | RunResultDto) => void
  /** TopBar の MAP ボタン押下で BattleScreen → MapScreen へ peek 切替する。 */
  onTogglePeek?: () => void
}

// -------------------- Animation timing --------------------

const STEP_DELAY_MS = 220

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

// -------------------- Layout helpers --------------------

// Fan layout for the player's hand. Values ported from battle-v10.html feel.
function fanLayout(count: number): { x: number; y: number; r: number }[] {
  if (count === 0) return []
  const step = 60 // px horizontal step
  const center = (count - 1) / 2
  const rotStep = 7 // degrees
  return Array.from({ length: count }, (_, i) => {
    const offset = i - center
    const x = offset * step
    const rot = offset * rotStep
    const y = Math.abs(offset) * 6
    return { x, y, r: rot }
  })
}

function padSlots(chars: CharacterDemo[], n: number): CharacterDemo[] {
  const out = [...chars]
  while (out.length < n) {
    out.push({
      occupied: false,
      name: '',
      desc: '',
      sprite: '',
      spriteKind: 'enemy',
      hpCur: 0,
      hpMax: 0,
      hpLv: '0',
      buffs: [],
    })
  }
  return out.slice(0, n)
}

// -------------------- Small sub-components --------------------

function useTip(c: TooltipContent | null) {
  return useTooltipTarget(c)
}

// Relic / Potion 内部コンポーネントは battle__hud 撤去 (TopBar 統合) で
// 不要になったため削除。RelicDemo / PotionDemo 型は他から参照されていれば残す。

function StatusBuff({ buff }: { buff: BuffDemo }) {
  const tip = useTip({ name: buff.name, desc: buff.desc })
  return (
    <span className={`status-buff status-buff--${buff.kind}`} {...tip}>
      {buff.icon}
      <span className="status-buff__num">{buff.num}</span>
    </span>
  )
}

function IntentChip({ intent }: { intent: IntentDemo }) {
  const tip = useTip({ name: intent.name, desc: intent.desc })
  return (
    <div className={`intent intent--${intent.kind}`} {...tip}>
      <span className="intent__icon">{intent.icon}</span>
      {intent.kind === 'attack' && intent.attack ? (
        // Why: 通常/ランダム/全体を 1 chip 内で色分け表示。slash は白、各数値は
        // 種類ごとの色 (single=現状色, random=オレンジ, all=赤)。
        <span className="intent__nums">
          {(() => {
            const segs: { v: number; k: 'single' | 'random' | 'all' }[] = []
            if (intent.attack.single && intent.attack.single > 0) segs.push({ v: intent.attack.single, k: 'single' })
            if (intent.attack.random && intent.attack.random > 0) segs.push({ v: intent.attack.random, k: 'random' })
            if (intent.attack.all && intent.attack.all > 0) segs.push({ v: intent.attack.all, k: 'all' })
            return segs.map((s, i) => (
              <span key={i}>
                {i > 0 ? <span className="intent__slash">/</span> : null}
                <span className={`intent__num intent__num--${s.k}`}>{s.v}</span>
              </span>
            ))
          })()}
        </span>
      ) : intent.num !== undefined ? (
        <span className="intent__num">{intent.num}</span>
      ) : null}
    </div>
  )
}

type SlotProps = {
  char: CharacterDemo
  isTargeted?: boolean
  onClick?: () => void
}

function Slot({ char, isTargeted, onClick }: SlotProps) {
  if (!char.occupied) {
    return <div className="battle__slot" data-occupied="0" />
  }
  const pct = Math.max(0, Math.min(100, (char.hpCur / char.hpMax) * 100))
  const hpFillStyle: CSSProperties = { width: `${pct}%` }
  const cls = `battle__slot${isTargeted ? ' is-targeted' : ''}`
  // Why: スプレッドにすることで onClick が undefined のとき role/tabIndex 属性自体が
  // JSX に出ない。axe/aria リンターの "role must be valid ARIA role: {expression}"
  // 誤検出（条件式値の静的解析不可）を回避する目的。
  const interactiveProps = onClick
    ? { onClick, role: 'button' as const, tabIndex: 0 }
    : {}
  return (
    <div
      className={cls}
      data-occupied="1"
      {...interactiveProps}
    >
      {char.intents && char.intents.length > 0 ? (
        <div className="intents">
          {char.intents.map((it, i) => (
            <span key={i} className="intents__seg">
              {i > 0 ? <span className="intents__sep">＆</span> : null}
              <IntentChip intent={it} />
            </span>
          ))}
        </div>
      ) : char.intent ? (
        <IntentChip intent={char.intent} />
      ) : null}
      <div
        className={`sprite sprite--${char.spriteKind}${char.sprite.length > 2 ? ' sprite--text' : ''}`}
      >
        {char.sprite}
      </div>
      <div className="sprite-shadow" />
      <div className="status-hp">
        <div className="status-hp__track" data-lv={char.hpLv}>
          <div className="status-hp__fill" style={hpFillStyle} />
          <span className="status-hp__num">
            {char.hpCur}/{char.hpMax}
          </span>
        </div>
      </div>
      {char.buffs.length > 0 ? (
        <div className="status-buffs">
          {char.buffs.map((b, i) => (
            <StatusBuff key={i} buff={b} />
          ))}
        </div>
      ) : null}
    </div>
  )
}

type HandCardProps = {
  card: HandCardDemo
  fan: { x: number; y: number; r: number }
  onClick?: () => void
}

function HandCard({ card, fan, onClick }: HandCardProps) {
  const tip = useTip({
    name: card.name,
    rarity: card.rarity,
    desc: card.desc,
  })

  // We need the rendered `.card` element to be a direct child of
  // `.hand` (CSS selector `.hand > .card` keys off this). Card.tsx
  // does not expose a ref or onMouseMove. We use a sibling probe
  // <span> (via ref-callback) to capture the previous DOM sibling —
  // which is the `.card` root — and imperatively apply CSS vars plus a
  // mousemove listener on it.
  const [cardEl, setCardEl] = useState<HTMLDivElement | null>(null)

  const probeRefCb = useCallback((probe: HTMLSpanElement | null) => {
    if (!probe) {
      setCardEl(null)
      return
    }
    const prev = probe.previousElementSibling
    if (prev instanceof HTMLDivElement && prev.classList.contains('card')) {
      setCardEl(prev)
    }
  }, [])

  useEffect(() => {
    if (!cardEl) return
    cardEl.style.setProperty('--fan-x', `${fan.x}px`)
    cardEl.style.setProperty('--fan-y', `${fan.y}px`)
    cardEl.style.setProperty('--fan-r', `${fan.r}deg`)
  }, [cardEl, fan.x, fan.y, fan.r])

  useEffect(() => {
    if (!cardEl) return
    const handler = (e: globalThis.MouseEvent) => {
      tip.onMouseMove({ clientX: e.clientX, clientY: e.clientY } as unknown as React.MouseEvent)
    }
    cardEl.addEventListener('mousemove', handler)
    return () => cardEl.removeEventListener('mousemove', handler)
  }, [cardEl, tip])

  return (
    <>
      <Card
        name={card.name}
        cost={card.cost}
        costOrig={card.costOrig}
        type={card.type}
        rarity={card.rarity}
        art={card.art}
        className={card.playable ? 'is-playable' : undefined}
        onClick={onClick}
        onMouseEnter={tip.onMouseEnter}
        onMouseLeave={tip.onMouseLeave}
      />
      <span ref={probeRefCb} aria-hidden="true" className="hand__probe" />
    </>
  )
}

// -------------------- Pile modal --------------------

type PileKind = 'draw' | 'discard' | 'exhaust'

type PileModalProps = {
  kind: PileKind
  cards: import('../api/types').BattleCardInstanceDto[]
  onClose: () => void
}

function PileModal({ kind, cards, onClose }: PileModalProps) {
  const { catalog } = useCardCatalog()
  const title = kind === 'draw' ? '山札' : kind === 'discard' ? '捨札' : '除外'
  // Why: 山札はシャッフル順を伏せるためカード名でソート (TopBar デッキ表示と
  // 同じ方式)。捨札 / 除外は実際の積み順を保持する。
  const ordered = kind === 'draw'
    ? [...cards].sort((a, b) => {
        const an = catalog?.[a.cardDefinitionId]?.displayName ?? catalog?.[a.cardDefinitionId]?.name ?? a.cardDefinitionId
        const bn = catalog?.[b.cardDefinitionId]?.displayName ?? catalog?.[b.cardDefinitionId]?.name ?? b.cardDefinitionId
        return an.localeCompare(bn, 'ja')
      })
    : cards
  return (
    <div className="pile-modal-backdrop" onClick={onClose} role="presentation">
      <div
        className="pile-modal"
        role="dialog"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        <header className="pile-modal__header">
          <span>
            {title} ({cards.length}枚)
            {kind === 'draw' ? (
              <span className="pile-modal__note"> ※山札の順番は不明</span>
            ) : null}
          </span>
          <button type="button" className="pile-modal__close" onClick={onClose} aria-label="閉じる">
            ×
          </button>
        </header>
        {ordered.length === 0 ? (
          <p className="pile-modal__empty">{title}は空です</p>
        ) : (
          <ul className="pile-modal__list">
            {ordered.map((card, i) => {
              const def = catalog?.[card.cardDefinitionId]
              const fallbackName = def?.displayName ?? def?.name ?? card.cardDefinitionId
              const disp = {
                name: fallbackName,
                cost: card.costOverride ?? def?.cost ?? 0,
                type: ((def?.cardType ?? 'attack').toLowerCase()) as CardType,
                rarity: ((['c','r','e','l'][def?.rarity ?? 0]) ?? 'c') as CardRarity,
                description: def?.description ?? '',
                upgradedDescription: def?.upgradedDescription ?? null,
              }
              return (
                <li key={`${card.instanceId}-${i}`} className="pile-modal__item">
                  <Card
                    name={disp.name}
                    cost={disp.cost}
                    type={disp.type}
                    rarity={disp.rarity}
                    description={disp.description}
                    upgradedDescription={disp.upgradedDescription}
                    upgraded={card.isUpgraded}
                    width={112}
                  />
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </div>
  )
}

/**
 * COMBO chip with hover tooltip.
 * Why: コンボ仕様 (元コスト+1 のカードを 1 軽減 + プレイで継続) を hover で
 * 確認できるようにする。superwild 中は「任意のカードでコンボ継続」と表現。
 */
function ComboChip({
  count,
  lastPlayedOrigCost,
  superwild,
}: {
  count: number
  lastPlayedOrigCost: number | null
  superwild: boolean
}) {
  const desc = (() => {
    if (count === 0) {
      return '現在０コンボ。カードをプレイすると、そのカードよりコストが1大きいカードのコストをターン中1軽減。'
    }
    const reduceTarget =
      lastPlayedOrigCost !== null ? `${lastPlayedOrigCost + 1}` : '?'
    if (superwild) {
      return `現在${count}コンボ。ターン中コストが${reduceTarget}のカードを1軽減し、任意のカードをプレイでコンボ継続。`
    }
    return `現在${count}コンボ。ターン中コストが${reduceTarget}のカードを1軽減し、それをプレイでコンボ継続。`
  })()
  const tip = useTip({ name: 'コンボ', desc })
  return (
    <div
      className={`battle__combo${count > 1 ? ' is-active' : ''}`}
      aria-label={`コンボ ×${count}`}
      {...tip}
    >
      <span className="battle__combo-k">COMBO</span>
      <span className="battle__combo-v">×{count}</span>
    </div>
  )
}

function EnergyOrb({ cur, max }: { cur: number; max: number }) {
  const tip = useTip({
    name: 'エナジー',
    desc: 'カードをプレイするのに必要なリソース。ターン開始時に最大値まで回復する。',
  })
  return (
    <div className="energy" {...tip}>
      <div className="energy__val">
        <span className="energy__cur">{cur}</span>
        <span className="energy__max">/{max}</span>
      </div>
    </div>
  )
}

// -------------------- Main component --------------------

export function BattleScreen({ accountId, snapshot, onBattleResolved, onTogglePeek }: Props) {
  const [state, setState] = useState<BattleStateDto | null>(null)
  const [animating, setAnimating] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pileOpen, setPileOpen] = useState<PileKind | null>(null)
  // resolvedRef は finalize 後の二重呼び出しを抑止するためのラッチ。
  const resolvedRef = useRef(false)

  const { catalog: cardCatalog } = useCardCatalog()
  const { catalog: enemyCatalog } = useEnemyCatalog()
  const { catalog: unitCatalog } = useUnitCatalog()

  // 戦闘終了 → /finalize 呼び出し → 親に通知。
  const handleFinalize = useCallback(async () => {
    if (resolvedRef.current) return
    resolvedRef.current = true
    try {
      const result = await finalizeBattle(accountId)
      onBattleResolved(result)
    } catch (e) {
      resolvedRef.current = false
      setError((e as Error).message)
    }
  }, [accountId, onBattleResolved])

  // BattleActionResponse を受け取り、各 step を 220ms 間隔で再生する。
  const playSteps = useCallback(
    async (resp: BattleActionResponseDto) => {
      setAnimating(true)
      for (const step of resp.steps) {
        setState(step.snapshotAfter)
        await sleep(STEP_DELAY_MS)
      }
      setState(resp.state)
      setAnimating(false)

      if (resp.state.outcome === 'Victory' || resp.state.outcome === 'Defeat') {
        await handleFinalize()
      }
    },
    [handleFinalize],
  )

  // 初期化: GET /battle → 既存ならそれを表示、なければ POST /start。
  useEffect(() => {
    let cancelled = false
    async function init() {
      try {
        const existing = await getBattle(accountId)
        if (cancelled) return
        if (existing) {
          setState(existing)
          if (existing.outcome === 'Victory' || existing.outcome === 'Defeat') {
            await handleFinalize()
          }
        } else {
          const resp = await startBattle(accountId)
          if (cancelled) return
          await playSteps(resp)
        }
      } catch (e) {
        if (!cancelled) setError((e as Error).message)
      }
    }
    void init()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accountId])

  async function withBusy<T>(fn: () => Promise<T>): Promise<T | null> {
    if (animating || busy) return null
    setBusy(true)
    setError(null)
    try {
      return await fn()
    } catch (e) {
      setError((e as Error).message)
      return null
    } finally {
      setBusy(false)
    }
  }

  async function handlePlayCard(handIndex: number) {
    // Why: コスト不足 / 範囲外の hand クリックでサーバー 400 が頻発するのは UX
    // 上ノイズなので、Client 側で事前にプレイ可否を判定して握り潰す。Server は
    // engine ロジックで無効化されるが、わざわざ呼ばないのが望ましい。
    const card = state?.hand[handIndex]
    if (!card) return
    const def = cardCatalog?.[card.cardDefinitionId]
    const orig = card.isUpgraded
      ? def?.upgradedCost ?? def?.cost
      : def?.cost
    const baseCost = card.costOverride ?? orig
    if (baseCost === null || baseCost === undefined) return
    // combo 軽減も考慮した支払いコスト
    const willCombo =
      orig !== null && orig !== undefined &&
      state?.lastPlayedOrigCost !== null && state?.lastPlayedOrigCost !== undefined &&
      orig === state.lastPlayedOrigCost + 1
    const payCost = Math.max(0, baseCost - (willCombo ? 1 : 0))
    if (state && payCost > state.energy) return
    const resp = await withBusy(() => playCard(accountId, { handIndex }))
    if (resp) await playSteps(resp)
  }

  async function handleEndTurn() {
    const resp = await withBusy(() => endTurn(accountId))
    if (resp) await playSteps(resp)
  }

  async function handleUsePotion(potionIndex: number) {
    const resp = await withBusy(() => usePotion(accountId, { potionIndex }))
    if (resp) await playSteps(resp)
  }

  async function handleSetTarget(side: ActorSide, slotIndex: number) {
    const newState = await withBusy(() =>
      setBattleTarget(accountId, { side, slotIndex }),
    )
    if (newState) setState(newState)
  }

  // ------------- Render -------------

  if (state === null) {
    return (
      <div className="battle">
        <div className="battle__pattern" />
        <div className="battle__loading">戦闘準備中…</div>
        {error ? <div className="battle__error">{error}</div> : null}
      </div>
    )
  }

  // データソース変換
  const heroActor = state.allies.find(
    (a) => a.definitionId === 'hero',
  ) as CombatActorDto | undefined
  const heroHp = heroActor
    ? {
        cur: heroActor.currentHp,
        max: heroActor.maxHp,
        lv: hpLevel(heroActor.currentHp, heroActor.maxHp),
      }
    : { cur: 0, max: 0, lv: '0' as const }
  // Why: HP / relics / potions は TopBar に集約済 (battle__hud 撤去済)。
  // 旧 demo 用の hpPct / relicDemos / potionDemos は不要になったため削除。

  // Why: BattleEngine は死亡した actor を state.Allies / Enemies から削除せず、
  // CurrentHp=0 のまま残す (.Where(a => a.IsAlive) で都度フィルタ)。Client 側でも
  // 表示前にフィルタして「死んだのに残り続ける」現象を回避する。
  const aliveAllies = state.allies.filter((a) => a.currentHp > 0)
  const aliveEnemies = state.enemies.filter((e) => e.currentHp > 0)

  // 4 スロット zero-pad で side ごとに描画。
  const allyDemos = aliveAllies.map((a) =>
    toCharacterDemo(a, { enemies: enemyCatalog, units: unitCatalog }),
  )
  const enemyDemos = aliveEnemies.map((e) =>
    toCharacterDemo(e, { enemies: enemyCatalog, units: unitCatalog }),
  )
  const playerSlots = padSlots(allyDemos, 4)
  const enemySlots = padSlots(enemyDemos, 4)

  const handDemos = state.hand.map((c) =>
    toHandCardDemo(c, cardCatalog, state.energy, state.lastPlayedOrigCost),
  )
  const fan = fanLayout(handDemos.length)

  const interactionsDisabled = animating || busy

  return (
    <div className="battle-screen">
      {/* Why: ユーザ要望「上のバーは map 画面のものをそのまま」。
          HP は live battle override (heroHp.cur)、potion 操作は battle 内で
          discard 不可なので onDiscardPotion は no-op。
          TopBar は .battle の外側に置くことで stage の intent chip 等と
          視覚的に重ならないようにする。 */}
      <TopBar
        currentHp={heroHp.cur}
        maxHp={heroHp.max}
        gold={snapshot.run.gold}
        potions={state.potions}
        deck={snapshot.run.deck}
        relics={state.ownedRelicIds}
        onDiscardPotion={() => { /* battle 中は discard 不可 */ }}
        onUsePotion={(i) => void handleUsePotion(i)}
        onOpenMenu={() => { /* battle 中の menu は後続フェーズで対応 */ }}
        onTogglePeek={onTogglePeek}
        peekActive={false}
        peekDisabled={!onTogglePeek}
        playSeconds={snapshot.run.playSeconds}
      />
      <div className="battle">
        <div className="battle__pattern" />
        <div className="battle__content">
        {/* Stage */}
        <div className="battle__stage">
          <div className="battle__chars">
            <div className="battle__side battle__side--player">
              {playerSlots.map((c, i) => {
                // Why: 死亡 actor を除外した aliveAllies に対する index で参照する。
                // state.allies[i] では死亡 actor を含むため整合しない。
                const actor = aliveAllies[i]
                const isTargeted =
                  actor !== undefined &&
                  state.targetAllyIndex === actor.slotIndex
                // Why: hero クリックは「targetAllyIndex を hero (slot 0) に
                // 戻す」操作として有効化する。召喚 ally を選択した後に hero へ
                // 戻せない不具合への対応。targetEnemyIndex も同時にクリアして
                // ターゲット状態をリセットする (現状 SetTarget は片方しか動かさ
                // ないのでそのまま敵スロット 0 に戻すか、味方 0 だけ戻すかは
                // Engine 仕様に従う。ここでは Ally,0 を呼ぶことで味方ターゲットを
                // hero に戻す挙動を提供)。
                const onClick =
                  c.occupied && actor && !interactionsDisabled
                    ? () => void handleSetTarget('Ally', actor.slotIndex)
                    : undefined
                return (
                  <Slot
                    key={`p-${i}`}
                    char={c}
                    isTargeted={isTargeted}
                    onClick={onClick}
                  />
                )
              })}
            </div>
            <div className="battle__side battle__side--enemy">
              {enemySlots.map((c, i) => {
                const actor = aliveEnemies[i]
                const isTargeted =
                  actor !== undefined &&
                  state.targetEnemyIndex === actor.slotIndex
                const onClick =
                  c.occupied && actor && !interactionsDisabled
                    ? () => void handleSetTarget('Enemy', actor.slotIndex)
                    : undefined
                return (
                  <Slot
                    key={`e-${i}`}
                    char={c}
                    isTargeted={isTargeted}
                    onClick={onClick}
                  />
                )
              })}
            </div>
          </div>

          {/* Energy orb */}
          <EnergyOrb cur={state.energy} max={state.energyMax} />

          {/* Piles (clickable to open modal) */}
          <button
            type="button"
            className="pile pile--draw"
            onClick={() => setPileOpen('draw')}
            aria-label={`山札 (${state.drawPile.length}枚) を表示`}
          >
            <span className="pile__sym">❖</span>
            <span className="pile__lbl">山札</span>
            <span className="pile__num">{state.drawPile.length}</span>
          </button>
          <button
            type="button"
            className="pile pile--exhaust"
            onClick={() => setPileOpen('exhaust')}
            aria-label={`除外 (${state.exhaustPile.length}枚) を表示`}
          >
            <span className="pile__sym">✦</span>
            <span className="pile__lbl">除外</span>
            <span className="pile__num">{state.exhaustPile.length}</span>
          </button>
          <button
            type="button"
            className="pile pile--discard"
            onClick={() => setPileOpen('discard')}
            aria-label={`捨札 (${state.discardPile.length}枚) を表示`}
          >
            <span className="pile__sym">✕</span>
            <span className="pile__lbl">捨札</span>
            <span className="pile__num">{state.discardPile.length}</span>
          </button>

          {/* COMBO chip + Turn end */}
          <ComboChip
            count={state.comboCount}
            lastPlayedOrigCost={state.lastPlayedOrigCost}
            superwild={state.nextCardComboFreePass}
          />
          <button
            type="button"
            className="end-turn"
            onClick={() => void handleEndTurn()}
            disabled={interactionsDisabled}
          >
            TURN END
          </button>

          {/* Hand (fanned) */}
          <div className="hand-wrap">
            <div className="hand">
              {handDemos.map((c, i) => (
                <HandCard
                  key={i}
                  card={c}
                  fan={fan[i]}
                  onClick={
                    interactionsDisabled
                      ? undefined
                      : () => void handlePlayCard(i)
                  }
                />
              ))}
            </div>
          </div>
        </div>

        {error ? <div className="battle__error">{error}</div> : null}
        </div>
      </div>
      {pileOpen ? (
        <PileModal
          kind={pileOpen}
          cards={
            pileOpen === 'draw' ? state.drawPile
              : pileOpen === 'discard' ? state.discardPile
              : state.exhaustPile
          }
          onClose={() => setPileOpen(null)}
        />
      ) : null}
    </div>
  )
}

