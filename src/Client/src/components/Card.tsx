import { useEffect, useMemo, useState } from 'react'
import type { KeyboardEventHandler, MouseEventHandler, ReactNode } from 'react'
import { useTooltipTarget } from './Tooltip'
import type { TooltipContent } from './Tooltip'
import type { TooltipCostChange } from './Tooltip'
import { CardDesc } from './CardDesc'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { useUnitCatalog } from '../hooks/useUnitCatalog'
import type { CardPoolClass } from '../api/catalog'
import { cardFrame } from '../presentation/assets'
import './Card.css'

export type CardType = 'attack' | 'skill' | 'power' | 'curse' | 'status' | 'unit'
export type CardRarity = 'c' | 'r' | 'e' | 'l' | 't'

type CardFrameAffiliation = 'striker' | 'guardian' | 'tactician' | 'enhancer' | 'universal' | 'curse'

const KNOWN_POOL_CLASSES: Readonly<Record<string, Exclude<CardFrameAffiliation, 'curse'>>> = {
  striker: 'striker',
  guardian: 'guardian',
  tactician: 'tactician',
  enhancer: 'enhancer',
  universal: 'universal',
}
const FRAME_AFFILIATION_LABELS: Readonly<Record<CardFrameAffiliation, string>> = {
  striker: 'Striker',
  guardian: 'Guardian',
  tactician: 'Tactician',
  enhancer: 'Enhancer',
  universal: 'Universal',
  curse: 'Curse',
}

function resolveFrameAffiliation(
  type: CardType,
  poolClass: CardPoolClass | null | undefined,
): CardFrameAffiliation {
  if (type === 'curse') return 'curse'

  const normalizedPool = typeof poolClass === 'string' ? poolClass.toLowerCase() : ''
  return KNOWN_POOL_CLASSES[normalizedPool] ?? 'universal'
}

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
  /** Authoritative original cost even when the lower visual is not needed. */
  costOriginal?: number | null
  comboReductionEligible?: boolean
  comboContinues?: boolean
  comboRingHighlighted?: boolean
  wildDisabled?: boolean
  /** Show an X over the cost when a status or per-turn limit blocks play. */
  costUnavailable?: boolean
  /** Authoritative ordered cost influences supplied by Core through the DTO. */
  costChanges?: readonly TooltipCostChange[] | null
  type: CardType
  rarity: CardRarity
  /** Acquisition pool used to select the complete affiliation-frame PNG. */
  poolClass?: CardPoolClass | null
  art?: ReactNode
  upgraded?: boolean
  selected?: boolean
  locked?: boolean
  className?: string
  description?: string
  upgradedDescription?: string | null
  /** Keep the complete card face visible while suppressing hover details. */
  detailsDisabled?: boolean
  /** The card is an armed 伏策 waiting in the normal Exhaust pile. */
  contingencyArmed?: boolean
  /** Authoritative trigger label without the `伏策待機中：` prefix. */
  contingencyTriggerText?: string | null
  ariaHidden?: boolean
  onClick?: MouseEventHandler<HTMLDivElement>
  onMouseEnter?: MouseEventHandler<HTMLDivElement>
  onMouseLeave?: MouseEventHandler<HTMLDivElement>
}

