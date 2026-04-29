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

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
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
  BattleCardInstanceDto,
  BattleStateDto,
  CombatActorDto,
  RunResultDto,
  RunSnapshotDto,
} from '../api/types'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { useCharacterCatalog } from '../hooks/useCharacterCatalog'
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
  /** PNG 等の立ち絵 URL。指定時は text sprite ではなく <img> で描画する。 */
  image?: string
  /** キャラのサイズ段階 (1 〜 10)。1 = スライム級の小型、5 = 標準、10 = 巨大ボス級。
   *  立ち絵 image 表示時は heightTier に応じて高さが決定される (縦横比は維持)。 */
  heightTier?: number
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
  /** Why: peek 中も親が live battle state を TopBar に表示できるよう、state
   *  更新ごとに親へ通知する。null は battle 終了 (clear) を意味する。 */
  onBattleStateChange?: (state: BattleStateDto | null) => void
  /** TopBar メニューボタン / ESC でゲーム内メニューを開閉する。
   *  state は MapScreen が保持する (戦闘中 abandon/exit のフローを共有するため)。 */
  menuOpen?: boolean
  onOpenMenu?: () => void
}

// -------------------- Animation timing --------------------

const STEP_DELAY_MS = 80         // 既存 fallback (汎用 event)
const ATTACK_SLIDE_MS = 280      // 攻撃側 slot の slide-in/out 1 周
const HIT_FLASH_MS = 320         // 被弾 slot の赤点滅
const CARD_LEAVE_MS = 260        // 手札カード exit (捨札方向に縮小フェード)
// Why: 死亡アニメーションは廃止 (ユーザ要望)。HP=0 で alive filter から外れて
// 自然消滅するため、後追い blink は入れない。

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/** Why: attack/hit アニメーション中の slot を className で識別するため
 *  の transient state。
 *  - attackerId: 攻撃中の slot (1 つ、slide 中)
 *  - hitIds: 同じ攻撃 burst で被弾した slot 群 (全体攻撃で複数同時 flash)
 *  死亡演出はユーザ要望で廃止 (HP=0 即フィルタで自然消滅)。 */
