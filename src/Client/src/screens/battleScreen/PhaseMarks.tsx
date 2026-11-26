/** 位相（静・動）。位相なしは undefined で表す。 */
export type ActorPhaseDemo = 'calm' | 'motion'

/** 位相の名前と効果（キャラにカーソルを合わせたときの説明に出す）。 */
const PHASE_INFO: Readonly<Record<ActorPhaseDemo, { name: string; effect: string }>> = {
  calm: {
    name: '静',
    effect: 'ターン終了時、手札のアタック全てに[K:bipolar]を1付与する。双極を持つカードはターン終了時に捨てられない。アタックを使うと位相がなくなる。',
  },
  motion: {
    name: '動',
    effect: 'ターン終了時の攻撃のダメージが1.5倍になる。[K:bipolar]を持つカードを使うと、その数だけエナジーを得る。アタック以外を使うと位相がなくなる。',
  },
}

/**
 * キャラにカーソルを合わせたときの説明。特性があれば特性の後ろに今の位相と効果を足す。
 * 特性が無ければ位相を見出しにする。どちらも無ければ null（説明を出さない）。
 */
export function slotTooltipContent(char: {
  traitName?: string
  traitDescription?: string
  phase?: ActorPhaseDemo
}): { name: string; desc: string } | null {
  const phase = char.phase ? PHASE_INFO[char.phase] : null
  if (char.traitDescription) {
    const name = char.traitName ?? '特性'
    return phase
      ? { name, desc: `${char.traitDescription}\n\n位相：${phase.name}\n${phase.effect}` }
      : { name, desc: char.traitDescription }
  }
  return phase ? { name: `位相：${phase.name}`, desc: phase.effect } : null
}

/**
 * 位相のオーラの足元の輪（静 = 青、動 = 赤）。
 * 体の輪郭に沿って揺らめく光は、スロットの has-phase--* クラスでスプライトに付ける（BattleScreen.css）。
 */
export function PhaseAura({ phase }: { phase?: ActorPhaseDemo }) {
  if (!phase) return null
  return <span className={`slot-phase-aura slot-phase-aura--${phase}`} aria-hidden="true" />
}
