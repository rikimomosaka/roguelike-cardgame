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
  /** 元コスト。cost と異なるとき「{costOrig}→{cost}」表示でコンボ軽減を可視化する。 */
  costOrig?: number | string | null
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
  costOrig,
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
        {costOrig !== null && costOrig !== undefined && costOrig !== cost ? (
          // Why: コンボ軽減時は通常 1 個のコスト円ではなく、現在コスト (上) と
          // 元コスト (下) を同サイズの円で縦並び表示する。上=強調、下=取り消し線。
          <div className="card__cost-stack">
            <div className="card__cost card__cost--current">
              <span className="card__cost-n">{cost}</span>
            </div>
            <div className="card__cost card__cost--orig">
              <span className="card__cost-n">{costOrig}</span>
            </div>
          </div>
        ) : (
          <div className="card__cost">
            <span className="card__cost-n">{cost}</span>
          </div>
        )}
        {/* Why: カード名は枠内に収めるため文字数に応じて段階的に font-size
            を縮小する (ユーザ要望: ストライク/ウィスプ召喚 等の見切れ回避)。
            card.css 側の .card__name[data-len="N"] で個別 font-size 指定。
            +1 は upgrade 時の '+' を考慮。 */}
        <div className="card__name" data-len={name.length + (upgraded ? 1 : 0)}>
          {name}
          {upgraded ? <span className="card__plus">+</span> : null}
        </div>
      </div>
      <div className="card__illust">{art ?? 'ILLUST'}</div>
      <div className="card__type">{typeLabel}</div>
    </div>
  )
}