type SlotAnim = {
  attackerId?: string
  attackerSide?: 'Ally' | 'Enemy'
  hitIds?: ReadonlyArray<string>
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

/**
 * Why: キャラ立ち絵の高さ段階 (1〜10) → px 換算。
 * tier 1 = スライム級小型 (60px), tier 5 = 標準 (160px),
 * tier 10 = 巨大ボス級 (260px)。線形に約 22px ずつ増加。
 * 縦横比は <img> 自身が維持するので width は auto。
 */
function heightForTier(tier: number): number {
  const clamped = Math.max(1, Math.min(10, Math.round(tier)))
  return 38 + clamped * 22  // 1→60, 5→148, 10→258
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

/** Why: HP バー幅 (100px) に収まるよう、長い名前は font-size を縮める。
 *  max-width: 12ch でのトランケート (...) は Japanese full-width に弱く
 *  「ケーブ・…」のように短い名前まで切れていたため廃止。 */
function StatusName({ name }: { name: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const [fontSize, setFontSize] = useState(12)
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    // Measure scrollWidth at baseline; if overflow, scale down proportionally.
    el.style.fontSize = '12px'
    const sw = el.scrollWidth
    const containerWidth = 100  // matches HP bar / slot width
    if (sw > containerWidth) {
      const next = Math.max(7, Math.floor(12 * containerWidth / sw))
      setFontSize(next)
    } else {
      setFontSize(12)
    }
  }, [name])
  return (
    <div
      ref={ref}
      className="status-name"
      style={{ fontSize: `${fontSize}px` }}
      title={name}
    >
      {name}
    </div>
  )
}

/** 単一 intent の中身 (icon + 値群)。tooltip 用 props は外から渡される。 */
function IntentSegment({
  intent,
  tipProps,
}: {
  intent: IntentDemo
  tipProps: ReturnType<typeof useTooltipTarget>
}) {
  return (
    <span className={`intent__seg intent__seg--${intent.kind}`} {...tipProps}>
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
    </span>
  )
}

function IntentChip({ intent }: { intent: IntentDemo }) {
  const tip = useTip({ name: intent.name, desc: intent.desc })
  return (
    <div className={`intent intent--${intent.kind}`}>
      <IntentSegment intent={intent} tipProps={tip} />
    </div>
  )
}

/** Why: 複数 intent (攻撃 + 防御 など) を 1 枠内に並べる。
 *  - 「＆」区切りなしで連続表示 (ユーザ要望)
 *  - hover でどの segment に当てても全 intent の name/desc を統合した
 *    tooltip を表示 (片方だけでなく両方の説明を見たい、というユーザ要望) */
function IntentChipRow({ intents }: { intents: IntentDemo[] }) {
  // 統合 tooltip: 全 intent の name と desc を改行で連結
  const mergedName = intents.map(i => i.name).join(' / ')
  const mergedDesc = intents.map(i => `■ ${i.name}\n${i.desc}`).join('\n\n')
  const tip = useTip({ name: mergedName, desc: mergedDesc })
  return (
    <div className={`intent intent--row${intents.length > 1 ? ' intent--multi' : ''}`}>
      {intents.map((it, i) => (
        <IntentSegment key={i} intent={it} tipProps={tip} />
      ))}
    </div>
  )
}

type SlotProps = {
  char: CharacterDemo
  isTargeted?: boolean
  /** Why: 攻撃中 / 被弾中の演出クラス付与用 (死亡演出は廃止)。 */
  attackingDir?: 'toward-enemy' | 'toward-ally'
  isHit?: boolean
  onClick?: () => void
}

function Slot({ char, isTargeted, attackingDir, isHit, onClick }: SlotProps) {
  // Why: 影の横幅は描画後の sprite 横幅と同じにしたい。silhouette は決定論的
  //  (tier-height × 0.55) で同期計算できる。画像は naturalWidth/Height が
  //  load 後にしか取れないので onLoad で aspect を State に保持し、それ以降
  //  の render で width = tier-height × aspect を渡す。影の高さは固定 (CSS)。
  const [imageAspect, setImageAspect] = useState<number | null>(null)
  useEffect(() => {
    setImageAspect(null) // 画像が切り替わったら再計測
  }, [char.image])
  const handleImageLoad = (e: React.SyntheticEvent<HTMLImageElement>) => {
    const img = e.currentTarget
    if (img.naturalHeight > 0) {
      setImageAspect(img.naturalWidth / img.naturalHeight)
    }
  }
  const shadowWidth: number | null = (() => {
    if (char.heightTier === undefined) return null // text fallback: CSS default
    const h = heightForTier(char.heightTier)
    if (char.image) {
      return imageAspect !== null && Number.isFinite(imageAspect)
        ? h * imageAspect
        : null
    }
    // silhouette: 既存の width: calc(--tier-height * 0.55) と同じ
    return h * 0.55
  })()
  const shadowStyle: CSSProperties | undefined =
    shadowWidth !== null ? { width: `${Math.round(shadowWidth)}px` } : undefined

  if (!char.occupied) {
    return <div className="battle__slot" data-occupied="0" />
  }
  const pct = Math.max(0, Math.min(100, (char.hpCur / char.hpMax) * 100))
  const hpFillStyle: CSSProperties = { width: `${pct}%` }
  const cls = [
    'battle__slot',
    isTargeted ? 'is-targeted' : '',
    attackingDir === 'toward-enemy' ? 'is-attacking-enemy' : '',
    attackingDir === 'toward-ally' ? 'is-attacking-ally' : '',
    isHit ? 'is-hit' : '',
  ].filter(Boolean).join(' ')
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
        // Why: 1 個の枠内に複数 intent を ＆ 区切りで並べる (ユーザ要望)。
        <div className="intents">
          <IntentChipRow intents={char.intents} />
        </div>
      ) : char.intent ? (
        <IntentChip intent={char.intent} />
      ) : null}
      {/* Why: image があれば <img>、無くても heightTier があればシルエット
          placeholder（実アセット届くまでの仮置き）、両方無ければ text sprite。
          --tier-height CSS 変数で高さを段階制御 (1〜10)。 */}
      {char.image ? (
        <div
          className={`sprite sprite--image sprite--${char.spriteKind}`}
          style={
            char.heightTier !== undefined
              ? ({ '--tier-height': `${heightForTier(char.heightTier)}px` } as CSSProperties)
              : undefined
          }
        >
          <img
            src={char.image}
            alt={char.name}
            draggable={false}
            onLoad={handleImageLoad}
          />
        </div>
      ) : char.heightTier !== undefined ? (
        <div
          className={`sprite sprite--silhouette sprite--${char.spriteKind}`}
          style={{ '--tier-height': `${heightForTier(char.heightTier)}px` } as CSSProperties}
          aria-label={`${char.name} (placeholder)`}
        />
      ) : (
        <div
          className={`sprite sprite--${char.spriteKind}${char.sprite.length > 2 ? ' sprite--text' : ''}`}
        >
          {char.sprite}
        </div>
      )}
      <div className="sprite-shadow" style={shadowStyle} />
      <div className="status-hp">
        <div className="status-hp__track" data-lv={char.hpLv}>
          <div className="status-hp__fill" style={hpFillStyle} />
          <span className="status-hp__num">
            {char.hpCur}/{char.hpMax}
          </span>
        </div>
      </div>
      <StatusName name={char.name} />
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
  /** Why: 直前まで hand にいたが手放されたカード (play / discard / exhaust /
   *  turn end discard 等) の exit アニメーション中の表示用。CSS で
   *  .is-leaving class が付き、捨札方向に縮小フェードする。 */
  leaving?: boolean
}

