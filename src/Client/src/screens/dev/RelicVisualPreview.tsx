// Phase 10.5.L1: 既存 RelicIcon component を再利用してレリックの見た目をライブプレビュー。
// description は手書き override が来ていればそのまま表示、無ければ preview API の自動生成テキスト。

import type { RelicSpec } from './DevSpecTypes'

type Props = {
  relicId: string
  relicName: string
  spec: RelicSpec
  /** preview API から得た description (override 無し時に使う) */
  autoDescription: string
}

const RARITY_LABEL: Record<number, string> = {
  0: 'Promo',
  1: 'Common',
  2: 'Rare',
  3: 'Epic',
  4: 'Legendary',
  5: 'Token',
}

export function RelicVisualPreview({ relicId, relicName, spec, autoDescription }: Props) {
  const rarityLabel = RARITY_LABEL[spec.rarity] ?? 'Unknown'
  const desc =
    spec.description && spec.description.length > 0 ? spec.description : autoDescription

  // RelicIcon は run 中の catalog を必要とするため、ここではインライン軽量プレビューにする。
  // /icons/relics/{id}.png を直接表示。画像が無ければ alt 表示。
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
            {rarityLabel} / {spec.trigger}
          </div>
          <div className="dev-relic-visual-preview__desc">{desc || '—'}</div>
          {!spec.implemented && (
            <div className="dev-relic-visual-preview__unimpl">[未実装]</div>
          )}
        </div>
      </div>
    </div>
  )
}
