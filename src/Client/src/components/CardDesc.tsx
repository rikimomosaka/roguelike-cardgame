import { useLayoutEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import './CardDesc.css'

// Why: キーワード ID → 日本語表示名 + 説明文。Core 側 CardKeywords 辞書と同期。
//   tooltip の二段目で定義 popup を出すために description も保持。
export const KEYWORD_DEFS: Record<string, { name: string; desc: string }> = {
  wild: {
    name: 'ワイルド',
    desc: 'プレイ時、コスト連番に関係なくコンボが継続する。',
  },
  superwild: {
    name: 'スーパーワイルド',
    desc: 'プレイ時、コンボが継続する。さらに次にプレイするカードもコンボ継続が保証される。',
  },
  wait: {
    name: '待機',
    desc: 'このカードはプレイ後も捨札に行かず、次ターンに手札へ持ち越される。',
  },
  exhaust: {
    name: '消費',
    desc: 'プレイ後、このカードは捨札ではなく除外山札に送られる (戦闘終了まで戻らない)。',
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
  omnistrike: { name: '全体攻撃', desc: '攻撃が敵全体に当たる。X ターン残存。' },
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
 * Phase 10.5.M3:
 *   - white-space: nowrap で 1 文に保つ
 *   - 自然幅 > 親幅 のとき transform: scaleX(ratio) で横方向に圧縮
 *   - 自然幅 > 親幅 × 1.5 のとき (圧縮しすぎる) は wrap モードに切替えて 2-3 行表示
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

    // リセットして自然幅を取り直す
    wrapper.classList.remove('card-desc-line--wrap')
    inner.style.transform = ''
    inner.style.width = ''

    const containerWidth = wrapper.clientWidth
    if (containerWidth <= 0) return  // 未レイアウト

    const naturalWidth = inner.scrollWidth
    if (naturalWidth <= containerWidth) {
      // 余裕で収まる
      return
    }

    // 1.5 倍超 → wrap モード (2-3 行)
    if (naturalWidth > containerWidth * 1.5) {
      wrapper.classList.add('card-desc-line--wrap')
      return
    }

    // それ以外 → scaleX で横方向圧縮
    const ratio = containerWidth / naturalWidth
    inner.style.transform = `scaleX(${ratio.toFixed(3)})`
    inner.style.transformOrigin = 'left center'
    // wrapper の hard-coded 幅で「inner box が圧縮された見え方」になるよう調整
    inner.style.width = `${naturalWidth}px`
  })

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
