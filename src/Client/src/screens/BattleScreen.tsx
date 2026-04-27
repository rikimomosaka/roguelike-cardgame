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
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import { useEnemyCatalog } from '../hooks/useEnemyCatalog'
import { useUnitCatalog } from '../hooks/useUnitCatalog'
import { TopBar } from '../components/TopBar'
import {
  hpLevel,
  toCharacterDemo,
  toHandCardDemo,
  toRelicDemo,
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

export type IntentDemo = {
  kind: IntentKind
  icon: string
  num?: number
  name: string
  desc: string
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
  intent?: IntentDemo
  buffs: BuffDemo[]
}

export type HandCardDemo = {
  name: string
  cost: number | string
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

function Relic({ relic }: { relic: RelicDemo }) {
  const tip = useTip({
    name: relic.name,
    rarity: relic.rarity,
    desc: relic.desc,
  })
  return (
    <div className={`hud-relic hud-relic--${relic.rarity}`} {...tip}>
      {relic.icon}
    </div>
  )
}

function Potion({ potion }: { potion: PotionDemo }) {
  const tip = useTip(
    potion.empty
      ? null
      : {
          name: potion.name ?? '',
          rarity: potion.rarity,
          desc: potion.desc ?? '',
        },
  )
  const cls = potion.empty
    ? 'hud-pot is-empty'
    : `hud-pot hud-pot--${potion.rarity ?? 'c'}`
  // Why: Slot 同様、onClick 不在時に role/tabIndex 属性を JSX から外して
  // axe/aria の誤検出 (条件式の静的解析不可) を回避する目的。
  const interactiveProps = potion.onClick && !potion.empty
    ? { onClick: potion.onClick, role: 'button' as const, tabIndex: 0 }
    : {}
  return (
    <div className={cls} {...tip} {...interactiveProps}>
      {potion.icon}
    </div>
  )
}

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
      {intent.num !== undefined ? (
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
      {char.intent ? <IntentChip intent={char.intent} /> : null}
      <div className={`sprite sprite--${char.spriteKind}`}>{char.sprite}</div>
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

export function BattleScreen({ accountId, snapshot, onBattleResolved }: Props) {
  const [state, setState] = useState<BattleStateDto | null>(null)
  const [animating, setAnimating] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // resolvedRef は finalize 後の二重呼び出しを抑止するためのラッチ。
  const resolvedRef = useRef(false)

  const { catalog: cardCatalog } = useCardCatalog()
  const { catalog: potionCatalog } = usePotionCatalog()
  const { catalog: relicCatalog } = useRelicCatalog()
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
  const hpPct = heroHp.max > 0
    ? Math.max(0, Math.min(100, (heroHp.cur / heroHp.max) * 100))
    : 0

  const relicDemos = state.ownedRelicIds.map((id) =>
    toRelicDemo(id, relicCatalog),
  )

  // potions: 配列を 3 枠表示 (POTION 数は MVP では 3 を仮定)。
  const POTION_SLOT_COUNT = 3
  const potionDemos: PotionDemo[] = []
  for (let i = 0; i < POTION_SLOT_COUNT; i++) {
    const id = state.potions[i]
    if (id && id.length > 0) {
      const def = potionCatalog?.[id]
      potionDemos.push({
        icon: '🜂',
        rarity: def ? cardRarityFromCatalogNumber(def.rarity) : 'c',
        name: def?.name ?? id,
        desc: def?.description ?? '',
        onClick: () => void handleUsePotion(i),
      })
    } else {
      potionDemos.push({ icon: '—', empty: true })
    }
  }

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
    <div className="battle">
      <div className="battle__pattern" />
      {/* Why: ユーザ要望「上のバーは map 画面のものをそのまま」。
          HP は live battle override (heroHp.cur)、potion 操作は battle 内で
          discard 不可なので onDiscardPotion は no-op。 */}
      <TopBar
        currentHp={heroHp.cur}
        maxHp={heroHp.max}
        gold={snapshot.run.gold}
        potions={state.potions}
        deck={snapshot.run.deck}
        relics={state.ownedRelicIds}
        onDiscardPotion={() => { /* battle 中は discard 不可 */ }}
        onOpenMenu={() => { /* battle 中の menu は後続フェーズで対応 */ }}
        playSeconds={snapshot.run.playSeconds}
      />
      <div className="battle__content">
        {/* Battle 内 HUD: combo + 操作系。run-level 情報は上の TopBar 参照。 */}
        <div className="battle__hud">
          <div className="hud-group hud-hp">
            <span className="hud-k">HP</span>
            <div className="track" data-lv={heroHp.lv}>
              <div className="fill" style={{ width: `${hpPct}%` }} />
              <span className="num">
                {heroHp.cur}/{heroHp.max}
              </span>
            </div>
          </div>
          <div className="hud-group hud-gold">
            <span className="hud-k">GOLD</span>
            <span className="hud-v">—</span>
          </div>
          <div className="hud-relics">
            <span className="hud-k">RELICS</span>
            <div className="hud-relics__list">
              {relicDemos.map((r, i) => (
                <Relic key={i} relic={r} />
              ))}
            </div>
          </div>
          <div className="hud-group hud-potions">
            <span className="hud-k">POTION</span>
            {potionDemos.map((p, i) => (
              <Potion key={i} potion={p} />
            ))}
          </div>
          <div className="hud-group">
            <span className="hud-k">DECK</span>
            <span className="hud-v">{state.drawPile.length}</span>
          </div>
          <div className={`hud-group hud-combo${state.comboCount > 1 ? ' is-active' : ''}`}>
            <span className="hud-k">COMBO</span>
            <span className="hud-v">×{state.comboCount}</span>
          </div>
          <button type="button" className="hud-btn">
            MAP
          </button>
          <button type="button" className="hud-btn">
            MENU ≡
          </button>
        </div>

        {/* Stage */}
        <div className="battle__stage">
          <div className="battle__chars">
            <div className="battle__side battle__side--player">
              {playerSlots.map((c, i) => {
                // Why: 死亡 actor を除外した aliveAllies に対する index で参照する。
                // state.allies[i] では死亡 actor を含むため整合しない。
                const actor = aliveAllies[i]
                const isHero = actor?.definitionId === 'hero'
                const isTargeted =
                  actor !== undefined &&
                  state.targetAllyIndex === actor.slotIndex
                // hero 自身は target にしない (自身を回復対象にする等は後続)。
                const onClick =
                  c.occupied && actor && !isHero && !interactionsDisabled
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

          {/* Piles */}
          <div className="pile pile--draw">
            <span className="pile__sym">❖</span>
            <span className="pile__lbl">山札</span>
            <span className="pile__num">{state.drawPile.length}</span>
          </div>
          <div className="pile pile--exhaust">
            <span className="pile__sym">✦</span>
            <span className="pile__lbl">除外</span>
            <span className="pile__num">{state.exhaustPile.length}</span>
          </div>
          <div className="pile pile--discard">
            <span className="pile__sym">✕</span>
            <span className="pile__lbl">捨札</span>
            <span className="pile__num">{state.discardPile.length}</span>
          </div>

          {/* Turn end */}
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
  )
}

// 内部用: Potion catalog rarity (number) → CardRarity。
// dtoAdapter と重複するのを避けるため軽量実装をこちらに置く。
function cardRarityFromCatalogNumber(n: number): CardRarity {
  switch (n) {
    case 0: return 'c'
    case 1: return 'r'
    case 2: return 'e'
    case 3: return 'l'
    default: return 'c'
  }
}
