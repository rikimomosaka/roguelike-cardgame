import {
  createContext,
  useContext,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
} from 'react'
import type { FocusEvent, MouseEvent, ReactNode } from 'react'
import type { CardRarity } from './Card'
import { CardDesc, extractCardDescRefs } from './CardDesc'
import type { CardDescRef } from './CardDesc'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { useUnitCatalog } from '../hooks/useUnitCatalog'
import './Tooltip.css'

export type TooltipContent = {
  name: ReactNode
  rarity?: CardRarity
  rarityLabel?: string
  /** Optional status line rendered between the tooltip title and description. */
  statusLine?: ReactNode
  desc: ReactNode
  /** Keyword ids rendered with disabled styling in card descriptions. */
  disabledKeywords?: readonly string[]
  /** Status自身の説明など、同じ定義を二段目へ重複表示したくない場合に使う。 */
  suppressRefs?: boolean
  /** 本文中に同じ用語が現れても、自分自身の定義だけは二段目へ重複表示しない。 */
  selfReference?: Pick<CardDescRef, 'kind' | 'id'>
  /**
   * 任意のフレーバーテキスト。指定された場合 desc の下に点線で区切り
   * 斜体・薄灰色で描画される (RelicVisualPreview と同じレイアウト)。
   * Phase 10.5.M6.3。
   */
  flavor?: string
  /** A separate panel rendered to the right of a card's normal description. */
  costBreakdown?: TooltipCostBreakdown
}

export type TooltipCostChange = {
  kind: 'fixed' | 'modifier' | 'status' | 'combo' | 'unknown'
  value: number
  sourceId: string | null
  sourceName: string | null
}

export type TooltipCostBreakdown = {
  originalCost: number
  currentCost: number
  changes: readonly TooltipCostChange[]
}

type Position = { x: number; y: number }

type Ctx = {
  show: (content: TooltipContent, pos: Position, id: string) => void
  move: (pos: Position) => void
  hide: () => void
}

const TooltipContext = createContext<Ctx | null>(null)

