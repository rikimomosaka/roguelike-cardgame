// Phase 10.5.M: keyword 配列を multi-select checkbox UI で編集。

import type { DevMeta } from '../../api/dev'

type Props = {
  value: string[] | null
  meta: DevMeta
  label: string
  onChange: (next: string[] | null) => void
  /** Why: 「強化前をコピー」ボタン用。upgradedKeywords 編集時のみ渡される。 */
  onCopyFromNormal?: () => void
}

export function KeywordSelector({ value, meta, label, onChange, onCopyFromNormal }: Props) {
  const set = new Set(value ?? [])
  const toggle = (id: string) => {
    if (set.has(id)) set.delete(id)
    else set.add(id)
    const arr = Array.from(set)
    // 空配列は null として扱い JSON 出力時に省略する
    onChange(arr.length === 0 ? null : arr)
  }

  return (
    <div className="keyword-selector">
      <div className="keyword-selector__header">
        <div className="keyword-selector__label">{label}</div>
        {onCopyFromNormal && (
          <button
            type="button"
            className="keyword-selector__copy"
            onClick={onCopyFromNormal}
            aria-label={`${label} に強化前をコピー`}
          >
            強化前をコピー
          </button>
        )}
      </div>
      <div className="keyword-selector__checks">
        {meta.keywords.length === 0 ? (
          <em className="keyword-selector__empty">(キーワードが定義されていません)</em>
        ) : (
          meta.keywords.map((k) => (
            <label
              key={k.id}
              className="keyword-selector__check"
              title={k.description}
            >
              <input
                type="checkbox"
                checked={set.has(k.id)}
                onChange={() => toggle(k.id)}
                aria-label={`keyword ${k.id}`}
              />
              {k.name}
            </label>
          ))
        )}
      </div>
    </div>
  )
}
