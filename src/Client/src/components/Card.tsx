import { useMemo } from 'react'
import type { CSSProperties, MouseEventHandler, ReactNode } from 'react'
import { useTooltipTarget } from './Tooltip'
import type { TooltipContent } from './Tooltip'
import './Card.css'

export type CardType = 'attack' | 'skill' | 'power' | 'curse' | 'status' | 'unit'
export type CardRarity = 'c' | 'r' | 'e' | 'l'

type Props = {
  name: string
  cost: number | string
  type: CardType
  rarity: CardRarity
  art?: ReactNode
  upgraded?: boolean
  selected?: boolean
  locked?: boolean
  width?: number
  className?: string
  description?: string
  upgradedDescription?: string | null
  onClick?: MouseEventHandler<HTMLDivElement>
  onMouseEnter?: MouseEventHandler<HTMLDivElement>
  onMouseLeave?: MouseEventHandler<HTMLDivElement>
}

export function Card({
  name,
  cost,
  type,
  rarity,
  art,
  upgraded,
  selected,
  locked,
  width = 104,
  className,
  description,
  upgradedDescription,
  onClick,
  onMouseEnter,
  onMouseLeave,
}: Props) {
  const classes = [
    'card',
    `card--type-${type}`,
    `card--rarity-${rarity}`,
    selected && 'is-selected',
    locked && 'is-locked',
    className,
  ]
    .filter(Boolean)
    .join(' ')

  const style: CSSProperties = { width: `${width}px` }
  // Why: 'unit' は内部 enum 値だが UI では「召喚カード」を表す概念で「SUMMON」表示。
  const typeLabel = type === 'unit' ? 'SUMMON' : type.toUpperCase()

  const activeDesc = upgraded && upgradedDescription ? upgradedDescription : description
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!activeDesc) return null
    return { name: upgraded ? `${name}+` : name, rarity, desc: activeDesc }
  }, [activeDesc, name, rarity, upgraded])
  const tip = useTooltipTarget(tooltipContent)

  const combinedEnter: MouseEventHandler<HTMLDivElement> = (e) => {
    tip.onMouseEnter(e)
    onMouseEnter?.(e)
  }
  const combinedLeave: MouseEventHandler<HTMLDivElement> = (e) => {
    tip.onMouseLeave()
    onMouseLeave?.(e)
  }

  // Why: onClick 不在時に role/tabIndex 属性を JSX から外して
  // axe/aria の誤検出 (条件式の静的解析不可) を回避する目的。
  const interactiveProps = onClick
    ? { onClick, role: 'button' as const, tabIndex: 0 }
    : {}
  return (
    <div
      className={classes}
      style={style}
      onMouseEnter={combinedEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={combinedLeave}
      {...interactiveProps}
    >
      <div className="card__bg" />
      <div className="card__top">
        <div className="card__cost">
          <span className="card__cost-n">{cost}</span>
        </div>
        <div className="card__name">
          {name}
          {upgraded ? <span className="card__plus">+</span> : null}
        </div>
      </div>
      <div className="card__illust">{art ?? 'ILLUST'}</div>
      <div className="card__type">{typeLabel}</div>
    </div>
  )
}
