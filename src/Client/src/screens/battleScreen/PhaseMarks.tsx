/** 位相（静・動）。位相なしは undefined で表す。 */
export type ActorPhaseDemo = 'calm' | 'motion'

const PHASE_LABEL: Readonly<Record<ActorPhaseDemo, string>> = {
  calm: '静',
  motion: '動',
}

/** 足元から立ちのぼり、位相が続くあいだ揺らめき続ける光（静 = 青、動 = 赤）。 */
export function PhaseAura({ phase }: { phase?: ActorPhaseDemo }) {
  if (!phase) return null
  return (
    <span className={`slot-phase-aura slot-phase-aura--${phase}`} aria-hidden="true">
      <span className="slot-phase-aura__flame" />
    </span>
  )
}

/** 名前の近くに出す「静」「動」の札（色だけに頼らない表示）。 */
export function PhaseTag({ phase }: { phase?: ActorPhaseDemo }) {
  if (!phase) return null
  return (
    <span className={`slot-phase-tag slot-phase-tag--${phase}`} title={`位相: ${PHASE_LABEL[phase]}`}>
      {PHASE_LABEL[phase]}
    </span>
  )
}
