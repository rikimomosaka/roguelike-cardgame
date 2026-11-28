import type { CSSProperties, ReactNode } from 'react'
import { CardDesc, PHASE_DEFS } from '../../components/CardDesc'

/** 位相（静・動）。位相なしは undefined で表す。 */
export type ActorPhaseDemo = 'calm' | 'motion'

/** 位相の名前と効果（CardDesc の PHASE_DEFS を共通で使う）。 */
const PHASE_INFO: Readonly<Record<ActorPhaseDemo, { name: string; effect: string }>> = {
  calm: { name: PHASE_DEFS.calm.name, effect: PHASE_DEFS.calm.desc },
  motion: { name: PHASE_DEFS.motion.name, effect: PHASE_DEFS.motion.desc },
}

/** 能力値の数字（黄色の数字。分からない値は「—」）。 */
const statText = (value: number | undefined) => typeof value === 'number' ? `[N:${value}]` : '—'

/** ツールチップに出す能力値の行（HP は上限、エナジーはターン開始時に回復する数）。 */
export function actorStatLines(char: {
  hpMax: number
  stats?: { attack?: number; defense?: number; drawPerTurn?: number; energyPerTurn?: number }
}): string[] {
  const stats = char.stats ?? {}
  const lines = [`HP ${statText(char.hpMax)}`]
  if (stats.attack !== undefined || stats.defense !== undefined)
    lines.push(`攻撃力 ${statText(stats.attack)} / 守備力 ${statText(stats.defense)}`)
  if (stats.drawPerTurn !== undefined || stats.energyPerTurn !== undefined)
    lines.push(`ドロー ${statText(stats.drawPerTurn)} / エナジー ${statText(stats.energyPerTurn)}`)
  return lines
}

/**
 * キャラにカーソルを合わせたときの説明。
 * 見出しは「キャラ名(操作アカウント)」。見出しの下に能力値（HP・攻撃力/守備力・ドロー/エナジー）と、
 * 特性名（戦技・奥義の名前と同じ見出しの書式）。本文は特性の効果と位相（あれば）。
 */
export function slotTooltipContent(char: {
  name: string
  charName?: string
  controllerLabel?: string
  hpMax: number
  stats?: {
    attack?: number
    defense?: number
    drawPerTurn?: number
    energyPerTurn?: number
  }
  traitName?: string
  traitDescription?: string
  phase?: ActorPhaseDemo
}): { name: ReactNode; statusLine: ReactNode; desc: string } {
  const title = `${char.charName ?? char.name}${char.controllerLabel ? `(${char.controllerLabel})` : ''}`
  const traitTitle = char.traitDescription
    ? (char.traitName ? `特性：${char.traitName}` : '特性')
    : null
  const statusLine = (
    <>
      <span className="tip-actor-stats">
        <CardDesc text={actorStatLines(char).join('\n')} compress={false} />
      </span>
      {traitTitle ? <span className="tip__name tip-actor-trait">{traitTitle}</span> : null}
    </>
  )
  const desc: string[] = []
  if (char.traitDescription) desc.push(char.traitDescription)
  const phase = char.phase ? PHASE_INFO[char.phase] : null
  if (phase) {
    if (desc.length > 0) desc.push('')
    desc.push(`位相：[P:${char.phase}]`, phase.effect)
  }
  return { name: title, statusLine, desc: desc.join('\n') }
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
 * カーソルの当たり枠（説明の表示用）。スプライトの画像全体（大半が透明）ではなく、
 * ドットのある範囲（体の左右幅 × 頭頂から足元）だけにする。隣のキャラの透明な部分で説明が切り替わらないように。
 */
/** 当たり枠の余白と最小の大きさ（px）。 */
const HIT_AREA_PAD = 4
const HIT_AREA_MIN = 56

export function bodyHitAreaStyle(
  stage: { bodyWidth: number; bodyLeft: number; displayHeight: number; headTop: number; footGap: number },
): CSSProperties {
  const canvasTop = STAGE_CANVAS_BOTTOM_Y - stage.displayHeight
  const top = canvasTop + stage.headTop
  const bottom = STAGE_CANVAS_BOTTOM_Y - stage.footGap
  // 体の周りに少し余白。コウモリなど小さいキャラは、中心を保ったまま最小の大きさまで広げる（狙いやすく）。
  const width = Math.max(HIT_AREA_MIN, Math.round(stage.bodyWidth) + HIT_AREA_PAD * 2)
  const height = Math.max(HIT_AREA_MIN, bottom - top + HIT_AREA_PAD * 2)
  const centerX = stage.bodyLeft + stage.bodyWidth / 2
  const centerY = (top + bottom) / 2
  return {
    left: `calc(50% - ${stage.displayHeight / 2}px + ${Math.round(centerX - width / 2)}px)`,
    top: `${Math.round(centerY - height / 2)}px`,
    width: `${width}px`,
    height: `${height}px`,
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
