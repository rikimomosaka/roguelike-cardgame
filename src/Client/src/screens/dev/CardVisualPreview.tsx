// Phase 10.5.M: ゲーム本編と同じ <Card> 描画でカード見た目をライブプレビュー。
// normal / upgraded を並列表示し、休憩マスの強化プレビュー (.rs-confirm-preview) と同じ
// 「[before] → [after]」レイアウトで配置する。
//
// description / upgradedDescription は preview API から得た auto-text を渡す。
// override (spec.description) があればそれを優先。

import { Card } from '../../components/Card'
import type { CardRarity, CardType } from '../../components/Card'
import type { CardSpec } from './DevSpecTypes'

type Props = {
  cardName: string
  displayName: string | null
  spec: CardSpec
  /** preview API から得た normal 用 auto-text (override 無し時に Card に渡る). */
  normalAutoText: string
  /** preview API から得た upgraded 用 auto-text. */
  upgradedAutoText: string
}

const RARITY_NUM_TO_CHAR: Record<number, CardRarity> = {
  // promo は common と同色
  0: 'c',
  1: 'c',
  2: 'r',
  3: 'e',
  4: 'l',
  5: 't',
}

const CARDTYPE_TO_LOWER: Record<string, CardType> = {
  Attack: 'attack',
  Skill: 'skill',
  Power: 'power',
  Curse: 'curse',
  Status: 'status',
  Unit: 'unit',
}

export function CardVisualPreview({
  cardName,
  displayName,
  spec,
  normalAutoText,
  upgradedAutoText,
}: Props) {
  const rarity: CardRarity = RARITY_NUM_TO_CHAR[spec.rarity] ?? 'c'
  const type: CardType = CARDTYPE_TO_LOWER[spec.cardType] ?? 'attack'
  // upgraded 表現があるカードかどうか (cost 差分 / effects 差分 / keywords 差分 のいずれか)
  const isUpgradable =
    spec.upgradedCost !== null ||
    (spec.upgradedEffects !== null && spec.upgradedEffects.length > 0) ||
    (spec.upgradedKeywords !== null && spec.upgradedKeywords.length > 0) ||
    spec.upgradedDescription !== null

  const normalCost: number | string = spec.cost ?? 'X'
  const upgradedCost: number | string =
    spec.upgradedCost ?? spec.cost ?? 'X'

  const normalDesc = spec.description ?? normalAutoText
  const upgradedDesc = spec.upgradedDescription ?? upgradedAutoText

  const name = displayName && displayName.length > 0 ? displayName : cardName

  return (
    <div className="dev-card-visual-preview">
      <div className="dev-card-visual-preview__panel">
        <div className="dev-card-visual-preview__caption">通常</div>
        <Card
          name={name}
          cost={normalCost}
          type={type}
          rarity={rarity}
          upgraded={false}
          description={normalDesc}
          upgradedDescription={isUpgradable ? upgradedDesc : null}
          width={128}
        />
      </div>
      {isUpgradable && (
        <>
          <span className="dev-card-visual-preview__arrow" aria-hidden="true">
            →
          </span>
          <div className="dev-card-visual-preview__panel">
            <div className="dev-card-visual-preview__caption">強化後</div>
            <Card
              name={name}
              cost={upgradedCost}
              type={type}
              rarity={rarity}
              upgraded={true}
              description={normalDesc}
              upgradedDescription={upgradedDesc}
              width={128}
            />
          </div>
        </>
      )}
    </div>
  )
}