export function Card({
  name,
  cost,
  costOrig,
  costOriginal,
  comboReductionEligible = false,
  comboContinues,
  comboRingHighlighted,
  wildDisabled = false,
  costUnavailable = false,
  costChanges,
  type,
  rarity,
  poolClass,
  art,
  upgraded,
  selected,
  locked,
  className,
  description,
  upgradedDescription,
  detailsDisabled = false,
  contingencyArmed = false,
  contingencyTriggerText,
  ariaHidden,
  onClick,
  onMouseEnter,
  onMouseLeave,
}: Props) {
  const frameAffiliation = resolveFrameAffiliation(type, poolClass)
  const frameAffiliationLabel = FRAME_AFFILIATION_LABELS[frameAffiliation]
  const affiliationFrameSrc = cardFrame('affiliations', frameAffiliation)
  const typeFrameSrc = cardFrame('types', type)
  const costFrameSrc = cardFrame('costs', frameAffiliation)
  const comboAuraActive = comboRingHighlighted ?? comboContinues ?? comboReductionEligible
  const classes = [
    'card',
    `card--type-${type}`,
    `card--rarity-${rarity}`,
    `card--pool-${frameAffiliation}`,
    comboAuraActive && 'is-combo-continuing',
    selected && 'is-selected',
    locked && 'is-locked',
    contingencyArmed && 'is-contingency-armed',
    className,
  ]
    .filter(Boolean)
    .join(' ')

  // The visible word is baked into the type frame; this label is for assistive technology.
  const typeLabel = type === 'unit' ? 'SUMMON' : type.toUpperCase()

  // Phase 10.5.M6.7: card / unit catalog を読んで marker → JP 名解決に使う。
  //  catalog 未ロード時は names = {} で CardDesc が ID フォールバック。
  const { names: cardCatalogNames } = useCardCatalog()
  const { names: unitCatalogNames } = useUnitCatalog()

  // Why: 右クリック長押し中は upgraded ⇄ unupgraded を反転表示する
  //  (ユーザ要望: 全領域・全リスト共通で +/非+ を比較可能にする)。
  //  upgradedDescription が無いカードでは反転しても意味が無いので無効化。
  const [previewToggle, setPreviewToggle] = useState(false)
  const canPreviewToggle = !detailsDisabled && !!upgradedDescription
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
  const numericCost = typeof cost === 'number' ? cost : null
  // `costOriginal` is the authoritative DTO field. Explicit null means that
  // no original-cost comparison should be shown; only undefined falls back to
  // the legacy `costOrig` alias used by older callers.
  const displayedOriginalCost = costOriginal !== undefined ? costOriginal : costOrig
  const hasCostDelta = displayedOriginalCost !== null
    && displayedOriginalCost !== undefined
    && String(displayedOriginalCost) !== String(cost)
  const numericOriginalCost = typeof displayedOriginalCost === 'number'
    ? displayedOriginalCost
    : null
  const numericBreakdownOriginal = numericOriginalCost
  const costNumberClass = numericOriginalCost === null || numericCost === null
    ? 'card__cost-n--same'
    : numericCost < numericOriginalCost
      ? 'card__cost-n--reduced'
      : numericCost > numericOriginalCost
        ? 'card__cost-n--increased'
        : 'card__cost-n--same'
  const costChangeAccessibilityLabel = !hasCostDelta
    ? null
    : numericOriginalCost !== null && numericCost !== null
      ? numericCost < numericOriginalCost
        ? '軽減'
        : numericCost > numericOriginalCost
          ? '増加'
          : '変更'
      : '変更'
  const cardStateAccessibilityLabel = [
    costUnavailable ? '使用不可' : null,
    `現在コスト${cost}`,
    hasCostDelta ? `元コスト${displayedOriginalCost}` : null,
    costChangeAccessibilityLabel,
    comboAuraActive ? 'コンボ継続可能' : null,
  ].filter(Boolean).join('、') + '。'
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (detailsDisabled || !activeDesc) return null
    return {
      name: effectiveUpgraded ? `${name}+` : name,
      rarity,
      desc: activeDesc,
      disabledKeywords: wildDisabled ? ['wild'] : undefined,
      costBreakdown: numericCost !== null
        && numericBreakdownOriginal !== null
        && costChanges
        && costChanges.length > 0
        ? {
            originalCost: numericBreakdownOriginal,
            currentCost: numericCost,
            changes: costChanges,
          }
        : undefined,
    }
  }, [
    activeDesc,
    costChanges,
    detailsDisabled,
    effectiveUpgraded,
    name,
    numericCost,
    numericBreakdownOriginal,
    rarity,
    wildDisabled,
  ])
  const tip = useTooltipTarget(tooltipContent)
  const contingencyLabel = contingencyArmed
    ? `伏策待機中：${contingencyTriggerText ?? '発動条件待ち'}`
    : null

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
  const handleKeyDown: KeyboardEventHandler<HTMLDivElement> = (e) => {
    if (!onClick || (e.key !== 'Enter' && e.key !== ' ')) return
    e.preventDefault()
    // Dispatch the same click path as pointer activation so every caller keeps
    // its existing event contract and analytics/game-command handling.
    e.currentTarget.click()
  }

  // Why: onClick 不在時に role/tabIndex 属性を JSX から外して
  // axe/aria の誤検出 (条件式の静的解析不可) を回避する目的。
  const interactiveProps = onClick
    ? { onClick, onKeyDown: handleKeyDown, role: 'button' as const, tabIndex: 0 }
    : {}
  return (
    <div
      className={classes}
      data-card-pool-class={frameAffiliationLabel}
      data-card-affiliation={frameAffiliation}
      aria-hidden={ariaHidden}
      onMouseEnter={combinedEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={combinedLeave}
      onMouseDown={handleMouseDown}
      onContextMenu={handleContextMenu}
      {...interactiveProps}
    >
      <img
        className="card__affiliation-frame"
        src={affiliationFrameSrc}
        alt=""
        aria-hidden="true"
        draggable={false}
      />
      <div className="card__illust" aria-hidden="true">
        <div className="card__art-content">{art ?? 'ILLUST'}</div>
      </div>
      <img
        className="card__type-frame"
        src={typeFrameSrc}
        alt=""
        aria-hidden="true"
        draggable={false}
      />
      {comboAuraActive ? (
        <span className="card__combo-aura" aria-hidden="true">
          <span className="card__combo-pulse" />
        </span>
      ) : null}
      {contingencyLabel ? (
        <>
          <div className="card__contingency-ring" aria-hidden="true" />
          <div className="card__contingency-badge" title={contingencyLabel}>
            {contingencyLabel}
          </div>
        </>
      ) : null}
      <div className="card__top">
        {costUnavailable ? (
          <span className="card__cost-unavailable" aria-hidden="true">×</span>
        ) : null}
        {hasCostDelta ? (
          // Why: 現在値と元値をそれぞれ独立した所属別台座へ載せる。元値は
          // 現在値の直下で小さく表示し、実際に支払う値を優先して読ませる。
          <div className="card__cost-stack" aria-hidden="true">
            <div className="card__cost card__cost--current">
              <img
                className="card__cost-base"
                src={costFrameSrc}
                alt=""
                aria-hidden="true"
                draggable={false}
              />
              <span className={`card__cost-n ${costNumberClass}`} data-digits={costDigitBand(cost)}>{cost}</span>
            </div>
            <div className="card__cost card__cost--orig">
              <img
                className="card__cost-base"
                src={costFrameSrc}
                alt=""
                aria-hidden="true"
                draggable={false}
              />
              <span className="card__cost-n" data-digits={costDigitBand(displayedOriginalCost)}>{displayedOriginalCost}</span>
            </div>
          </div>
        ) : (
          <div className="card__cost card__cost--current" aria-hidden="true">
            <img
              className="card__cost-base"
              src={costFrameSrc}
              alt=""
              aria-hidden="true"
              draggable={false}
            />
            <span className={`card__cost-n ${costNumberClass}`} data-digits={costDigitBand(cost)}>{cost}</span>
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
      {/* Why: カード下半分に description (formatter の auto-text) を表示。
          [N:..] / [K:..] / [S:..] 等の marker を CardDesc が span 化、CSS で
          黒フチ + 小さめ font に整形して in-card に収める。 */}
      {activeDesc ? (
        <div className="card__desc">
          {/* Phase 10.5.M6.7: catalog を引いて [C:..] / [U:..] marker を JP 名に解決。
              hook はデフォルト空 names を返すため fetch 失敗環境でも safe (ID 表示)。 */}
          <CardDesc
            text={activeDesc}
            cardNames={cardCatalogNames}
            unitNames={unitCatalogNames}
            disabledKeywords={wildDisabled ? ['wild'] : undefined}
          />
        </div>
      ) : null}
      <span className="card__type">{`TYPE: ${typeLabel}`}</span>
      <span className="card__affiliation-label">{`AFFILIATION: ${frameAffiliationLabel.toUpperCase()}`}</span>
      <span className="card__state-label">{cardStateAccessibilityLabel}</span>
    </div>
  )
}
