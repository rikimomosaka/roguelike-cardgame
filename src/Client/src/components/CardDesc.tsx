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
  // M6.7: summon action から keyword 化。色は status と同じオレンジ系 (CSS 上書き)。
  summon: {
    name: '召喚',
    desc: '指定されたユニットを味方として呼び出す。',
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

// Why: relic / power カード共通の発火タイミング ID → 日本語表示。
// Phase 10.5.L1.5: relic + power 統合に伴い 18 値の JP ラベル。
const TRIGGER_JP: Record<string, string> = {
  OnPickup: 'レリック取得時',
  OnBattleStart: '戦闘開始時',
  OnBattleEnd: '戦闘終了時',
  OnTurnStart: 'ターン開始時',
  OnTurnEnd: 'ターン終了時',
  OnPlayCard: 'カードプレイ時',
  OnEnemyDeath: '敵撃破時',
  OnDamageReceived: 'ダメージ受け時',
  OnCombo: 'コンボ達成時',
  OnMapTileResolved: 'マスイベント解決後',
  OnCardDiscarded: 'カード捨て時',
  OnCardExhausted: 'カード除外時',
  OnEnterShop: 'ショップ訪問時',
  OnEnterRestSite: '休憩所訪問時',
  OnRest: '休憩時',
  OnRewardGenerated: '報酬生成時',
  OnCardAddedToDeck: 'デッキ追加時',
  Passive: '常時',
}

/**
 * description テキストから参照されるキーワード / status / カード ID を抽出する。
 * tooltip の二段目で定義 popup を出すための入力。
 * 重複は除去 (順序は最初の出現順を維持)。
 *
 * Phase 10.5.M6.9: unit ref ([U:wisp] 等) は補足対象外。理由: ユニット名はキーワード
 *   ではないので「召喚」のような効果説明とは性質が違う。将来的にユニットのステータス
 *   や行動説明を出す場合は専用の別 popup を用意する想定。
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
    // Phase 10.5.M6.9: unit ([U:..]) は intentional に補足対象から除外。
  }
  return refs
}

type Props = {
  text: string
  /** [C:cardId] のカード id → 表示名マップ。catalog から渡す。 */
  cardNames?: Record<string, string>
  /** [U:unitId] のユニット id → 表示名マップ。unit catalog から渡す。 */
  unitNames?: Record<string, string>
  /**
   * 自動文字幅圧縮の有効/無効。default true。
   * Why: カード本体は固定狭幅枠で 1 行に収めるため圧縮が必要だが、tooltip / dev preview
   *   など max-width が広く折返し許容な場所では Stage 2/3 (font-size 8px/7px) が
   *   発動してしまい異常に小さく見えるため、それらの呼出元では false にする。
   */
  compress?: boolean
}

// [N:5] / [K:wild] / [S:strength] / [T:OnTurnStart] / [V:X|手札の数] / [C:strike] / [U:wisp]
const MARKER_RE = /\[(N|K|S|T|V|C|U):([^\]|]+)(?:\|([^\]]+))?\]/g

/**
 * カード description のリッチテキスト描画。
 * formatter 出力の marker syntax を解析し、span に変換して色分け表示する。
 *
 * spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §1-3 Q9
 * 関連 plan: docs/superpowers/plans/2026-05-01-phase10-5B-formatter-v2.md
 */
