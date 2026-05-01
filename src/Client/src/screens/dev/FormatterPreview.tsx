// Phase 10.5.M: 200ms debounce で /api/dev/cards/preview を叩き、
// auto-text を CardDesc で render するライブプレビュー。

import { useEffect, useState } from 'react'
import { previewDescription } from '../../api/dev'
import { CardDesc } from '../../components/CardDesc'
import type { CardSpec } from './DevSpecTypes'
import { specToJsonObject } from './DevSpecTypes'

type Props = {
  spec: CardSpec
  upgraded: boolean
  cardNames: Record<string, string>
  label?: string
}

export function FormatterPreview({ spec, upgraded, cardNames, label }: Props) {
  const [state, setState] = useState<{ text: string; error: string | null }>({
    text: '',
    error: null,
  })

  // Why: spec の参照比較は毎 render で別物 → JSON.stringify を deps にして
  //   実値比較。性能は十分 (effect は ms オーダーの debounce 実行)。
  const specKey = JSON.stringify(specToJsonObject(spec))

  useEffect(() => {
    let cancelled = false
    const t = window.setTimeout(async () => {
      try {
        const result = await previewDescription(specToJsonObject(spec), upgraded)
        if (!cancelled) setState({ text: result, error: null })
      } catch (e) {
        if (!cancelled) setState({ text: '', error: String(e) })
      }
    }, 200)
    return () => {
      cancelled = true
      window.clearTimeout(t)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [specKey, upgraded])

  const { text, error } = state

  return (
    <div className="formatter-preview">
      {label && <div className="formatter-preview__label">{label}</div>}
      {error ? (
        <div className="dev-error formatter-preview__error">Preview error: {error}</div>
      ) : (
        <div className="formatter-preview__body">
          {text ? <CardDesc text={text} cardNames={cardNames} /> : <em>(empty)</em>}
        </div>
      )}
    </div>
  )
}
