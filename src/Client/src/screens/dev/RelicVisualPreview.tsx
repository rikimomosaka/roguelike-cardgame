// Phase 10.5.L1: 既存 RelicIcon / インラインアイコンを使ったレリックビジュアルプレビュー。
// Phase 10.5.L1.5+ (M6): description を effectText (CardDesc 経由 marker 翻訳) と
//   flavor (フレーバーテキスト、点線で区切って小さめ font) の 2 段で描画する。
//   旧: server combined description をそのまま表示 → marker が解釈されない問題

import type { RelicSpec } from './DevSpecTypes'
import { CardDesc } from '../../components/CardDesc'

type Props = {
  relicId: string
  relicName: string
  spec: RelicSpec
  /** preview API から得た flavor (手動 description) */
  flavor: string
  /** preview API から得た effectText (effects 自動文章化、marker 含む) */
  effectText: string
}

const RARITY_LABEL: Record<number, string> = {
  0: 'Promo',
  1: 'Common',
  2: 'Rare',
  3: 'Epic',
  4: 'Legendary',
  5: 'Token',
}

export function RelicVisualPreview({ relicId, relicName, spec, flavor, effectText }: Props) {
  const rarityLabel = RARITY_LABEL[spec.rarity] ?? 'Unknown'
  const hasEffect = effectText.length > 0
  const hasFlavor = flavor.length > 0

  return (
    <div className="dev-relic-visual-preview">
      <div className="dev-relic-visual-preview__panel">
        <div className="dev-relic-visual-preview__icon">
          <img
            src={`/icons/relics/${relicId}.png`}
            alt={relicName}
            draggable={false}
            onError={(e) => {
              ;(e.currentTarget as HTMLImageElement).style.visibility = 'hidden'
            }}
          />
        </div>
        <div className="dev-relic-visual-preview__meta">
          <div className="dev-relic-visual-preview__name">{relicName || '(無名)'}</div>
          <div className="dev-relic-visual-preview__rarity">
            {rarityLabel}
          </div>
          {/* M6: 効果テキスト (CardDesc で marker → JP / 黄数字 解釈) */}
          {hasEffect && (
            <div className="dev-relic-visual-preview__desc">
              <CardDesc text={effectText} />
            </div>
          )}
          {/* M6: 効果とフレーバーの間に点線区切り (両方ある場合のみ) */}
          {hasEffect && hasFlavor && <div className="dev-relic-visual-preview__sep" aria-hidden="true" />}
          {/* M6: フレーバーは小さめ + 斜体 + 控えめな色 */}
          {hasFlavor && (
            <div className="dev-relic-visual-preview__flavor">{flavor}</div>
          )}
          {!hasEffect && !hasFlavor && (
            <div className="dev-relic-visual-preview__desc">—</div>
          )}
          {!spec.implemented && (
            <div className="dev-relic-visual-preview__unimpl">[未実装]</div>
          )}
        </div>
      </div>
    </div>
  )
}