function HandCard({ card, fan, onClick, leaving }: HandCardProps) {
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
        /* Why: 実績画面 (TopBar デッキ modal) の Card 幅 112 と統一する。
           デフォルト 104 だと「ストライク」「ウィスプ召喚」が見切れていた。 */
        width={112}
        className={[
          card.playable ? 'is-playable' : '',
          leaving ? 'is-leaving' : '',
        ].filter(Boolean).join(' ') || undefined}
        onClick={leaving ? undefined : onClick}
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

export function BattleScreen({
  accountId,
  snapshot,
  onBattleResolved,
  onTogglePeek,
  onBattleStateChange,
  menuOpen,
  onOpenMenu,
}: Props) {
  const [state, setState] = useState<BattleStateDto | null>(null)
  const [animating, setAnimating] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [pileOpen, setPileOpen] = useState<PileKind | null>(null)
  // Why: ターン終了時 attack アニメーションの transient state。
  // attackerId: slide 中の slot、hitId: 赤点滅の被弾 slot、dyingIds: 撃破点滅中。
  const [slotAnim, setSlotAnim] = useState<SlotAnim>({})
  // Why: MVP 仕様で step.snapshotAfter は全 step が「最終 state」と同一なため、
  // burst 終了時に setState を呼ぶと一気に最終 HP / dead に飛んでしまう。
  // playSteps 中は setState せず、ここに instanceId → 累積 damage を蓄積し、
  // render 時に actor.currentHp - overlay でプログレッシブに HP を減らす。
  // 全 event 処理後に setState(finalState) + setDamageOverlay({}) で確定。
  const [damageOverlay, setDamageOverlay] = useState<Record<string, number>>({})
  // Why: 攻撃が block で完全相殺されても block 値は減るべき (ユーザ要望)。
  // engine の DealDamage は post-block damage しか出さないため、Client 側で
  // AttackFire.Amount (= totalAttack) と DealDamage.Amount (= damage) の差分を
  // block 消費量として累積する。
  const [blockOverlay, setBlockOverlay] = useState<Record<string, number>>({})
  // Why: 戦闘フェーズ banner (戦闘開始 / プレイヤーのターン / 敵のターン / 勝利)。
  // Map screen の ACT START と同じ 2 秒帯フォーマットで画面中央にオーバーレイ。
  // banner 表示中は他のアニメーション/入力を遅延させる (await showBanner)。
  const [phaseBanner, setPhaseBanner] = useState<string | null>(null)
  // resolvedRef は finalize 後の二重呼び出しを抑止するためのラッチ。
  const resolvedRef = useRef(false)
  // Why: hand から消えたカードを exit アニメーション中だけ残しておくバッファ。
  // 表示中はクリック不可 (onClick 無効)、CSS で .is-leaving が捨札方向へ
  // 縮小フェードする。タイマーで自動削除。
  type LeavingHandEntry = {
    card: BattleCardInstanceDto
    demo: HandCardDemo
    fan: { x: number; y: number; r: number }
  }
  const [leavingHand, setLeavingHand] = useState<LeavingHandEntry[]>([])
  const prevHandRef = useRef<{ list: BattleCardInstanceDto[]; demos: HandCardDemo[]; fans: { x: number; y: number; r: number }[] }>({
    list: [],
    demos: [],
    fans: [],
  })

  const { catalog: cardCatalog } = useCardCatalog()
  const { catalog: enemyCatalog } = useEnemyCatalog()
  const { catalog: unitCatalog } = useUnitCatalog()
  const { catalog: characterCatalog } = useCharacterCatalog()

  // Why: state が更新されるたび親 (MapScreen) に通知。peek 中も TopBar が live
  // battle state (HP / potions) を表示し続けるための仕組み (ユーザ要望)。
  useEffect(() => {
    onBattleStateChange?.(state)
  }, [state, onBattleStateChange])

  // Why: state.hand が更新された瞬間に「直前 hand にいたが今いない」カードを
  // 検出し、leavingHand に追加する。表示中は CSS .is-leaving で捨札方向に
  // 縮小フェードし、CARD_LEAVE_MS 経過で除去 (ユーザ要望: ゾーン間移動
  // アニメーション)。前回の hand list / demo / fan は ref で保持。
  // 注意: hooks は条件付き呼び出し不可なので state===null 早期 return より
  // 前で宣言する。state===null のときは body 冒頭で no-op return する。
  useEffect(() => {
    if (!state) return
    const hand = state.hand
    const prev = prevHandRef.current
    const currentIds = new Set(hand.map(c => c.instanceId))
    const newlyLeaving: LeavingHandEntry[] = []
    prev.list.forEach((card, idx) => {
      if (!currentIds.has(card.instanceId)) {
        newlyLeaving.push({
          card,
          demo: prev.demos[idx],
          fan: prev.fans[idx],
        })
      }
    })
    if (newlyLeaving.length > 0) {
      setLeavingHand(prevList => [...prevList, ...newlyLeaving])
      const ids = newlyLeaving.map(e => e.card.instanceId)
      window.setTimeout(() => {
        setLeavingHand(prevList =>
          prevList.filter(e => !ids.includes(e.card.instanceId)),
        )
      }, CARD_LEAVE_MS)
    }
    // 次回比較用に現在の hand / demos / fans を ref に保存。demos と fans は
    // render 内の handDemos / fan と同じ計算で生成 (cardCatalog 等は dep に
    // 入れず最新値を読む)。
    const newDemos = hand.map((c) =>
      toHandCardDemo(c, cardCatalog, state.energy, state.lastPlayedOrigCost),
    )
    const newFans = fanLayout(newDemos.length)
    prevHandRef.current = {
      list: hand.slice(),
      demos: newDemos,
      fans: newFans,
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state?.hand])

  // 戦闘フェーズ banner: text を 2 秒間オーバーレイ表示してから clear。
  // Map の ACT START と同じ 2s フェード仕様 (CSS keyframes 側で 10/88/100% 遷移)。
  const showBanner = useCallback(async (text: string) => {
    setPhaseBanner(text)
    await sleep(2000)
    setPhaseBanner(null)
  }, [])

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

  // BattleActionResponse を再生する。
  // 設計 (ユーザ要望):
  //  - 同じ caster の連続 AttackFire / DealDamage を 1 個の "attack burst"
  //    として束ねる (engine は per-target に AttackFire+DealDamage ペアを
  //    emit するため、全体攻撃 / 複数 effects で連続する)。
  //  - burst あたり: slide アニメ 1 回 → 全被弾 slot 同時 flash → 進行
  //  - HP 表示は damageOverlay (instanceId → 累積 damage) で progressive に
  //    減らす。playSteps 中は setState を呼ばず最後にまとめて確定するため、
  //    後続 attacker の最終 state が前 attacker の演出に染み出さない。
  //  - ActorDeath: 撃破点滅 → dyingIds から削除。HP=0 は damageOverlay に
  //    反映済 + state 未確定なので alive filter のため killedIds を別管理する。
  //  - その他 event (BattleStart / TurnStart / GainBlock / Draw / etc.):
  //    短い fallback delay。HP に影響しないので state 未確定でも見た目は同等。
  const playSteps = useCallback(
    async (resp: BattleActionResponseDto) => {
      setAnimating(true)
      const steps = resp.steps
      // Why: 初回 (state===null) の場合、setState せずに banner を流すと
      // loading 画面 (state===null gate) が後ろに残って banner が見えない。
      // 初回は最初に resp.state を設定して battle UI を出してから演出に入る。
      // 初回 response (battle start) に DealDamage は含まれないため、
      // setState(resp.state) しても progressive HP は問題にならない。
      if (state === null) {
        setState(resp.state)
      }
      // Why: playSteps 中は setState を呼ばず、ローカル overlay を更新して
      // setDamageOverlay / setBlockOverlay でレンダーに反映させる。
      // これにより同一 turn 内で attacker A → attacker B と続く時、A の
      // 演出中に B の damage が反映 (= HP が落ちる/敵が消える) する不具合
      // を回避できる。block も同じ仕組みで progressive に減らす。
      const dmg: Record<string, number> = {}
      const blk: Record<string, number> = {}
      // Why: 「敵のターン」 banner は 1 turn につき 1 回 (味方攻撃終了 → 初の
      // 敵 caster AttackFire の前) で出す。表示済フラグでガードする。
      let shownEnemyTurnBanner = false

      let i = 0
      while (i < steps.length) {
        const step = steps[i]
        const ev = step.event

        // 「戦闘開始」「プレイヤーのターン」 banner: BattleStart / TurnStart
        // event をそのまま trigger にする (engine が emit するタイミング)。
        if (ev.kind === 'BattleStart') {
          await showBanner('戦闘開始')
          i++
          continue
        }
        if (ev.kind === 'TurnStart') {
          await showBanner('プレイヤーのターン')
          i++
          continue
        }

        // 「敵のターン」 banner: 最初の敵 caster の AttackFire 直前に出す。
        // 攻撃 burst を始める前に判定し、敵側ならまず banner を流す。
        if (ev.kind === 'AttackFire' && !shownEnemyTurnBanner) {
          const isEnemyCaster = ev.casterInstanceId
            ? step.snapshotAfter.enemies.some(e => e.instanceId === ev.casterInstanceId)
            : false
          if (isEnemyCaster) {
            await showBanner('敵のターン')
            shownEnemyTurnBanner = true
          }
        }

        if (ev.kind === 'AttackFire') {
          // この caster の連続 AttackFire / DealDamage を集約。
          // AttackFire (totalAttack) と DealDamage (post-block damage) を
          // ペアで読み、block 消費量 = totalAttack - damage を計算する。
          const casterId = ev.casterInstanceId ?? undefined
          const hits: { targetId: string; damage: number; blockConsumed: number }[] = []
          let pendingFireAmount = 0
          let pendingFireTargetId: string | undefined = undefined
          let j = i
          while (j < steps.length) {
            const e = steps[j].event
            if ((e.kind === 'AttackFire' || e.kind === 'DealDamage')
                && e.casterInstanceId === casterId) {
              if (e.kind === 'AttackFire') {
                pendingFireAmount = e.amount ?? 0
                pendingFireTargetId = e.targetInstanceId ?? undefined
              } else if (e.kind === 'DealDamage' && e.targetInstanceId) {
                const damage = e.amount ?? 0
                // pendingFire と同 target なら pair。違う場合は fallback で
                // damage のみ反映 (block 消費量は不明 → 0 扱い)。
                const blockConsumed = pendingFireTargetId === e.targetInstanceId
                  ? Math.max(0, pendingFireAmount - damage)
                  : 0
                hits.push({
                  targetId: e.targetInstanceId,
                  damage,
                  blockConsumed,
                })
                pendingFireAmount = 0
                pendingFireTargetId = undefined
              }
              j++
            } else {
              break
            }
          }

          // slide 方向は caster side で決定 (現在の state ベースで判定)。
          const side = casterId && state
            ? (state.allies.some(a => a.instanceId === casterId) ? 'Ally' : 'Enemy')
            : undefined

          // Why: 内部で蓄積された攻撃回数 (intent.attackHits) 分だけ slide
          // アニメを連続再生する (ユーザ要望: ｼｭｼｭｼｭと連続で動くイメージ)。
          // ダメージ自体は engine が 1 個の DealDamage に集約しているので、
          // damage 反映と flash は最終 slide のタイミングでまとめて実行する。
          // attackHits 不明 (null) や 0 の場合は 1 回だけ slide。
          // 上限を MAX_SLIDES に cap: 一部の敵 (six_ghost.divider 等) は move
          // 内に 6+ の attack effect を持つが、そのまま再生すると視覚的に
          // 過剰なので 5 段までに制限する。
          const MAX_SLIDES = 5
          const casterActor = casterId && state
            ? (state.allies.find(a => a.instanceId === casterId)
                ?? state.enemies.find(e => e.instanceId === casterId))
            : undefined
          const rawSlideCount = Math.max(1, casterActor?.intent?.attackHits ?? 1)
          const slideCount = Math.min(MAX_SLIDES, rawSlideCount)
          const PER_HIT_MS = slideCount > 1 ? 200 : ATTACK_SLIDE_MS
          const GAP_MS = 60

          // Why: damage > 0 の target だけ赤 flash する。block で完全相殺
          // (damage=0) の場合は flash しない (ユーザ仕様: ブロック値だけ減る)。
          const flashTargets = Array.from(new Set(
            hits.filter(h => h.damage > 0).map(h => h.targetId),
          ))

          // 前段の slide (damage 反映なし)
          for (let s = 0; s < slideCount - 1; s++) {
            setSlotAnim({ attackerId: casterId, attackerSide: side })
            await sleep(PER_HIT_MS)
            // CSS animation を再トリガするため一度 class を外す
            setSlotAnim({})
            await sleep(GAP_MS)
          }

          // 最終 slide: 中盤で damage/block 反映 + flash 開始
          setSlotAnim({ attackerId: casterId, attackerSide: side })
          await sleep(PER_HIT_MS / 2)
          for (const h of hits) {
            if (h.damage > 0) {
              dmg[h.targetId] = (dmg[h.targetId] ?? 0) + h.damage
            }
            if (h.blockConsumed > 0) {
              blk[h.targetId] = (blk[h.targetId] ?? 0) + h.blockConsumed
            }
          }
          setDamageOverlay({ ...dmg })
          setBlockOverlay({ ...blk })
          if (flashTargets.length > 0) {
            setSlotAnim({ attackerId: casterId, attackerSide: side, hitIds: flashTargets })
          }
          await sleep(PER_HIT_MS / 2)
          setSlotAnim(prev => ({ ...prev, attackerId: undefined, attackerSide: undefined }))
          if (flashTargets.length > 0) {
            await sleep(Math.max(0, HIT_FLASH_MS - PER_HIT_MS / 2))
          }
          setSlotAnim({})
          i = j
          continue
        }

        if (ev.kind === 'ActorDeath') {
          // Why: ユーザ要望で死亡アニメーションは廃止 (HP=0 で消えるのが
          // 優先される現状のレンダーに対し、後追い演出だと一瞬復活したように
          // 見える)。event は consume するだけで delay も入れない。
          i++
          continue
        }

        // その他 (BattleStart / TurnStart / GainBlock / Draw / etc.)
        // HP 以外のフィールドは playSteps 終了時に state 確定で反映するため、
        // ここでは見た目を変えずに短い delay のみ取る。
        await sleep(STEP_DELAY_MS)
        i++
      }

      // 全 event 処理完了 → 最終 state に確定 + overlay クリア
      setSlotAnim({})
      setDamageOverlay({})
      setBlockOverlay({})
      setState(resp.state)

      // Victory のみ「勝利」banner を出す (ユーザ要望、Defeat は banner なし)。
      if (resp.state.outcome === 'Victory') {
        await showBanner('勝利')
      }
      setAnimating(false)

      if (resp.state.outcome === 'Victory' || resp.state.outcome === 'Defeat') {
        await handleFinalize()
      }
    },
    [handleFinalize, state, showBanner],
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
  const heroActorRaw = state.allies.find(
    (a) => a.definitionId === 'hero',
  ) as CombatActorDto | undefined
  // damageOverlay 反映: TopBar/HUD の HP 表示も progressive に減らす。
  const heroActor = heroActorRaw
    ? {
        ...heroActorRaw,
        currentHp: Math.max(
          0,
          heroActorRaw.currentHp - (damageOverlay[heroActorRaw.instanceId] ?? 0),
        ),
      }
    : undefined
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
  // damageOverlay は playSteps 中の累積ダメージで、表示用 HP に減算する。
  // blockOverlay は同じく block 消費量で blockDisplay に減算する。
  // 死亡アニメーションは廃止 (ユーザ要望) のため HP=0 で即フィルタ → 自然消滅。
  const applyOverlay = (a: CombatActorDto): CombatActorDto => {
    const dmg = damageOverlay[a.instanceId] ?? 0
    const blk = blockOverlay[a.instanceId] ?? 0
    if (dmg <= 0 && blk <= 0) return a
    return {
      ...a,
      currentHp: Math.max(0, a.currentHp - dmg),
      blockDisplay: Math.max(0, a.blockDisplay - blk),
    }
  }
  const allAllies = state.allies.map(applyOverlay)
  const allEnemies = state.enemies.map(applyOverlay)
  const aliveAllies = allAllies.filter((a) => a.currentHp > 0)
  const aliveEnemies = allEnemies.filter((e) => e.currentHp > 0)

  // 4 スロット zero-pad で side ごとに描画。
  const allyDemos = aliveAllies.map((a) =>
    toCharacterDemo(a, { enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog }, accountId),
  )
  const enemyDemos = aliveEnemies.map((e) =>
    toCharacterDemo(e, { enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog }, accountId),
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
        onOpenMenu={onOpenMenu ?? (() => {})}
        menuActive={menuOpen ?? false}
        onTogglePeek={onTogglePeek}
        peekActive={false}
        peekDisabled={!onTogglePeek}
        playSeconds={snapshot.run.playSeconds}
      />
      <div className="battle" data-act={snapshot.run.currentAct}>
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
                // Why: ally が攻撃側の時は敵側 (右) に slide。
                const attackingDir =
                  actor && slotAnim.attackerId === actor.instanceId
                    ? ('toward-enemy' as const)
                    : undefined
                const isHit = !!(actor && (slotAnim.hitIds ?? []).includes(actor.instanceId))
                return (
                  <Slot
                    key={`p-${i}`}
                    char={c}
                    isTargeted={isTargeted}
                    attackingDir={attackingDir}
                    isHit={isHit}
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
                // Why: enemy が攻撃側の時は味方側 (左) に slide。
                const attackingDir =
                  actor && slotAnim.attackerId === actor.instanceId
                    ? ('toward-ally' as const)
                    : undefined
                const isHit = !!(actor && (slotAnim.hitIds ?? []).includes(actor.instanceId))
                return (
                  <Slot
                    key={`e-${i}`}
                    char={c}
                    isTargeted={isTargeted}
                    attackingDir={attackingDir}
                    isHit={isHit}
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

          {/* Hand (fanned).
              Why: key を instanceId にすることで React が DOM を 1:1 で追跡し、
              新規カードのみ entry CSS animation が走り、再 mount 不要なカードは
              アニメ再生されない (急に再描画されない)。 */}
          <div className="hand-wrap">
            <div className="hand">
              {handDemos.map((c, i) => (
                <HandCard
                  key={state.hand[i]?.instanceId ?? `idx-${i}`}
                  card={c}
                  fan={fan[i]}
                  onClick={
                    interactionsDisabled
                      ? undefined
                      : () => void handlePlayCard(i)
                  }
                />
              ))}
              {/* leaving cards: 直前の hand には居たが今 hand にいないカード。
                  exit アニメ完了後に leavingHand から削除される。 */}
              {leavingHand.map((lh) => (
                <HandCard
                  key={`leaving-${lh.card.instanceId}`}
                  card={lh.demo}
                  fan={lh.fan}
                  leaving
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
      {/* Why: 戦闘フェーズ banner。Map の ACT START と同じ 2 秒帯を画面中央に
          オーバーレイ。表示中は他の input/anim が await でブロックされている。
          phaseBanner の key に text を使うことで、連続表示時にアニメーションが
          再起動するようにする (同じ要素を残すと再アニメが走らない)。 */}
      {phaseBanner !== null && (
        <div
          key={phaseBanner}
          className="battle-screen__phase-banner"
          role="status"
          aria-live="polite"
        >
          <span className="battle-screen__phase-banner-text">{phaseBanner}</span>
        </div>
      )}
    </div>
  )
}

