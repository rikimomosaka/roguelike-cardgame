export type CharacterSkillCutinKind = 'tactical' | 'ultimate'

const TACTICAL_CUTIN_MS = 1900
const ULTIMATE_CUTIN_MS = 2400
const TACTICAL_CUTIN_MIN_MS = 1400
const ULTIMATE_CUTIN_MIN_MS = 1600

export function characterSkillCutinDurationMs(
  kind: CharacterSkillCutinKind,
  playbackSpeed = 1,
  motionEnabled = true,
): number {
  const baseDuration = kind === 'ultimate' ? ULTIMATE_CUTIN_MS : TACTICAL_CUTIN_MS
  const minimumDuration = kind === 'ultimate'
    ? ULTIMATE_CUTIN_MIN_MS
    : TACTICAL_CUTIN_MIN_MS

  if (!motionEnabled) return minimumDuration
  return Math.max(
    minimumDuration,
    Math.round(baseDuration / Math.max(0.25, playbackSpeed)),
  )
}
