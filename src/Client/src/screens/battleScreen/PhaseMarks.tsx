import type { CSSProperties, ReactNode } from 'react'
import { PHASE_DEFS } from '../../components/CardDesc'

/** 位相（静・動）。位相なしは undefined で表す。 */
export type ActorPhaseDemo = 'calm' | 'motion'

/** 位相の名前と効果（CardDesc の PHASE_DEFS を共通で使う）。 */
const PHASE_INFO: Readonly<Record<ActorPhaseDemo, { name: string; effect: string }>> = {
  calm: { name: PHASE_DEFS.calm.name, effect: PHASE_DEFS.calm.desc },
  motion: { name: PHASE_DEFS.motion.name, effect: PHASE_DEFS.motion.desc },
}

/** 能力値の 1 行。分からない値は「—」。 */
const statText = (value: number | undefined) => typeof value === 'number' ? String(value) : '—'

/**
 * キャラにカーソルを合わせたときの説明。
 * 見出しは「キャラ名(操作アカウント)」。本文は現在の能力値（HP・攻撃力/守備力・ドロー/エナジー）、
 * 続けて特性（あれば）と位相（あれば）。
 */
export function slotTooltipContent(char: {
  name: string
  charName?: string
  controllerLabel?: string
  hpCur: number
  hpMax: number
  stats?: {
    attack?: number
    defense?: number
    drawPerTurn?: number
    energy?: number
    energyPerTurn?: number
  }
  traitName?: string
  traitDescription?: string
  phase?: ActorPhaseDemo
}): { name: ReactNode; desc: string } {
  const title = `${char.charName ?? char.name}${char.controllerLabel ? `(${char.controllerLabel})` : ''}`
  const stats = char.stats ?? {}
  const lines = [`HP${char.hpCur}/${char.hpMax}`]
  if (stats.attack !== undefined || stats.defense !== undefined)
    lines.push(`攻撃力${statText(stats.attack)} / 守備力${statText(stats.defense)}`)
  if (stats.drawPerTurn !== undefined || stats.energy !== undefined)
    lines.push(`ドロー${statText(stats.drawPerTurn)} / エナジー${statText(stats.energy)}/${statText(stats.energyPerTurn)}`)
  if (char.traitDescription) {
    // 戦技：技名・奥義：技名 と同じく「特性：名前」で出す。
    lines.push('', char.traitName ? `特性：${char.traitName}` : '特性', char.traitDescription)
  }
  const phase = char.phase ? PHASE_INFO[char.phase] : null
  if (phase) lines.push('', `位相：[P:${char.phase}]`, phase.effect)
  return { name: title, desc: lines.join('\n') }
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
 * 攪乱の混乱の輪（頭上で回る星と輪）の位置。頭頂より少し上に、体の幅より狭く置く。
 */
export function disruptionHaloStyle(
  stage: { bodyWidth: number; displayHeight: number; headTop: number },
): CSSProperties {
  const width = Math.round(Math.max(40, Math.min(84, stage.bodyWidth * 0.62)))
  const height = Math.max(10, Math.round(width * 0.3))
  const headY = STAGE_CANVAS_BOTTOM_Y - stage.displayHeight + stage.headTop
  return {
    width: `${width}px`,
    height: `${height}px`,
    top: `${Math.round(headY - height * 0.9)}px`,
    '--halo-rx': `${Math.round(width / 2)}px`,
    '--halo-ry': `${Math.round(height / 2)}px`,
  } as CSSProperties
}

/** 攪乱を持つ間、頭上で回る混乱の輪（輪の上を星が 3 つ回る）。 */
export function DisruptionHalo({ stacks, style }: { stacks?: number; style?: CSSProperties }) {
  if (!stacks || stacks <= 0) return null
  return (
    <span className="slot-disruption-halo" style={style} aria-hidden="true">
      <i><b /></i><i><b /></i><i><b /></i>
    </span>
  )
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
