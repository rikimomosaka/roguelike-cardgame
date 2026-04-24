import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react'
import type { MouseEvent, ReactNode } from 'react'
import type { CardRarity } from './Card'
import './Tooltip.css'

export type TooltipContent = {
  name: ReactNode
  rarity?: CardRarity
  rarityLabel?: string
  desc: ReactNode
}

type Position = { x: number; y: number }

type Ctx = {
  show: (content: TooltipContent, pos: Position) => void
  move: (pos: Position) => void
  hide: () => void
}

const TooltipContext = createContext<Ctx | null>(null)

export function TooltipHost({ children }: { children: ReactNode }) {
  const [state, setState] = useState<{
    content: TooltipContent | null
    pos: Position
  }>({ content: null, pos: { x: 0, y: 0 } })

  const tipRef = useRef<HTMLDivElement | null>(null)

  const ctx = useMemo<Ctx>(
    () => ({
      show: (content, pos) => setState({ content, pos }),
      move: (pos) => setState((s) => (s.content ? { ...s, pos } : s)),
      hide: () => setState({ content: null, pos: { x: 0, y: 0 } }),
    }),
    [],
  )

  useEffect(() => {
    const el = tipRef.current
    if (!el || !state.content) return
    const { left, top } = computePlacement(state.pos)
    el.style.setProperty('--tip-left', `${left}px`)
    el.style.setProperty('--tip-top', `${top}px`)
  }, [state])

  const { content } = state

  return (
    <TooltipContext.Provider value={ctx}>
      {children}
      {content ? (
        <div ref={tipRef} className="tip is-on" role="tooltip">
          <div className="tip__head">
            <div className="tip__name">{content.name}</div>
            {content.rarity ? (
              <div className={`tip__rare tip__rare--${content.rarity}`}>
                {content.rarityLabel ?? defaultRarityLabel(content.rarity)}
              </div>
            ) : null}
          </div>
          <div className="tip__desc">{content.desc}</div>
        </div>
      ) : null}
    </TooltipContext.Provider>
  )
}

export function useTooltipTarget(content: TooltipContent | null) {
  const ctx = useContext(TooltipContext)
  const showingRef = useRef(false)
  // content が null になったら（例: 商品が売切に変化した瞬間）、
  // 現在マウスが乗っていてもツールチップを消す。
  useEffect(() => {
    if (!content && showingRef.current) {
      ctx?.hide()
      showingRef.current = false
    }
  }, [ctx, content])
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
        ctx.show(content, { x: e.clientX, y: e.clientY })
        showingRef.current = true
      },
      onMouseMove: (e: MouseEvent) => {
        if (!ctx || !content) return
        ctx.move({ x: e.clientX, y: e.clientY })
      },
      onMouseLeave: () => {
        ctx?.hide()
        showingRef.current = false
      },
    }),
    [ctx, content],
  )
}

function defaultRarityLabel(r: CardRarity): string {
  switch (r) {
    case 'c': return 'COMMON'
    case 'r': return 'RARE'
    case 'e': return 'EPIC'
    case 'l': return 'LEGENDARY'
  }
}

const OFFSET = 14
const TIP_W = 300
const TIP_H_EST = 120

function computePlacement({ x, y }: Position): { left: number; top: number } {
  if (typeof window === 'undefined') return { left: x + OFFSET, top: y + OFFSET }
  const vw = window.innerWidth
  const vh = window.innerHeight
  const flipX = x + OFFSET + TIP_W > vw - 8
  const flipY = y + OFFSET + TIP_H_EST > vh - 8
  return {
    left: flipX ? Math.max(8, x - OFFSET - TIP_W) : x + OFFSET,
    top: flipY ? Math.max(8, y - OFFSET - TIP_H_EST) : y + OFFSET,
  }
}
