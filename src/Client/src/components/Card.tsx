import { useEffect, useMemo, useState } from 'react'
import type { MouseEventHandler, ReactNode } from 'react'
import { useTooltipTarget } from './Tooltip'
import type { TooltipContent } from './Tooltip'
import { CardDesc } from './CardDesc'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { useUnitCatalog } from '../hooks/useUnitCatalog'
import './Card.css'

export type CardType = 'attack' | 'skill' | 'power' | 'curse' | 'status' | 'unit'
export type CardRarity = 'c' | 'r' | 'e' | 'l' | 't'

// CSS only needs three fitting bands: one glyph, two glyphs, and three or more.
// Capping the value keeps unusually long dev/debug costs inside the same safe band.
function costDigitBand(value: number | string): 1 | 2 | 3 {
  return Math.min(3, Math.max(1, Array.from(String(value)).length)) as 1 | 2 | 3
}

type Props = {
  name: string
  cost: number | string
  /** コンボ参照用の元コスト。現在値と異なるとき下段に表示する。 */
  costOrig?: number | string | null
  type: CardType
  rarity: CardRarity
  art?: ReactNode
  upgraded?: boolean
  selected?: boolean
  locked?: boolean
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

  // Why: 'unit' は内部 enum 値だが UI では「召喚カード」を表す概念で「SUMMON」表示。
  const typeLabel = type === 'unit' ? 'SUMMON' : type.toUpperCase()

  // Phase 10.5.M6.7: card / unit catalog を読んで marker → JP 名解決に使う。
  //  catalog 未ロード時は names = {} で CardDesc が ID フォールバック。
  const { names: cardCatalogNames } = useCardCatalog()
  const { names: unitCatalogNames } = useUnitCatalog()

  // Why: 右クリック長押し中は upgraded ⇄ unupgraded を反転表示する
  //  (ユーザ要望: 全領域・全リスト共通で +/非+ を比較可能にする)。
  //  upgradedDescription が無いカードでは反転しても意味が無いので無効化。
  const [previewToggle, setPreviewToggle] = useState(false)
  const canPreviewToggle = !!upgradedDescription
  const effectiveUpgraded = canPreviewToggle && previewToggle ? !upgraded : upgraded
  // window 全体で mouseup を拾って、カード外で離した場合もトグル解除。
  useEffect(() => {
    if (!previewToggle) return
    const onUp = (e: globalThis.MouseEvent) => {
      if (e.button === 2) setPreviewToggle(false)
    }
    const onBlur = () => setPreviewToggle(false)
    window.addEventListener('mouseup', onUp)
    window.addEventListener('blur', onBlur)
    return () => {
      window.removeEventListener('mouseup', onUp)
      window.removeEventListener('blur', onBlur)
    }
  }, [previewToggle])

  const activeDesc = effectiveUpgraded && upgradedDescription ? upgradedDescription : description
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!activeDesc) return null
    return { name: effectiveUpgraded ? `${name}+` : name, rarity, desc: activeDesc }
  }, [activeDesc, name, rarity, effectiveUpgraded])
  const tip = useTooltipTarget(tooltipContent)

  const combinedEnter: MouseEventHandler<HTMLDivElement> = (e) => {
    tip.onMouseEnter(e)
    onMouseEnter?.(e)
  }
  const combinedLeave: MouseEventHandler<HTMLDivElement> = (e) => {
    tip.onMouseLeave()
    onMouseLeave?.(e)
  }
  const handleContextMenu: MouseEventHandler<HTMLDivElement> = (e) => {
    e.preventDefault()
  }
  const handleMouseDown: MouseEventHandler<HTMLDivElement> = (e) => {
    if (e.button === 2 && canPreviewToggle) setPreviewToggle(true)
  }

  // Why: onClick 不在時に role/tabIndex 属性を JSX から外して
  // axe/aria の誤検出 (条件式の静的解析不可) を回避する目的。
  const interactiveProps = onClick
    ? { onClick, role: 'button' as const, tabIndex: 0 }
    : {}
  return (
    <div
      className={classes}
      onMouseEnter={combinedEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={combinedLeave}
      onMouseDown={handleMouseDown}
      onContextMenu={handleContextMenu}
      {...interactiveProps}
    >
      <div className="card__bg" />
      <div className="card__top">
        {costOrig !== null && costOrig !== undefined && costOrig !== cost ? (
          // Why: コンボ軽減時は通常 1 個のコスト円ではなく、現在コスト (上) と
          // 元コスト (下) を同サイズの円で縦並び表示する。上=強調、下=灰色。
          <div className="card__cost-stack">
            <div className="card__cost card__cost--current">
              <span className="card__cost-n" data-digits={costDigitBand(cost)}>{cost}</span>
            </div>
            <div className="card__cost card__cost--orig">
              <span className="card__cost-n" data-digits={costDigitBand(costOrig)}>{costOrig}</span>
            </div>
          </div>
        ) : (
          <div className="card__cost">
            <span className="card__cost-n" data-digits={costDigitBand(cost)}>{cost}</span>
          </div>
        )}
        {/* Why: カード名は枠内に収めるため文字数に応じて段階的に font-size
            を縮小する (ユーザ要望: ストライク/ウィスプ召喚 等の見切れ回避)。
            card.css 側の .card__name[data-len="N"] で個別 font-size 指定。
            +1 は upgrade 時の '+' を考慮。 */}
        <div className="card__name" data-len={name.length + (effectiveUpgraded ? 1 : 0)}>
          {name}
          {effectiveUpgraded ? <span className="card__plus">+</span> : null}
        </div>
      </div>
      <div className="card__illust">{art ?? 'ILLUST'}</div>
      {/* Why: カード下半分に description (formatter の auto-text) を表示。
          [N:..] / [K:..] / [S:..] 等の marker を CardDesc が span 化、CSS で
          黒フチ + 小さめ font に整形して in-card に収める。 */}
      {activeDesc ? (
        <div className="card__desc">
          {/* Phase 10.5.M6.7: catalog を引いて [C:..] / [U:..] marker を JP 名に解決。
              hook はデフォルト空 names を返すため fetch 失敗環境でも safe (ID 表示)。 */}
          <CardDesc text={activeDesc} cardNames={cardCatalogNames} unitNames={unitCatalogNames} />
        </div>
      ) : null}
      <div className="card__type">{typeLabel}</div>
    </div>
  )
}
