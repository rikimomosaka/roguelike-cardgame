// Phase 10.5.L1: Relic 用構造化フォーム。
// CardSpecForm を mirror して relic の field のみを編集。
// rarity / trigger / description / implemented / effects (CardEffect 共通) を編集し、
// ライブプレビュー (RelicVisualPreview) を下部に表示。

import { useEffect, useState } from 'react'
import { previewRelicDescription } from '../../api/dev'
import type { DevMeta } from '../../api/dev'
import type { RelicSpec } from './DevSpecTypes'
import { relicSpecToJsonObject } from './DevSpecTypes'
import { EffectListEditor } from './EffectListEditor'
import { RelicVisualPreview } from './RelicVisualPreview'

type Props = {
  relicId: string
  relicName: string
  spec: RelicSpec
  meta: DevMeta
  allCardIds: string[]
  onChange: (next: RelicSpec) => void
}

export function RelicSpecForm({
  relicId,
  relicName,
  spec,
  meta,
  allCardIds,
  onChange,
}: Props) {
  const set = (patch: Partial<RelicSpec>) => onChange({ ...spec, ...patch })

  const [autoDesc, setAutoDesc] = useState<string>('')
  const specKey = JSON.stringify(relicSpecToJsonObject(spec))

  useEffect(() => {
    let cancelled = false
    const t = window.setTimeout(async () => {
      try {
        const obj = relicSpecToJsonObject(spec)
        const d = await previewRelicDescription(obj)
        if (!cancelled) setAutoDesc(d)
      } catch {
        if (!cancelled) setAutoDesc('')
      }
    }, 200)
    return () => {
      cancelled = true
      window.clearTimeout(t)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [specKey])

  return (
    <div className="card-spec-form">
      <div className="card-spec-form__row">
        <label className="card-spec-form__label">
          レアリティ
          <select
            value={spec.rarity}
            onChange={(e) => set({ rarity: parseInt(e.target.value, 10) })}
            aria-label="relic rarity"
          >
            {meta.rarities.map((r) => (
              <option key={r.value} value={r.value}>
                {r.value}: {r.label}
              </option>
            ))}
          </select>
        </label>
        <label className="card-spec-form__label">
          トリガー
          <select
            value={spec.trigger}
            onChange={(e) => set({ trigger: e.target.value })}
            aria-label="relic trigger"
          >
            {meta.relicTriggers.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        </label>
        <label className="card-spec-form__label">
          実装済み
          <input
            type="checkbox"
            checked={spec.implemented}
            onChange={(e) => set({ implemented: e.target.checked })}
            aria-label="relic implemented"
          />
        </label>
      </div>

      <EffectListEditor
        effects={spec.effects}
        meta={meta}
        allCardIds={allCardIds}
        label="効果"
        onChange={(e) => set({ effects: e })}
      />

      <label className="card-spec-form__label card-spec-form__label--block">
        説明文 (手動)
        <textarea
          value={spec.description}
          onChange={(e) => set({ description: e.target.value })}
          aria-label="relic description override"
          placeholder="(空ならサーバ側 formatter が effects から自動生成)"
        />
      </label>

      <div className="card-spec-form__previews">
        <h4 className="card-spec-form__previews-heading">レリックプレビュー</h4>
        <RelicVisualPreview
          relicId={relicId}
          relicName={relicName}
          spec={spec}
          autoDescription={autoDesc}
        />
      </div>
    </div>
  )
}
