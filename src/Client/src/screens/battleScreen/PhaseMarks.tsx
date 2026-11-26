import type { CSSProperties, ReactNode } from 'react'
import { PHASE_DEFS } from '../../components/CardDesc'

/** 位相（静・動）。位相なしは undefined で表す。 */
export type ActorPhaseDemo = 'calm' | 'motion'

/** 位相の名前と効果（CardDesc の PHASE_DEFS を共通で使う）。 */
const PHASE_INFO: Readonly<Record<ActorPhaseDemo, { name: string; effect: string }>> = {
  calm: { name: PHASE_DEFS.calm.name, effect: PHASE_DEFS.calm.desc },
  motion: { name: PHASE_DEFS.motion.name, effect: PHASE_DEFS.motion.desc },
}

/**
 * キャラにカーソルを合わせたときの説明。特性があれば特性の後ろに今の位相と効果を足す。
 * 特性が無ければ位相を見出しにする。どちらも無ければ null（説明を出さない）。
 */
export function slotTooltipContent(char: {
  traitName?: string
  traitDescription?: string
  phase?: ActorPhaseDemo
}): { name: ReactNode; desc: string } | null {
  const phase = char.phase ? PHASE_INFO[char.phase] : null
  if (char.traitDescription) {
    const name = char.traitName ?? '特性'
    return phase
      ? { name, desc: `${char.traitDescription}\n\n位相：[P:${char.phase}]\n${phase.effect}` }
      : { name, desc: char.traitDescription }
  }
  return phase && char.phase
    ? {
        name: <>位相：<span className={`card-desc-phase card-desc-phase--${char.phase}`}>{phase.name}</span></>,
        desc: phase.effect,
      }
    : null
}

/** 新規格のキャラのキャンバス下端（スロット上端からの y）。BattleScreen の STAGE_CANVAS_BOTTOM_Y と同じ。 */
const STAGE_CANVAS_BOTTOM_Y = 198
/** 輪の縦横比（横に平たい楕円）。 */
const RING_ASPECT = 0.2

/**
 * 足元の輪の位置と大きさ。幅はドットの左右幅、下端はドットの下端に揃える
 * （輪の 2 割だけ下にはみ出させて足を囲む）。
 */
export function phaseRingStyle(stage: { bodyWidth: number; footGap: number }): CSSProperties {
  const width = Math.round(stage.bodyWidth)
  const height = Math.max(8, Math.round(width * RING_ASPECT))
  const footY = STAGE_CANVAS_BOTTOM_Y - stage.footGap
  return {
    width: `${width}px`,
    height: `${height}px`,
    top: `${Math.round(footY - height * 0.8)}px`,
  }
}

/**
 * 位相のオーラの足元の輪（静 = 青、動 = 赤）。位置と大きさは phaseRingStyle で決める。
 * 体の輪郭に沿って揺らめく光は、スロットの has-phase--* クラスでスプライトに付ける（BattleScreen.css）。
 */
export function PhaseAura({ phase, style }: { phase?: ActorPhaseDemo; style?: CSSProperties }) {
  if (!phase) return null
  return (
    <>
      <span className={`slot-phase-aura slot-phase-aura--${phase}`} style={style} aria-hidden="true" />
      {/* 噴き出す光は体の手前に重ねる（輪は体の後ろ）。 */}
      <span className={`slot-phase-plume slot-phase-plume--${phase}`} style={style} aria-hidden="true" />
    </>
  )
}
