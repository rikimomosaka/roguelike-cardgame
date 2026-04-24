import { useMemo } from 'react'
import type { CSSProperties, MouseEventHandler, ReactNode } from 'react'
import { useTooltipTarget } from './Tooltip'
import type { TooltipContent } from './Tooltip'
import './Card.css'

export type CardType = 'attack' | 'skill' | 'power' | 'curse' | 'status'
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
  const typeLabel = type.toUpperCase()

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

  return (
    <div
      className={classes}
      style={style}
      onClick={onClick}
      onMouseEnter={combinedEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={combinedLeave}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
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