export function TooltipHost({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{
    content: TooltipContent | null
    pos: Position
    id: string | null
  }>({ content: null, pos: { x: 0, y: 0 }, id: null })

  const tipRef = useRef<HTMLDivElement | null>(null)

  const ctx = useMemo<Ctx>(
    () => ({
      show: (content, pos, id) => setState({ content, pos, id }),
      move: (pos) => setState((s) => (s.content ? { ...s, pos } : s)),
      hide: () => setState({ content: null, pos: { x: 0, y: 0 }, id: null }),
    }),
    [],
  )

  const { content } = state
  // Why: marker (e.g. [C:strike] / [U:wisp]) を表示名へ解決するため catalog を引く。
  //   未ロード時は空オブジェクトで CardDesc が ID フォールバック表示する。
  const { names: cardNames } = useCardCatalog()
  const { names: unitNames } = useUnitCatalog()
  // 本文中のキーワード・状態の説明。本文の下ではなく、横の別枠（tip__side）に並べる。
  const refs = content && typeof content.desc === 'string' && !content.suppressRefs
    ? extractCardDescRefs(content.desc, cardNames).filter(
        ref => ref.kind !== content.selfReference?.kind || ref.id !== content.selfReference.id,
      )
    : []
  // 横の別枠は用語の説明だけ。コスト変更の内訳は本文の上（.tip__cost-panel）に出す。
  const hasSide = refs.length > 0

  useEffect(() => {
    const el = tipRef.current
    if (!el || !state.content) return
    const side = el.querySelector<HTMLElement>('.tip__side')
    const cost = el.querySelector<HTMLElement>('.tip__cost-panel')
    // 本文（.tip 自身。別枠は absolute なので高さに含まれない）を常にカーソル近くに置く。
    const { left, top, costPanelSide } = computePlacement(
      state.pos,
      !!side,
      {
        baseWidth: el.offsetWidth,
        baseHeight: el.offsetHeight,
        panelWidth: side?.offsetWidth ?? 0,
        panelHeight: 0,
      },
    )
    if (costPanelSide) el.dataset.side = costPanelSide
    else delete el.dataset.side
    el.style.setProperty('--tip-left', `${left}px`)
    el.style.setProperty('--tip-top', `${top}px`)
    // 用語の別枠は本文の下端にそろえる（画面の上にはみ出す場合だけ下げる）。
    if (side) {
      el.style.setProperty('--side-top', `${alignSidePanelTop({
        mainTop: top, mainHeight: el.offsetHeight, sideHeight: side.offsetHeight,
      })}px`)
    }
    // コスト変更の内訳は本文の上。上に入らなければ下。
    if (cost) el.dataset.costPos = costPanelPosition({ mainTop: top, costHeight: cost.offsetHeight })
    else delete el.dataset.costPos
  }, [state])

  return (
    <TooltipContext.Provider value={ctx}>
      {children}
      {content ? (
        <div
          id={state.id ?? undefined}
          ref={tipRef}
          className="tip is-on"
          role="tooltip"
          data-side={preferredCostPanelSide(state.pos, hasSide) ?? undefined}
        >
          <div className="tip__head">
            <div className={`tip__name${content.rarity ? ` tip__name--${content.rarity}` : ''}`}>
              {content.name}
            </div>
            {content.rarity ? (
              <div className={`tip__rare tip__rare--${content.rarity}`}>
                {content.rarityLabel ?? defaultRarityLabel(content.rarity)}
              </div>
            ) : null}
          </div>
          {content.statusLine ? (
            <div className="tip__status">{content.statusLine}</div>
          ) : null}
          <div className="tip__desc">
            {typeof content.desc === 'string' ? (
              <CardDesc
                text={content.desc}
                cardNames={cardNames}
                unitNames={unitNames}
                disabledKeywords={content.disabledKeywords}
                compress={false}
              />
            ) : (
              content.desc
            )}
          </div>
          {/* Phase 10.5.M6.3: フレーバーテキスト (任意)。点線区切り + 斜体グレーで
              「ストーリー描写」感を出し、効果テキストとの視覚的区別を確保。 */}
          {content.flavor ? (
            <>
              <div className="tip__flavor-sep" aria-hidden="true" />
              <div className="tip__flavor">{content.flavor}</div>
            </>
          ) : null}
          {/* コスト変更の内訳と、本文中の keyword / status / cardref の説明は、本文の横の別枠に並べる。
              本文がカーソルの右に出るときはさらに右、左に出るときはさらに左（data-side）。 */}
          {content.costBreakdown ? (
            <CostBreakdownPanel breakdown={content.costBreakdown} />
          ) : null}
          {hasSide ? (
            <div className="tip__side">
              <aside className="tip__refs-panel" aria-label="用語の説明">
                <TipRefs refs={refs} />
              </aside>
            </div>
          ) : null}
        </div>
      ) : null}
    </TooltipContext.Provider>
  )
}

function CostBreakdownPanel({ breakdown }: { breakdown: TooltipCostBreakdown }) {
  return (
    <aside className="tip__cost-panel" aria-label="コスト変更の内訳">
      <div className="tip__cost-title">コスト変更</div>
      <div className="tip__cost-base">コスト{breakdown.originalCost}</div>
      {breakdown.changes.map((change, index) => (
        <div className="tip__cost-line" key={`${change.kind}:${change.sourceId ?? ''}:${index}`}>
          <span className="tip__cost-arrow" aria-hidden="true">→</span>
          <CostChangeText change={change} />
        </div>
      ))}
      <div className="tip__cost-result">＝{breakdown.currentCost}コスト</div>
    </aside>
  )
}

function CostChangeText({ change }: { change: TooltipCostChange }) {
  const sourceName = change.sourceName ?? change.sourceId ?? '効果'
  if (change.kind === 'fixed') {
    return (
      <span>
        <span className="tip__cost-source">[{sourceName}]</span>
        によりコスト<span className="tip__cost-value">{change.value}</span>に変更
      </span>
    )
  }
  if (change.kind === 'combo') {
    return (
      <span>
        コンボによりコスト
        <span className="tip__cost-value tip__cost-value--reduced">{formatSigned(change.value)}</span>
      </span>
    )
  }
  const source = change.kind === 'status'
    ? `[${sourceName}]`
    : `「${sourceName}」`
  return (
    <span>
      <span className="tip__cost-source">{source}</span>
      によりコスト
      <span className={`tip__cost-value ${change.value < 0 ? 'tip__cost-value--reduced' : 'tip__cost-value--increased'}`}>
        {formatSigned(change.value)}
      </span>
    </span>
  )
}

function formatSigned(value: number): string {
  return value > 0 ? `+${value}` : String(value)
}

// The hook must share TooltipContext's private instance with TooltipHost.
// eslint-disable-next-line react-refresh/only-export-components
export function useTooltipTarget(content: TooltipContent | null) {
  const ctx = useContext(TooltipContext)
  const tooltipId = useId()
  const showingRef = useRef(false)
  // Why: hover 中に content が動的に変わった (例: 右クリック長押しで強化版に
  //  切替) ときも tooltip を更新するため、最後のカーソル位置を ref に保持。
  const lastPosRef = useRef<Position>({ x: 0, y: 0 })
  // content が null になったら（例: 商品が売切に変化した瞬間）、
  // 現在マウスが乗っていてもツールチップを消す。
  useEffect(() => {
    if (!content && showingRef.current) {
      ctx?.hide()
      showingRef.current = false
    } else if (content && showingRef.current && ctx) {
      // hover 中に content が変わった: 最新内容で再表示。
      ctx.show(content, lastPosRef.current, tooltipId)
    }
  }, [ctx, content, tooltipId])
  useEffect(() => {
    return () => {
      if (showingRef.current) {
        ctx?.hide()
        showingRef.current = false
      }
    }
  }, [ctx])
  return useMemo(
    () => ({
      onMouseEnter: (e: MouseEvent) => {
        if (!ctx || !content) return
        const pos = { x: e.clientX, y: e.clientY }
        lastPosRef.current = pos
        ctx.show(content, pos, tooltipId)
        showingRef.current = true
      },
      onMouseMove: (e: MouseEvent) => {
        if (!ctx || !content) return
        const pos = { x: e.clientX, y: e.clientY }
        lastPosRef.current = pos
        ctx.move(pos)
      },
      onMouseLeave: () => {
        ctx?.hide()
        showingRef.current = false
      },
      onFocus: (e: FocusEvent<HTMLElement>) => {
        if (!ctx || !content) return
        const rect = e.currentTarget.getBoundingClientRect()
        const pos = { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 }
        lastPosRef.current = pos
        ctx.show(content, pos, tooltipId)
        showingRef.current = true
      },
      onBlur: (e: FocusEvent<HTMLElement>) => {
        if (e.relatedTarget instanceof Node && e.currentTarget.contains(e.relatedTarget)) return
        ctx?.hide()
        showingRef.current = false
      },
      'aria-describedby': content ? tooltipId : undefined,
    }),
    [ctx, content, tooltipId],
  )
}

/**
 * Tooltip 二段目: description 内の keyword / status / cardref の詳細定義を並べる。
 * 1 個も無ければ何も描画しない。
 */
function TipRefs({ refs }: { refs: CardDescRef[] }) {
  if (refs.length === 0) return null
  return (
    <div className="tip__refs">
      {refs.map((r) => (
        <div key={`${r.kind}:${r.id}`} className={`tip__ref tip__ref--${r.kind}`}>
          <div className={r.kind === 'keyword' && r.id.startsWith('phase:')
            ? `tip__ref-name card-desc-phase card-desc-phase--${r.id.slice('phase:'.length)}`
            : 'tip__ref-name'}>{r.name}</div>
          {/* card は名前のみ (cardref として識別補助)。
              keyword / status は desc を併記。
              unit は extractCardDescRefs 側で除外済 (M6.9)。 */}
          {r.kind === 'card' ? null : (
            <div className="tip__ref-desc">
              <CardDesc
                text={r.desc}
                cardNames={{}}
                unitNames={{}}
                compress={false}
              />
            </div>
          )}
        </div>
      ))}
    </div>
  )
}

function defaultRarityLabel(r: CardRarity): string {
  switch (r) {
    case 'c': return 'COMMON'
    case 'r': return 'RARE'
    case 'e': return 'EPIC'
    case 'l': return 'LEGENDARY'
    case 't': return 'TOKEN'
  }
}

const OFFSET = 14
const TIP_W = 300
const TIP_H_EST = 120
const COST_PANEL_W = 270
const COST_PANEL_GAP = 8

const SCREEN_MARGIN = 8

/**
 * 用語の別枠の top（本文の上端からの相対 px）。別枠の下端を本文の下端にそろえ、
 * 画面の上端（8px）より上にはみ出す場合だけ下げる。
 */
export function alignSidePanelTop({ mainTop, mainHeight, sideHeight }: {
  mainTop: number
  mainHeight: number
  sideHeight: number
}): number {
  return Math.max(SCREEN_MARGIN - mainTop, mainHeight - sideHeight)
}

/** コスト変更の内訳を本文の上に置けるか（入らなければ下）。 */
export function costPanelPosition({ mainTop, costHeight }: {
  mainTop: number
  costHeight: number
}): 'above' | 'below' {
  return mainTop - COST_PANEL_GAP - costHeight >= SCREEN_MARGIN ? 'above' : 'below'
}

type TooltipMeasurement = {
  baseWidth: number
  baseHeight: number
  panelWidth: number
  panelHeight: number
}

type CostPanelSide = 'left' | 'right'

function preferredCostPanelSide(
  { x }: Position,
  hasCostBreakdown: boolean,
  baseWidth = TIP_W,
  panelWidth = COST_PANEL_W,
): CostPanelSide | null {
  if (!hasCostBreakdown || typeof window === 'undefined' || window.innerWidth < 700) return null
  const rightSpace = window.innerWidth - x - OFFSET - 8
  const leftSpace = x - OFFSET - 8
  const totalWidth = baseWidth + COST_PANEL_GAP + panelWidth
  if (rightSpace >= totalWidth) return 'right'
  if (leftSpace >= totalWidth) return 'left'
  return rightSpace >= leftSpace ? 'right' : 'left'
}

function computePlacement(
  { x, y }: Position,
  hasCostBreakdown = false,
  measured?: TooltipMeasurement,
): { left: number; top: number; costPanelSide: CostPanelSide | null } {
  if (typeof window === 'undefined') {
    return { left: x + OFFSET, top: y + OFFSET, costPanelSide: null }
  }
  const vw = window.innerWidth
  const vh = window.innerHeight
  const stacked = hasCostBreakdown && vw < 700
  const baseWidth = measured?.baseWidth || TIP_W
  const baseHeight = measured?.baseHeight || TIP_H_EST
  const panelWidth = measured?.panelWidth || COST_PANEL_W
  const panelHeight = measured?.panelHeight || 210
  const costPanelSide = preferredCostPanelSide(
    { x, y }, hasCostBreakdown, baseWidth, panelWidth,
  )
  const estimatedHeight = stacked
    ? baseHeight + COST_PANEL_GAP + panelHeight
    : Math.max(baseHeight, hasCostBreakdown ? panelHeight : 0)
  const regularFlipX = x + OFFSET + baseWidth > vw - 8
  const rawLeft = costPanelSide === 'left' || (!costPanelSide && regularFlipX)
    ? x - OFFSET - baseWidth
    : x + OFFSET
  const maxBaseLeft = Math.max(8, vw - 8 - baseWidth)
  const flipY = y + OFFSET + estimatedHeight > vh - 8
  return {
    left: Math.min(maxBaseLeft, Math.max(8, rawLeft)),
    top: flipY ? Math.max(8, y - OFFSET - estimatedHeight) : y + OFFSET,
    costPanelSide,
  }
}
