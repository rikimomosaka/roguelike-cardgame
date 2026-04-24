// src/Client/src/screens/BattleScreen.tsx
//
// Visual demo component that mirrors battle-v10.html using existing
// CSS (BattleScreen.css) and shared primitives (Card, Tooltip).
// This is intentionally hardcoded / props-driven; it does NOT wire up
// to the real battle API. Accessed via `?demo=battle`.

import { useCallback, useEffect, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { Card } from '../components/Card'
import type { CardRarity, CardType } from '../components/Card'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import './BattleScreen.css'

// -------------------- Types --------------------

type HpLv = '0' | '1' | '2' | '3'

type RelicDemo = {
  icon: string
  rarity: CardRarity
  name: string
  desc: string
}

type PotionDemo = {
  icon: string
  rarity?: CardRarity
  name?: string
  desc?: string
  empty?: boolean
}

type BuffKind = 'block' | 'buff' | 'debuff'

type BuffDemo = {
  kind: BuffKind
  icon: string
  num: number
  name: string
  desc: string
}

type IntentKind = 'attack' | 'defend' | 'buff' | 'heal' | 'unknown'

type IntentDemo = {
  kind: IntentKind
  icon: string
  num?: number
  name: string
  desc: string
}

type SpriteKind = 'hero' | 'ally' | 'enemy' | 'elite'

type CharacterDemo = {
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

type HandCardDemo = {
  name: string
  cost: number | string
  type: CardType
  rarity: CardRarity
  art?: ReactNode
  playable?: boolean
  desc: string
}

type Props = {
  hp?: { cur: number; max: number; lv: HpLv }
  gold?: number
  deck?: number
  energy?: { cur: number; max: number }
  relics?: RelicDemo[]
  potions?: PotionDemo[]
  players?: CharacterDemo[]
  enemies?: CharacterDemo[]
  piles?: { draw: number; discard: number; exhaust: number }
  hand?: HandCardDemo[]
}

// -------------------- Defaults (match battle-v10.html) --------------------

const DEFAULT_RELICS: RelicDemo[] = [
  { icon: '♆', rarity: 'c', name: '燃え残りの薪', desc: '戦闘開始時、1 ブロックを得る。' },
  { icon: '⚓', rarity: 'c', name: '鉄の錨', desc: '戦闘開始時、5 ブロックを得る。' },
  { icon: '❖', rarity: 'r', name: '黄昏の封蝋', desc: '各戦闘の 1 ターン目、ドロー +1。' },
  { icon: '♜', rarity: 'r', name: '古びた塔', desc: 'エリート戦勝利時、追加でレア報酬カードを得る。' },
  { icon: '✧', rarity: 'e', name: '虚無の瞳', desc: '毎ターン終了時、最大 HP -1 だが、カードを 1 枚ドロー。' },
  { icon: '♛', rarity: 'l', name: '炎帝の王冠', desc: 'ACT 2 開始時に獲得。毎戦闘、最初のカードのコストが 0 になる。' },
  { icon: '✦', rarity: 'c', name: '欠けた砥石', desc: '戦闘開始時、基礎ダメージ +1 のバフを 1 ターン得る。' },
  { icon: '✪', rarity: 'c', name: '青銅の護符', desc: '毎戦闘開始時、ブロック +3。' },
]

const DEFAULT_POTIONS: PotionDemo[] = [
  { icon: '🜂', rarity: 'c', name: '小さな治癒薬', desc: 'HP を 25% 回復する。' },
  { icon: '☠', rarity: 'r', name: '毒の小瓶', desc: '対象に 6 毒を付与する。' },
  { icon: '—', empty: true },
]

const DEFAULT_PLAYERS: CharacterDemo[] = [
  {
    occupied: true,
    name: '主人公',
    desc: 'プレイヤー本体。HP 0 で敗北。',
    sprite: '☗',
    spriteKind: 'hero',
    hpCur: 58,
    hpMax: 80,
    hpLv: '2',
    buffs: [
      { kind: 'block', icon: '◆', num: 12, name: 'ブロック', desc: 'このターン、ダメージを軽減する。' },
      { kind: 'buff', icon: '✦', num: 2, name: '力', desc: '与えるダメージが +2 増加する。' },
    ],
  },
]

const DEFAULT_ENEMIES: CharacterDemo[] = [
  {
    occupied: true,
    name: '呪われた騎士',
    desc: 'HP 45 / 攻撃と守りを繰り返す通常敵。',
    sprite: '☠',
    spriteKind: 'enemy',
    hpCur: 32,
    hpMax: 45,
    hpLv: '2',
    intent: {
      kind: 'attack',
      icon: '⚔',
      num: 14,
      name: '予定行動：攻撃 14',
      desc: 'このターン、14 ダメージを与えます。',
    },
    buffs: [
      { kind: 'debuff', icon: '☠', num: 3, name: '毒', desc: 'ターン終了時に 3 ダメージ。スタック数が 1 減る。' },
    ],
  },
  {
    occupied: true,
    name: '業火の骸骨王',
    desc: 'HP 120 / エリート。強力な攻撃を放つ。',
    sprite: '♛',
    spriteKind: 'elite',
    hpCur: 88,
    hpMax: 120,
    hpLv: '3',
    intent: {
      kind: 'attack',
      icon: '⚔',
      num: 12,
      name: '予定行動：攻撃 12',
      desc: 'このターン、12 ダメージを与えます。',
    },
    buffs: [
      { kind: 'buff', icon: '✦', num: 2, name: '力', desc: '与えるダメージが +2 増加する。' },
    ],
  },
]

const DEFAULT_HAND: HandCardDemo[] = [
  { name: '打撃', cost: 1, type: 'attack', rarity: 'c', playable: true, desc: '敵 1 体に 6 ダメージ。' },
  { name: '防御', cost: 1, type: 'skill', rarity: 'c', playable: true, desc: 'ブロック +5。' },
  { name: '炎の剣', cost: 2, type: 'attack', rarity: 'r', playable: true, desc: '敵 1 体に 10 ダメージ + 炎上 2。' },
  { name: '祈り', cost: 0, type: 'skill', rarity: 'c', playable: true, desc: 'カードを 1 枚ドロー。' },
  { name: '烈火', cost: 2, type: 'attack', rarity: 'e', playable: true, desc: '全ての敵に 8 ダメージ。' },
  { name: '鉄壁', cost: 3, type: 'power', rarity: 'r', playable: false, desc: '毎ターン開始時、ブロック +4。' },
]

// Fan layout for a 6-card hand. Values ported from battle-v10.html feel.
function fanLayout(count: number): { x: number; y: number; r: number }[] {
  if (count === 0) return []
  const step = 60 // px horizontal step
  const center = (count - 1) / 2
  const rotStep = 7 // degrees
  return Array.from({ length: count }, (_, i) => {
    const offset = i - center
    const x = offset * step
    const rot = offset * rotStep
    // lift the center of the fan; ends dip lower (arc)
    const y = Math.abs(offset) * 6
    return { x, y, r: rot }
  })
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
  return (
    <div className={cls} {...tip}>
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

function Slot({ char }: { char: CharacterDemo }) {
  if (!char.occupied) {
    return <div className="battle__slot" data-occupied="0" />
  }
  const pct = Math.max(0, Math.min(100, (char.hpCur / char.hpMax) * 100))
  const hpFillStyle: CSSProperties = { width: `${pct}%` }
  return (
    <div className="battle__slot" data-occupied="1">
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

function HandCard({
  card,
  fan,
}: {
  card: HandCardDemo
  fan: { x: number; y: number; r: number }
}) {
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
        onMouseEnter={tip.onMouseEnter}
        onMouseLeave={tip.onMouseLeave}
      />
      <span ref={probeRefCb} aria-hidden="true" className="hand__probe" />
    </>
  )
}

// -------------------- Main component --------------------

export function BattleScreen({
  hp = { cur: 58, max: 80, lv: '2' },
  gold = 128,
  deck = 20,
  energy = { cur: 2, max: 3 },
  relics = DEFAULT_RELICS,
  potions = DEFAULT_POTIONS,
  players = DEFAULT_PLAYERS,
  enemies = DEFAULT_ENEMIES,
  piles = { draw: 15, discard: 4, exhaust: 1 },
  hand = DEFAULT_HAND,
}: Props = {}) {
  const hpPct = Math.max(0, Math.min(100, (hp.cur / hp.max) * 100))
  const fan = fanLayout(hand.length)

  // Pad players/enemies up to 4 slots each (row-reverse on player side
  // means the hero is already on the rightmost/innermost position).
  const playerSlots: CharacterDemo[] = padSlots(players, 4)
  const enemySlots: CharacterDemo[] = padSlots(enemies, 4)

  return (
    <div className="battle">
      <div className="battle__pattern" />
      <div className="battle__content">
        {/* Top HUD */}
        <div className="battle__hud">
          <div className="hud-group hud-hp">
            <span className="hud-k">HP</span>
            <div className="track" data-lv={hp.lv}>
              <div className="fill" style={{ width: `${hpPct}%` }} />
              <span className="num">
                {hp.cur}/{hp.max}
              </span>
            </div>
          </div>
          <div className="hud-group hud-gold">
            <span className="hud-k">GOLD</span>
            <span className="hud-v">{gold}</span>
          </div>
          <div className="hud-relics">
            <span className="hud-k">RELICS</span>
            <div className="hud-relics__list">
              {relics.map((r, i) => (
                <Relic key={i} relic={r} />
              ))}
            </div>
          </div>
          <div className="hud-group hud-potions">
            <span className="hud-k">POTION</span>
            {potions.map((p, i) => (
              <Potion key={i} potion={p} />
            ))}
          </div>
          <div className="hud-group">
            <span className="hud-k">DECK</span>
            <span className="hud-v">{deck}</span>
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
              {playerSlots.map((c, i) => (
                <Slot key={`p-${i}`} char={c} />
              ))}
            </div>
            <div className="battle__side battle__side--enemy">
              {enemySlots.map((c, i) => (
                <Slot key={`e-${i}`} char={c} />
              ))}
            </div>
          </div>

          {/* Energy orb */}
          <EnergyOrb cur={energy.cur} max={energy.max} />

          {/* Piles */}
          <div className="pile pile--draw">
            <span className="pile__sym">❖</span>
            <span className="pile__lbl">山札</span>
            <span className="pile__num">{piles.draw}</span>
          </div>
          <div className="pile pile--exhaust">
            <span className="pile__sym">✦</span>
            <span className="pile__lbl">除外</span>
            <span className="pile__num">{piles.exhaust}</span>
          </div>
          <div className="pile pile--discard">
            <span className="pile__sym">✕</span>
            <span className="pile__lbl">捨札</span>
            <span className="pile__num">{piles.discard}</span>
          </div>

          {/* Turn end */}
          <button type="button" className="end-turn">
            TURN END
          </button>

          {/* Hand (fanned) */}
          <div className="hand-wrap">
            <div className="hand">
              {hand.map((c, i) => (
                <HandCard key={i} card={c} fan={fan[i]} />
              ))}
            </div>
          </div>
        </div>
      </div>
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
