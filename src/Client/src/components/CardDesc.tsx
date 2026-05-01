import type { ReactNode } from 'react'
import './CardDesc.css'

// Why: キーワード ID → 日本語表示名。Core 側 CardKeywords 辞書 (10.5.B) と同期。
//   将来 catalog API で配布する想定だが、現状は静的にミラー。
const KEYWORD_JP: Record<string, string> = {
  wild: 'ワイルド',
  superwild: 'スーパーワイルド',
}

// Why: パワーカードの発火タイミング ID → 日本語表示。10.5.E で engine 実装予定だが
//   text marker は本フェーズで先行採用。
const TRIGGER_JP: Record<string, string> = {
  OnTurnStart: 'ターン開始時',
  OnTurnEnd: 'ターン終了時',
  OnPlayCard: 'カードプレイ時',
  OnDamageReceived: 'ダメージ受け時',
  OnCombo: 'コンボ達成時',
}

type Props = {
  text: string
  /** [C:cardId] のカード id → 表示名マップ。catalog から渡す。 */
  cardNames?: Record<string, string>
}

// [N:5] / [K:wild] / [T:OnTurnStart] / [V:X|手札の数] / [C:strike]
const MARKER_RE = /\[(N|K|T|V|C):([^\]|]+)(?:\|([^\]]+))?\]/g

/**
 * カード description のリッチテキスト描画。
 * formatter 出力の marker syntax を解析し、span に変換して色分け表示する。
 *
 * spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §1-3 Q9
 * 関連 plan: docs/superpowers/plans/2026-05-01-phase10-5B-formatter-v2.md
 */
export function CardDesc({ text, cardNames = {} }: Props) {
  // Why: テスト固定値や JSON 由来の "\n" (2 文字 escape) も実改行として扱う。
  const normalized = text.replace(/\\n/g, '\n')
  const lines = normalized.split('\n')
  return (
    <span className="card-desc">
      {lines.map((line, lineIdx) => (
        <span key={lineIdx} className="card-desc-line">
          {renderLine(line, cardNames)}
          {lineIdx < lines.length - 1 ? <br /> : null}
        </span>
      ))}
    </span>
  )
}

function renderLine(line: string, cardNames: Record<string, string>): ReactNode[] {
  const parts: ReactNode[] = []
  let lastIndex = 0
  let key = 0
  // matchAll で全 marker を順次取り出し、間のテキストと交互に push する。
  for (const m of line.matchAll(MARKER_RE)) {
    const idx = m.index ?? 0
    if (idx > lastIndex) parts.push(line.slice(lastIndex, idx))
    const [, kind, value, extra] = m
    parts.push(renderMarker(kind, value, extra, cardNames, key++))
    lastIndex = idx + m[0].length
  }
  if (lastIndex < line.length) parts.push(line.slice(lastIndex))
  return parts
}

function renderMarker(
  kind: string,
  value: string,
  extra: string | undefined,
  cardNames: Record<string, string>,
  key: number,
): ReactNode {
  switch (kind) {
    case 'N': {
      // Why: 10.5.C で formatter が `[N:7|up]` / `[N:3|down]` を emit する。
      //   battle 中の hero 統計を反映した結果、base より上振れ → 赤、下振れ → 青。
      //   extra 無しの `[N:5]` は黄 (デフォルト) のまま。
      const cls = ['card-desc-num']
      if (extra === 'up') cls.push('card-desc-num--up')
      else if (extra === 'down') cls.push('card-desc-num--down')
      return (
        <span key={key} className={cls.join(' ')}>
          {value}
        </span>
      )
    }
    case 'K': {
      const jp = KEYWORD_JP[value] ?? value
      return (
        <span key={key} className="card-desc-keyword" data-keyword={value}>
          {jp}
        </span>
      )
    }
    case 'T': {
      const jp = TRIGGER_JP[value] ?? value
      return (
        <span key={key} className="card-desc-trigger">
          {jp}
        </span>
      )
    }
    case 'V': {
      const label = extra ?? '?'
      return (
        <span key={key} className="card-desc-var">
          {value}(Xは{label})
        </span>
      )
    }
    case 'C': {
      const name = cardNames[value] ?? value
      return (
        <span key={key} className="card-desc-cardref">
          {name}
        </span>
      )
    }
    default:
      return <span key={key}>{value}</span>
  }
}