export function CardDesc({ text, cardNames = {}, unitNames = {}, compress = true }: Props) {
  // Why: テスト固定値や JSON 由来の "\n" (2 文字 escape) も実改行として扱う。
  const normalized = text.replace(/\\n/g, '\n')
  const lines = normalized.split('\n')
  if (!compress) {
    // 圧縮ロジックを skip。CSS の white-space: pre-wrap で自然折返し。
    return (
      <span className="card-desc card-desc--no-compress">
        {lines.map((line, lineIdx) => (
          <span key={lineIdx} className="card-desc-line-plain">
            {renderLine(line, cardNames, unitNames)}
            {lineIdx < lines.length - 1 ? '\n' : null}
          </span>
        ))}
      </span>
    )
  }
  return (
    <span className="card-desc">
      {lines.map((line, lineIdx) => (
        <CardDescLine key={lineIdx} line={line} cardNames={cardNames} unitNames={unitNames} />
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
  unitNames,
}: {
  line: string
  cardNames: Record<string, string>
  unitNames: Record<string, string>
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
      inner.style.fontSize = ''
      inner.style.letterSpacing = ''

      const containerWidth = wrapper.clientWidth
      if (containerWidth <= 0) return  // 未レイアウト
      const initialNatural = inner.scrollWidth
      if (initialNatural <= containerWidth) return  // 余裕で収まる

      // Phase 10.5.M6.9: 多段階の geometric 圧縮 (letter-spacing → font-size 縮小 → wrap)。
      //  旧 scaleX 最終手段は visual のみ縮み、wrapper の overflow:hidden が
      //  geometric size 基準のため両端が切れるバグの原因 → 廃止。
      //  各段で `void inner.offsetHeight` で reflow を強制 → scrollWidth が
      //  letter-spacing/font-size 変更を確実に反映するようにする。
      //  letter-spacing と font-size を細かく刻み、最終的に 6px font まで
      //  攻めることで 14-18 字程度の long card text も 1 行で収める。

      const measure = (): number => {
        // Force reflow: 一部ブラウザは style 変更直後の scrollWidth を
        //  キャッシュ値で返すことがあるため、offsetHeight 読み出しで強制再計算。
        void inner.offsetHeight
        return inner.scrollWidth
      }

      // Stage 1a-c: letter-spacing のみで 1 行に収める試み (-0.4 / -0.6 / -0.8 / -1.0px)
      const lsSteps = [-0.4, -0.6, -0.8, -1.0]
      for (const ls of lsSteps) {
        inner.style.letterSpacing = `${ls}px`
        if (measure() <= containerWidth) return
      }

      // Stage 2: font-size を 8.5 → 6px へ段階的に縮小、各段で letter-spacing も調整。
      //   長文で letter-spacing 限界に達したら font-size を縮める方が読みやすい。
      const fontSteps: Array<[string, string]> = [
        ['8.5px', '-0.5px'],
        ['8px', '-0.6px'],
        ['7.5px', '-0.5px'],
        ['7px', '-0.4px'],
        ['6.5px', '-0.4px'],
        ['6px', '-0.3px'],
      ]
      for (const [fs, ls] of fontSteps) {
        inner.style.fontSize = fs
        inner.style.letterSpacing = ls
        if (measure() <= containerWidth) return
      }

      // Stage 3: 最小設定でも収まらない → wrap モード (2-3 行に折返し)
      //  wrap モードでは font-size / letter-spacing をリセット (親 .card__desc
      //  のデフォルト 9px に戻して読みやすさを優先)。
      inner.style.fontSize = ''
      inner.style.letterSpacing = ''
      wrapper.classList.add('card-desc-line--wrap')
    }

    recompute()

    // wrapper サイズ変動 (initial layout 確定後 / 親リサイズ等) でも再計算。
    //  inner は観察しない: recompute が font-size を変えると inner サイズも
    //  変わるため observe(inner) は循環 loop の原因になる (M6.10 反省)。
    //  catalog 後ロードで marker → JP 名置換され文字数が変わるケースは
    //  effect の dep (line / cardNames / unitNames) の変化で検出する。
    if (typeof ResizeObserver !== 'undefined') {
      const ro = new ResizeObserver(() => recompute())
      ro.observe(wrapper)
      return () => ro.disconnect()
    }
    return undefined
    // line / cardNames / unitNames が変わったら再計算 (catalog 後ロード対応)。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [line, JSON.stringify(cardNames), JSON.stringify(unitNames)])

  return (
    <span ref={wrapperRef} className="card-desc-line">
      <span ref={innerRef} className="card-desc-line-inner">
        {renderLine(line, cardNames, unitNames)}
      </span>
    </span>
  )
}

function renderLine(
  line: string,
  cardNames: Record<string, string>,
  unitNames: Record<string, string>,
): ReactNode[] {
  const parts: ReactNode[] = []
  let lastIndex = 0
  let key = 0
  // matchAll で全 marker を順次取り出し、間のテキストと交互に push する。
  for (const m of line.matchAll(MARKER_RE)) {
    const idx = m.index ?? 0
    if (idx > lastIndex) parts.push(line.slice(lastIndex, idx))
    const [, kind, value, extra] = m
    parts.push(renderMarker(kind, value, extra, cardNames, unitNames, key++))
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
  unitNames: Record<string, string>,
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
    case 'U': {
      // Phase 10.5.M6.7: unit ID → JP 名 (catalog 経由)。catalog 未ロード or
      //  ID が無ければそのまま ID を表示。色は cardref と同系統 (紫)。
      const name = unitNames[value] ?? value
      return (
        <span key={key} className="card-desc-unitref">
          {name}
        </span>
      )
    }
    default:
      return <span key={key}>{value}</span>
  }
}
