// Phase 10.5.M: keyword 配列を multi-select checkbox UI で編集。

import type { DevMeta } from '../../api/dev'

type Props = {
  value: string[] | null
  meta: DevMeta
  label: string
  onChange: (next: string[] | null) => void
}

export function KeywordSelector({ value, meta, label, onChange }: Props) {
  const set = new Set(value ?? [])
  const toggle = (id: string) => {
    if (set.has(id)) set.delete(id)
    else set.add(id)
    const arr = Array.from(set)
    // [] は「この版にはキーワードなし」を明示する値。null（未設定）とは分ける。
    onChange(arr)
  }

  return (
    <div className="keyword-selector">
      <div className="keyword-selector__header">
        <div className="keyword-selector__label">{label}</div>
        {value === null ? (
          <button
            type="button"
            className="keyword-selector__copy"
            onClick={() => onChange([])}
            aria-label={`${label}を個別設定`}
          >
            この版を個別設定
          </button>
        ) : (
          <button
            type="button"
            className="keyword-selector__copy"
            onClick={() => onChange(null)}
            aria-label={`${label}の設定を削除`}
          >
            設定を削除
          </button>
        )}
      </div>
      <div className="keyword-selector__checks">
        {value === null ? (
          <em className="keyword-selector__empty">(未設定。この版ではキーワードなし)</em>
        ) : meta.keywords.length === 0 ? (
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
