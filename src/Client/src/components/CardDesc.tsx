import { useLayoutEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import './CardDesc.css'

// Why: キーワード ID → 日本語表示名 + 説明文。Core 側 CardKeywords 辞書と同期。
//   tooltip の二段目で定義 popup を出すために description も保持。
export const KEYWORD_DEFS: Record<string, { name: string; desc: string }> = {
  wild: {
    name: 'ワイルド',
    desc: 'このカードではコンボが途切れない。このコンボ中、以降のワイルドを無効にする。',
  },
  superwild: {
    name: 'スーパーワイルド',
    desc: 'このカード及び次に使うカードではコンボが途切れない。このコンボ中、以降のワイルドを無効にする。',
  },
  wait: {
    name: '待機',
    desc: '手札にある限り、次のターンに持ち越される。',
  },
  exhaust: {
    name: '喪失',
    desc: 'このカードを除外する。',
  },
}

// Why: status ID → 日本語名 + 効果説明。tooltip 二段目で定義 popup 表示用。
//   Core 側 (CardTextFormatter.JpStatusName / Server meta endpoint) と同期。
export const STATUS_DEFS: Record<string, { name: string; desc: string }> = {
  strength: { name: '筋力', desc: '与えるダメージが X 増加する。' },
  dexterity: { name: '敏捷', desc: '得るブロックが X 増加する。' },
  weak: { name: '脱力', desc: '与えるダメージが 0.75 倍になる (端数切捨)。X ターン残存。' },
  vulnerable: { name: '脆弱', desc: '受けるダメージが 1.5 倍になる。X ターン残存。' },
  poison: { name: '毒', desc: 'ターン開始時に X ダメージを受け、X が 1 減る。' },
  omnistrike: { name: '拡散', desc: '全ての攻撃を全体攻撃に変更する。X ターン残存。' },
}

// Why: パワーカードの発火タイミング ID → 日本語表示。
const TRIGGER_JP: Record<string, string> = {
  OnTurnStart: 'ターン開始時',
  OnTurnEnd: 'ターン終了時',
  OnPlayCard: 'カードプレイ時',
  OnDamageReceived: 'ダメージ受け時',
  OnCombo: 'コンボ達成時',
}

/**
 * description テキストから参照されるキーワード / status / カード ID を抽出する。
 * tooltip の二段目で定義 popup を出すための入力。
 * 重複は除去 (順序は最初の出現順を維持)。
 */
export type CardDescRef =
  | { kind: 'keyword'; id: string; name: string; desc: string }
  | { kind: 'status'; id: string; name: string; desc: string }
  | { kind: 'card'; id: string; name: string }

export function extractCardDescRefs(
  text: string,
  cardNames: Record<string, string> = {},
): CardDescRef[] {
  const seen = new Set<string>()
  const refs: CardDescRef[] = []
  for (const m of text.matchAll(MARKER_RE)) {
    const [, kind, value] = m
    const key = `${kind}:${value}`
    if (seen.has(key)) continue
    seen.add(key)
    if (kind === 'K') {
      const def = KEYWORD_DEFS[value]
      if (def) refs.push({ kind: 'keyword', id: value, name: def.name, desc: def.desc })
    } else if (kind === 'S') {
      const def = STATUS_DEFS[value]
      if (def) refs.push({ kind: 'status', id: value, name: def.name, desc: def.desc })
    } else if (kind === 'C') {
      const name = cardNames[value] ?? value
      refs.push({ kind: 'card', id: value, name })
    }
  }
  return refs
}

type Props = {
  text: string
  /** [C:cardId] のカード id → 表示名マップ。catalog から渡す。 */
  cardNames?: Record<string, string>
}

// [N:5] / [K:wild] / [S:strength] / [T:OnTurnStart] / [V:X|手札の数] / [C:strike]
const MARKER_RE = /\[(N|K|S|T|V|C):([^\]|]+)(?:\|([^\]]+))?\]/g

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
        <CardDescLine key={lineIdx} line={line} cardNames={cardNames} />
      ))}
    </span>
  )
}

/**
 * 1 行分の描画 + 自動文字幅圧縮。
 * Phase 10.5.M3 / M4:
 *   - white-space: nowrap で 1 文に保つ
 *   - 自然幅 > 親幅 のとき transform: scaleX(ratio * SAFETY) で横方向に圧縮
 *     (SAFETY = 0.96 で右端余白を確保し、はみ出しを防ぐ)
 *   - 自然幅 > 親幅 × 1.3 のときは wrap モードに切替えて 2-3 行表示
 *   - ResizeObserver で wrapper サイズ変更時に再計算
 */
function CardDescLine({
  line,
  cardNames,
}: {
  line: string
  cardNames: Record<string, string>
}) {
  const wrapperRef = useRef<HTMLSpanElement>(null)
  const innerRef = useRef<HTMLSpanElement>(null)

  useLayoutEffect(() => {
    const wrapper = wrapperRef.current
    const inner = innerRef.current
    if (!wrapper || !inner) return

    const recompute = () => {
      // リセットして自然幅を取り直す
      wrapper.classList.remove('card-desc-line--wrap')
      inner.style.transform = ''
      inner.style.width = ''

      const containerWidth = wrapper.clientWidth
      if (containerWidth <= 0) return  // 未レイアウト
      const naturalWidth = inner.scrollWidth
      if (naturalWidth <= containerWidth) return  // 余裕で収まる

      // 1.3 倍超 → wrap モード (両端切れ防止)
      if (naturalWidth > containerWidth * 1.3) {
        wrapper.classList.add('card-desc-line--wrap')
        return
      }

      // それ以外 → scaleX で横方向圧縮。
      // origin: center にしないと text-align: center の親で inline-block が
      // 中央寄せされた結果、scaleX(ratio) origin: left が左にずれて視覚的に
      // 左端が切れるバグになる (M4-1 修正)。
      // SAFETY = 0.94 で両端 3% ずつ余白を確保する。
      const SAFETY = 0.94
      const ratio = (containerWidth / naturalWidth) * SAFETY
      inner.style.transform = `scaleX(${ratio.toFixed(3)})`
      inner.style.transformOrigin = 'center center'
    }

    recompute()

    // wrapper サイズ変動 (initial layout 確定後 / 親リサイズ等) でも再計算
    if (typeof ResizeObserver !== 'undefined') {
      const ro = new ResizeObserver(() => recompute())
      ro.observe(wrapper)
      return () => ro.disconnect()
    }
    return undefined
  }, [line])

  return (
    <span ref={wrapperRef} className="card-desc-line">
      <span ref={innerRef} className="card-desc-line-inner">
        {renderLine(line, cardNames)}
      </span>
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
      const jp = KEYWORD_DEFS[value]?.name ?? value
      return (
        <span key={key} className="card-desc-keyword" data-keyword={value}>
          {jp}
        </span>
      )
    }
    case 'S': {
      const jp = STATUS_DEFS[value]?.name ?? value
      return (
        <span key={key} className="card-desc-status" data-status={value}>
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
