import { useLayoutEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { statusIcon } from '../presentation/assets'
import './CardDesc.css'

// Why: キーワード ID → 日本語表示名 + 説明文。Core 側 CardKeywords 辞書と同期。
//   tooltip の二段目で定義 popup を出すために description も保持。
// Shared immutable description data is intentionally colocated with its renderer.
// eslint-disable-next-line react-refresh/only-export-components
export const KEYWORD_DEFS: Record<string, { name: string; desc: string }> = {
  bipolar: {
    name: '双極',
    // X は表示する双極の数に置き換える（keywordDesc）。
    desc: '位相が[P:calm]の時、手札にある限り、次のターンに持ち越される。\n位相が[P:motion]の時、発動時にエナジーを+[N:X]し、双極が消滅する。',
  },
  enhance: {
    name: 'エンハンス',
    desc: '【 】内の効果を与える。',
  },
  invert: {
    name: '反転',
    desc: '位相を持つ場合、対となる位相に変更する。',
  },
  wild: {
    name: 'ワイルド',
    desc: 'このカード及び次に使うカードではコンボが途切れない。このコンボ中、以降のワイルドを無効にする。',
  },
  superwild: {
    name: 'スーパーワイルド',
    desc: 'このカード及び次に使うカードではコンボが途切れない。',
  },
  wait: {
    name: '待機',
    desc: '手札にある限り、次のターンに持ち越される。',
  },
  exhaust: {
    name: '喪失',
    desc: 'このカードを除外する。',
  },
  contingency: {
    name: '伏策',
    desc: '使用時は効果が実行されず、発動待ちとして除外する。伏策条件を満たした時に効果を実行し、捨札へ移動する。',
  },
  momentum: {
    name: '乗勢',
    desc: '効果の実行時、自身のこのターンの最大コンボがX以上であれば適用される。',
  },
  finisher: {
    name: '終撃',
    desc: '効果の適用後、このカードを含むこのコンボ中の全てのアタックカードを保存し、現在コンボを0にする。',
  },
  reprise: {
    name: '再演',
    desc: '再演条件を満たし、この戦闘中に最後に効果の解決を完了した自身のカード1枚の効果を再実行する。',
  },
  // M6.7: summon action から keyword 化。色は status と同じオレンジ系 (CSS 上書き)。
  summon: {
    name: '召喚',
    desc: '指定されたユニットを味方として呼び出す。',
  },
}

// Why: status ID → 日本語名 + 効果説明。tooltip 二段目で定義 popup 表示用。
//   Core 側 (CardTextFormatter.JpStatusName / Server meta endpoint) と同期。
// eslint-disable-next-line react-refresh/only-export-components
export const STATUS_DEFS: Record<string, { name: string; desc: string }> = {
  strength: { name: '筋力', desc: 'アタックで与えるダメージが[N:X]増加する。' },
  dexterity: { name: '敏捷', desc: '得るブロックが[N:X]増加する。' },
  weak: { name: '脱力', desc: 'アタックで与えるダメージが0.75倍になる。ターン終了時に[S:weak]が1減少する。' },
  vulnerable: { name: '脆弱', desc: 'アタックで受けるダメージが1.5倍になる。ターン終了時に[S:vulnerable]が1減少する。' },
  poison: { name: '毒', desc: 'ターン開始時に[N:X]ダメージを受け、[S:poison]が1減少する。' },
  omnistrike: { name: '拡散', desc: '全ての攻撃を全体攻撃に変更する。ターン終了時に[S:omnistrike]が1減少する。' },
  barrier: { name: 'バリア', desc: 'アタックによってHPが1以上減る場合、そのダメージを無効化し、[S:barrier]が1減少する。' },
  lifesteal: { name: '吸血', desc: 'アタックによって減らしたHPの[N:X]%を回復する。' },
  reflect: {
    name: '反射',
    desc: 'アタックを受けた時、攻撃元にアタックの回数分だけ[N:X]ダメージを返す(この際ブロックを無視する)。',
  },
  handCostReduction: {
    name: '節制',
    desc: '手札のカードの使用コストを[N:X]軽減する。カードの使用時に[S:handCostReduction]が1減少する。',
  },
  handCostIncrease: {
    name: '浪費',
    desc: '手札のカードの使用コストを[N:X]増加する。カードの使用時に[S:handCostIncrease]が1減少する。',
  },
  vigor: { name: '元気', desc: 'アタックで与えるダメージが1.5倍になる。ターン終了時に[S:vigor]が1減少する。' },
  pursuit: { name: '追撃', desc: '攻撃直前に、「敵全体に[N:X]アタック。」を得て、[S:pursuit]が1減少する。' },
  regeneration: { name: '再生', desc: 'ターン開始時にHPを[N:X]回復し、[S:regeneration]が1減少する。' },
  fortify: { name: '堅牢', desc: 'ブロックを最大[N:X]まで次のターンに持ち越す。' },
  resistance: { name: '耐性', desc: 'デバフを無効化し、[S:resistance]が1減少する。' },
  revive: { name: '復活', desc: '死亡時に1度だけHPを[N:X]回復し、[S:revive]が消滅する。' },
  growth: { name: '成長', desc: 'ターン開始時に[S:strength][N:X]を得る。' },
  insight: { name: '洞察', desc: 'ターン開始時に[N:X]枚追加で引き、[S:insight]が消滅する。' },
  haste: { name: '加速', desc: 'ターン開始時のエナジー回復量を+[N:X]し、[S:haste]が消滅する。' },
  continuity: { name: '継続', desc: 'コンボ条件を満たさないカードを使った時、コンボを継続し、[S:continuity]が1減少する。' },
  mastery: { name: '熟練', desc: 'ターン開始時にコンボゲージを[N:X]得て、[S:mastery]が1減少する。' },
  virulentPoison: { name: '猛毒', desc: '[S:poison]によって受けるダメージが[N:X]倍になる。' },
  stagnation: { name: '停滞', desc: 'コンボゲージが増えなくなる。ターン終了時に[S:stagnation]が1減少する。' },
  burn: { name: '火傷', desc: 'ターン開始時に現在HPの[N:X]%のダメージを受け、[S:burn]が消滅する。' },
  scorch: { name: '灼痕', desc: '[S:burn]が消滅する時、代わりに[S:scorch]が1減少する。' },
  confusion: { name: '混乱', desc: 'カードが手札へ入った時、その使用コストを0～3のランダムな数値に変更する。ターン終了時に[S:confusion]が1減少する。' },
  curse: { name: '呪縛', desc: 'カードを使用した時に[S:curse]が1減少する。[S:curse]が消滅したターンはカードを使用できなくなる。' },
  bind: { name: '束縛', desc: 'アタックカードを使用できない。ターン終了時に[S:bind]が1減少する。' },
  silence: { name: '沈黙', desc: 'アタック以外のカードを使用できない。ターン終了時に[S:silence]が1減少する。' },
  seal: { name: '封印', desc: '戦技・奥義を使用できない。ターン終了時に[S:seal]が1減少する。' },
  exhaustion: { name: '消耗', desc: 'ターン開始時にエナジーを[N:X]失い、[S:exhaustion]が1減少する。' },
  distress: { name: '動揺', desc: 'カードを使用することでコンボが途切れた時、エナジーとコンボゲージを0にする。ターン終了時に[S:distress]が1減少する。' },
  afterimage: { name: '残光', desc: '自身の攻撃終了時、このターン最後の攻撃を[N:X]回実行し、[S:afterimage]が消滅する。' },
  steadfast: { name: '堅持', desc: 'ターン終了時に消滅するブロックの[N:50]%を次のターンに持ち越す。ターン終了時に[S:steadfast]が1減少する。' },
  cover: { name: '護衛', desc: 'アタックによって味方が受けるダメージを、自身のブロックで可能な限り防ぐ。ターン終了時に[S:cover]が1減少する。' },
  defensiveStance: { name: '守勢', desc: '自身のブロックを使って敵のアタックを完全に防いだ時、コンボゲージを[N:1]得る。ターン終了時に[S:defensiveStance]が1減少する。' },
  comboCarryover: { name: '連環', desc: 'ターン終了時、現在コンボを次のターンへ持ち越し、[S:comboCarryover]が1減少する。次のターンに最初に使用するカードは条件を問わずコンボを継続するが、コンボによるコスト軽減を受けない。' },
  afterglow: { name: '余韻', desc: 'ターン終了時に減少または消滅する自身の他のバフが[N:1]つ以上ある時、それらを全て維持し、[S:afterglow]が消滅する。' },
  resonance: { name: '共鳴', desc: '自身のみに[S:resonance]以外のバフが付与された時、付与された全てのバフを他の全ての味方に付与し、[S:resonance]が1減少する。' },
  disruption: { name: '攪乱', desc: '自身の全てのアタックをランダム攻撃に変更する。ターン終了時に[S:disruption]が1減少する。' },
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
  OnHit: '攻撃命中時',
  OnCombo: 'コンボ達成時',
  OnMapTileResolved: 'マスイベント解決後',
  OnCardDiscarded: 'カード捨て時',
  OnCardExhausted: 'カード除外時',
  OnEnterShop: 'ショップ訪問時',
  OnEnterRestSite: '休憩所訪問時',
  OnRest: '休息時',
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

export type CardDescTerm = Extract<CardDescRef, { kind: 'keyword' | 'status' }>

/** 位相（静・動）の名前と効果。カード本文に静・動が出たときの補足説明と、キャラの説明に使う。 */
export const PHASE_DEFS: Record<'calm' | 'motion', { name: string; desc: string }> = {
  calm: {
    name: '静',
    desc: 'ターン終了時、手札の全アタックに[K:enhance]【[K:bipolar]】する。アタックを使うと、その効果の後に位相がなくなる。',
  },
  motion: {
    name: '動',
    desc: 'ターン終了時の攻撃のダメージが1.5倍になる。アタック以外を使うと、その効果の後に位相がなくなる。',
  },
}

/** キーワードマーカーの付加情報（"+" = エンハンスで後から付いた、数字 = 数）。 */
function parseKeywordExtra(extra: string | undefined): { enhanced: boolean; count: number | null } {
  const raw = extra ?? ''
  const enhanced = raw.startsWith('+')
  const digits = enhanced ? raw.slice(1) : raw
  return { enhanced, count: /^\d+$/.test(digits) ? Number(digits) : null }
}

/** キーワードの説明。X はその場の数（双極2 なら 2、数が無ければ 1）に置き換える。 */
function keywordDesc(desc: string, count: number | null): string {
  return desc.replace('[N:X]', `[N:${count ?? 1}]`)
}

// Pure parser used by Tooltip; it has no Fast Refresh state of its own.
// eslint-disable-next-line react-refresh/only-export-components
export function extractCardDescRefs(
  text: string,
  cardNames: Record<string, string> = {},
): CardDescRef[] {
  const seen = new Set<string>()
  const refs: CardDescRef[] = []
  for (const m of text.matchAll(MARKER_RE)) {
    const [, kind, value, extra] = m
    const key = `${kind}:${value}`
    if (seen.has(key)) continue
    seen.add(key)
    if (kind === 'K') {
      const def = KEYWORD_DEFS[value]
      if (def) {
        const { count } = parseKeywordExtra(extra)
        refs.push({ kind: 'keyword', id: value, name: def.name, desc: keywordDesc(def.desc, count) })
      }
    } else if (kind === 'P') {
      const def = value === 'calm' || value === 'motion' ? PHASE_DEFS[value] : undefined
      if (def) refs.push({ kind: 'keyword', id: `phase:${value}`, name: def.name, desc: def.desc })
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
  /** Keyword ids whose visible label is disabled for this card instance. */
  disabledKeywords?: readonly string[]
  /** Optional renderer used by authoring previews to make known terms independently focusable. */
  renderTerm?: (term: CardDescTerm, rendered: ReactNode, key: number) => ReactNode
  /**
   * 自動文字幅圧縮の有効/無効。default true。
   * Why: カード本体は固定狭幅枠で 1 行に収めるため圧縮が必要だが、tooltip / dev preview
   *   など max-width が広く折返し許容な場所では Stage 2/3 (font-size 8px/7px) が
   *   発動してしまい異常に小さく見えるため、それらの呼出元では false にする。
   */
  compress?: boolean
}

// [N:5] / [K:wild] / [S:strength] / [T:OnTurnStart] / [V:X|手札の数] / [C:strike] / [U:wisp]
// [R:6|up:自身の攻撃力] = 戦闘中に解決済みの能力値参照（値|上振れ下振れ:参照ラベル）
// [P:calm] / [P:motion] = 位相の名前（静 = 青、動 = 赤）
const MARKER_RE = /\[(N|K|S|T|V|C|U|R|P):([^\]|]+)(?:\|([^\]]+))?\]/g

/**
 * カード description のリッチテキスト描画。
 * formatter 出力の marker syntax を解析し、span に変換して色分け表示する。
 *
 * spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §1-3 Q9
 * 関連 plan: docs/superpowers/plans/2026-05-01-phase10-5B-formatter-v2.md
 */
export function CardDesc({
  text,
  cardNames = {},
  unitNames = {},
  disabledKeywords = [],
  renderTerm,
  compress = true,
}: Props) {
  // Why: テスト固定値や JSON 由来の "\n" (2 文字 escape) も実改行として扱う。
  const normalized = text.replace(/\\n/g, '\n')
  const lines = normalized.split('\n')
  if (!compress) {
    // 圧縮ロジックを skip。CSS の white-space: pre-wrap で自然折返し。
    return (
      <span className="card-desc card-desc--no-compress">
        {lines.map((line, lineIdx) => (
          <span
            key={lineIdx}
            className={`card-desc-line-plain${isUsageLimitReachedLine(line) ? ' card-desc-line--usage-limit-reached' : ''}`}
          >
            {renderLine(line, cardNames, unitNames, disabledKeywords, renderTerm, true)}
            {lineIdx < lines.length - 1 ? '\n' : null}
          </span>
        ))}
      </span>
    )
  }
  return (
    <span
      className="card-desc"
      data-lines={Math.min(lines.length, 5)}
      data-line-overflow={lines.length > 5 ? 'true' : undefined}
    >
      {lines.map((line, lineIdx) => (
        <CardDescLine
          key={lineIdx}
          line={line}
          cardNames={cardNames}
          unitNames={unitNames}
          disabledKeywords={disabledKeywords}
          renderTerm={renderTerm}
        />
      ))}
    </span>
  )
}

/**
 * 1 行分の描画 + 自動文字幅圧縮。
 * Phase 10.5.M3 / M4:
 *   - white-space: nowrap で 1 文に保つ
 *   - 短文の 9px 表示は変更せず、長文だけ字間を軽く詰めてから scaleX する
 *   - font-size と formatter が決めた改行は変更しない
 *   - ResizeObserver で wrapper サイズ変更時に再計算
 */
function CardDescLine({
  line,
  cardNames,
  unitNames,
  disabledKeywords,
  renderTerm,
}: {
  line: string
  cardNames: Record<string, string>
  unitNames: Record<string, string>
  disabledKeywords: readonly string[]
  renderTerm?: (term: CardDescTerm, rendered: ReactNode, key: number) => ReactNode
}) {
  const wrapperRef = useRef<HTMLSpanElement>(null)
  const innerRef = useRef<HTMLSpanElement>(null)
  const fitDependency = JSON.stringify([line, cardNames, unitNames, disabledKeywords])

  useLayoutEffect(() => {
    const wrapper = wrapperRef.current
    const inner = innerRef.current
    if (!wrapper || !inner) return

    const recompute = () => {
      // リセットして自然幅を取り直す
      wrapper.classList.remove('card-desc-line--scaled')
      inner.style.transform = ''
      inner.style.left = ''
      inner.style.letterSpacing = ''
      delete inner.dataset.fit

      const containerWidth = wrapper.clientWidth
      if (containerWidth <= 0) return  // 未レイアウト
      // The glyph box itself needs two pixels at each side: one for the crisp
      // black text-shadow and one clear pixel before the illustrated rule.
      // DOM width APIs do not include text-shadow's painted area.
      // CSS computed width keeps sub-pixel precision and, unlike a visual
      // bounding rect, is not multiplied by transforms on ancestor cards.
      const paintSafeInset = 2
      const safeWidth = Math.max(1, containerWidth - paintSafeInset * 2)
      const measure = (): number => {
        void inner.offsetHeight
        const computedWidth = Number.parseFloat(getComputedStyle(inner).width)
        return Number.isFinite(computedWidth) ? computedWidth : inner.scrollWidth
      }

      const initialNatural = measure()
      if (initialNatural <= safeWidth) return  // 黒フチを含めて収まる

      // Preserve the normal short-line rhythm. Only an overflowing line gets
      // progressively tighter tracking, and the glyph height never changes.
      const lsSteps = [-0.2, -0.4, -0.6]
      for (const ls of lsSteps) {
        inner.style.letterSpacing = `${ls}px`
        if (measure() <= safeWidth) {
          inner.dataset.fit = 'tracking'
          return
        }
      }

      // Keep the paint-safe left inset and scale the final tracked width into
      // the remaining safe area. transform-origin is the inner span's left edge.
      const naturalWidth = measure()
      const ratio = Math.min(1, safeWidth / naturalWidth)
      wrapper.classList.add('card-desc-line--scaled')
      inner.style.left = `${paintSafeInset}px`
      inner.style.transform = `scaleX(${ratio})`
      inner.dataset.fit = 'scaled'
    }

    recompute()

    // wrapper サイズ変動 (initial layout 確定後 / 親リサイズ等) でも再計算。
    //  inner は観察しない: recompute が字間を変えると inner サイズも
    //  変わるため observe(inner) は循環 loop の原因になる。
    //  catalog 後ロードで marker → JP 名置換され文字数が変わるケースは
    //  effect の dep (line / cardNames / unitNames) の変化で検出する。
    if (typeof ResizeObserver !== 'undefined') {
      const ro = new ResizeObserver(() => recompute())
      ro.observe(wrapper)
      return () => ro.disconnect()
    }
    return undefined
    // 表示文、catalog 解決名、無効化表示のいずれが変わっても再計算する。
  }, [fitDependency])

  return (
    <span
      ref={wrapperRef}
      className={`card-desc-line${isUsageLimitReachedLine(line) ? ' card-desc-line--usage-limit-reached' : ''}`}
    >
      <span ref={innerRef} className="card-desc-line-inner">
        {renderLine(line, cardNames, unitNames, disabledKeywords, renderTerm)}
      </span>
    </span>
  )
}

function isUsageLimitReachedLine(line: string): boolean {
  const match = line.match(/^ターン中\d+度だけ使用可能\((\d+)\/(\d+)\)。$/)
  return match !== null && match[1] === match[2]
}

function renderLine(
  line: string,
  cardNames: Record<string, string>,
  unitNames: Record<string, string>,
  disabledKeywords: readonly string[],
  renderTerm?: (term: CardDescTerm, rendered: ReactNode, key: number) => ReactNode,
  revealStatRefs = false,
): ReactNode[] {
  const parts: ReactNode[] = []
  let lastIndex = 0
  let key = 0
  // matchAll で全 marker を順次取り出し、間のテキストと交互に push する。
  for (const m of line.matchAll(MARKER_RE)) {
    const idx = m.index ?? 0
    if (idx > lastIndex) parts.push(line.slice(lastIndex, idx))
    const [, kind, value, extra] = m
    parts.push(renderMarker(kind, value, extra, cardNames, unitNames, disabledKeywords, key++, renderTerm, revealStatRefs))
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
  disabledKeywords: readonly string[],
  key: number,
  renderTerm?: (term: CardDescTerm, rendered: ReactNode, key: number) => ReactNode,
  revealStatRefs = false,
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
    case 'P': {
      const label = value === 'calm' ? '静' : value === 'motion' ? '動' : value
      return (
        <span key={key} className={`card-desc-phase card-desc-phase--${value}`}>
          {label}
        </span>
      )
    }
    case 'K': {
      const definition = KEYWORD_DEFS[value]
      const jp = definition?.name ?? value
      const disabled = disabledKeywords.includes(value)
      const { enhanced, count } = parseKeywordExtra(extra)
      const rendered = (
        <span
          key={key}
          className={[
            'card-desc-keyword',
            disabled ? 'card-desc-keyword--disabled' : '',
            enhanced ? 'card-desc-keyword--enhanced' : '',
          ].filter(Boolean).join(' ')}
          data-keyword={value}
        >
          <span className="card-desc-keyword__label">{jp}{count !== null ? <span className="card-desc-num">{count}</span> : null}</span>
          {disabled ? <span className="card-desc-keyword__disabled"> (無効)</span> : null}
        </span>
      )
      return definition && renderTerm
        ? renderTerm({ kind: 'keyword', id: value, name: definition.name, desc: keywordDesc(definition.desc, count) }, rendered, key)
        : rendered
    }
    case 'S': {
      const definition = STATUS_DEFS[value]
      const jp = definition?.name ?? value
      const rendered = (
        <span key={key} className="card-desc-status" data-status={value}>
          {jp}
        </span>
      )
      return definition && renderTerm
        ? renderTerm({ kind: 'status', id: value, name: definition.name, desc: definition.desc }, rendered, key)
        : rendered
    }
    case 'T': {
      const jp = TRIGGER_JP[value] ?? value
      return (
        <span key={key} className="card-desc-trigger">
          {value === 'OnHit' && (
            <img
              className="card-desc-trigger__icon"
              src={statusIcon('on_hit')}
              alt=""
              draggable={false}
            />
          )}
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
    case 'R': {
      // 能力値参照。カード面（圧縮表示）は具体値、ポップアップ等の展開表示では
      // 「X(Xは自身の攻撃力)」として参照元を示し、固定値カードと見分けられるようにする。
      const raw = extra ?? ''
      const separator = raw.indexOf(':')
      const modifier = separator >= 0 ? raw.slice(0, separator) : ''
      const label = separator >= 0 ? raw.slice(separator + 1) : raw
      if (revealStatRefs) {
        // 展開表示（ポップアップ）では代入後の値と参照元を並べる: 「5(自身の攻撃力)」。
        return (
          <span key={key} className="card-desc-var" data-stat-ref={label}>
            <span className="card-desc-num">{value}</span>({label})
          </span>
        )
      }
      const cls = ['card-desc-num']
      if (modifier === 'up') cls.push('card-desc-num--up')
      else if (modifier === 'down') cls.push('card-desc-num--down')
      return (
        <span key={key} className={cls.join(' ')} data-stat-ref={label}>
          {value}
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
