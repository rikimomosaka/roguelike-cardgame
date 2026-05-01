// Phase 10.5.M: 構造化フォームの top-level コンポーネント。
// rarity / cardType / cost / upgradedCost / keywords / effects / upgradedEffects /
// description override を編集し、ライブテキスト / ビジュアルプレビューを並べて表示。

import { useEffect, useState } from 'react'
import { previewDescription } from '../../api/dev'
import type { DevMeta } from '../../api/dev'
import type { CardSpec } from './DevSpecTypes'
import { specToJsonObject } from './DevSpecTypes'
import { EffectListEditor } from './EffectListEditor'
import { KeywordSelector } from './KeywordSelector'
import { FormatterPreview } from './FormatterPreview'
import { CardVisualPreview } from './CardVisualPreview'
import './CardSpecForm.css'

type Props = {
  spec: CardSpec
  meta: DevMeta
  allCardIds: string[]
  cardNames: Record<string, string>
  cardName: string
  displayName: string | null
  onChange: (next: CardSpec) => void
}

export function CardSpecForm({
  spec,
  meta,
  allCardIds,
  cardNames,
  cardName,
  displayName,
  onChange,
}: Props) {
  const set = (patch: Partial<CardSpec>) => onChange({ ...spec, ...patch })

  // CardVisualPreview に渡す auto-text を 200ms debounce で内部 state に保持。
  // FormatterPreview と同じ /api/dev/cards/preview を normal/upgraded で 2 回叩く。
  const [normalAuto, setNormalAuto] = useState<string>('')
  const [upgradedAuto, setUpgradedAuto] = useState<string>('')
  const specKey = JSON.stringify(specToJsonObject(spec))

  useEffect(() => {
    let cancelled = false
    const t = window.setTimeout(async () => {
      try {
        const obj = specToJsonObject(spec)
        const [n, u] = await Promise.all([
          previewDescription(obj, false),
          previewDescription(obj, true),
        ])
        if (!cancelled) {
          setNormalAuto(n)
          setUpgradedAuto(u)
        }
      } catch {
        // 失敗時は空文字 (FormatterPreview 側でエラー表示するので、ここでは silently skip)
        if (!cancelled) {
          setNormalAuto('')
          setUpgradedAuto('')
        }
      }
    }, 200)
    return () => {
      cancelled = true
      window.clearTimeout(t)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [specKey])

  const hasUpgraded =
    (spec.upgradedEffects !== null && spec.upgradedEffects.length > 0) ||
    spec.upgradedCost !== null ||
    (spec.upgradedKeywords !== null && spec.upgradedKeywords.length > 0) ||
    spec.upgradedDescription !== null

  return (
    <div className="card-spec-form">
      <div className="card-spec-form__row">
        <label className="card-spec-form__label">
          Rarity
          <select
            value={spec.rarity}
            onChange={(e) => set({ rarity: parseInt(e.target.value, 10) })}
            aria-label="card rarity"
          >
            {meta.rarities.map((r) => (
              <option key={r.value} value={r.value}>
                {r.value}: {r.label}
              </option>
            ))}
          </select>
        </label>
        <label className="card-spec-form__label">
          Card Type
          <select
            value={spec.cardType}
            onChange={(e) => set({ cardType: e.target.value })}
            aria-label="card type"
          >
            {meta.cardTypes.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </label>
        <label className="card-spec-form__label">
          Cost
          <input
            type="number"
            value={spec.cost ?? ''}
            placeholder="(unplayable)"
            onChange={(e) =>
              set({ cost: e.target.value === '' ? null : parseInt(e.target.value, 10) })
            }
            aria-label="card cost"
          />
        </label>
        <label className="card-spec-form__label">
          Upgraded Cost
          <input
            type="number"
            value={spec.upgradedCost ?? ''}
            placeholder="(=cost)"
            onChange={(e) =>
              set({
                upgradedCost: e.target.value === '' ? null : parseInt(e.target.value, 10),
              })
            }
            aria-label="card upgraded cost"
          />
        </label>
      </div>

      <KeywordSelector
        value={spec.keywords}
        meta={meta}
        label="Keywords"
        onChange={(v) => set({ keywords: v })}
      />
      <KeywordSelector
        value={spec.upgradedKeywords}
        meta={meta}
        label="Upgraded Keywords"
        onChange={(v) => set({ upgradedKeywords: v })}
      />

      <EffectListEditor
        effects={spec.effects}
        meta={meta}
        allCardIds={allCardIds}
        label="Effects"
        onChange={(e) => set({ effects: e })}
      />
      <EffectListEditor
        effects={spec.upgradedEffects ?? []}
        meta={meta}
        allCardIds={allCardIds}
        label="Upgraded Effects"
        onChange={(e) => set({ upgradedEffects: e.length === 0 ? null : e })}
      />

      <details className="card-spec-form__desc-override">
        <summary>Description Override (optional)</summary>
        <label className="card-spec-form__label card-spec-form__label--block">
          Description (override)
          <textarea
            value={spec.description ?? ''}
            onChange={(e) => set({ description: e.target.value || null })}
            aria-label="description override"
          />
        </label>
        <label className="card-spec-form__label card-spec-form__label--block">
          Upgraded Description
          <textarea
            value={spec.upgradedDescription ?? ''}
            onChange={(e) => set({ upgradedDescription: e.target.value || null })}
            aria-label="upgraded description override"
          />
        </label>
      </details>

      <div className="card-spec-form__previews">
        <h4 className="card-spec-form__previews-heading">Card Visual Preview</h4>
        <CardVisualPreview
          cardName={cardName}
          displayName={displayName}
          spec={spec}
          normalAutoText={normalAuto}
          upgradedAutoText={upgradedAuto}
        />
        <h4 className="card-spec-form__previews-heading">Auto-Text (raw markers)</h4>
        <FormatterPreview
          spec={spec}
          upgraded={false}
          cardNames={cardNames}
          label="Normal"
        />
        {hasUpgraded && (
          <FormatterPreview
            spec={spec}
            upgraded={true}
            cardNames={cardNames}
            label="Upgraded"
          />
        )}
      </div>
    </div>
  )
}
