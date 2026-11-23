import type {
  AudioCue,
  BgmDuckEnvelopeOptions,
  LayeredAudioCue,
} from './audioEngine'
import {
  selectSe,
  type SeCueName,
} from './audioManifest'

export type BattleSoundRecipeName =
  | 'attackHit'
  | 'attackBlocked'
  | 'attackPartial'
  | 'blockGain'
  | 'buffApplied'
  | 'debuffApplied'
  | 'poisonApplied'
  | 'healApplied'
  | 'finisher'
  | 'contingency'
  | 'reenact'

export type CharacterSoundClass =
  | 'Striker'
  | 'Guardian'
  | 'Tactician'
  | 'Enhancer'

export type CharacterSkillSoundKind = 'tactical' | 'ultimate'

export type SoundRecipeOptions = {
  pan?: number
}

type RecipeLayer = {
  cue: SeCueName
  delayMs?: number
  gain?: number
}

const RECIPES: Record<BattleSoundRecipeName, readonly RecipeLayer[]> = {
  // Attack is the anticipation/swing layer; hurt or block provides contact.
  attackHit: [
    { cue: 'attack', gain: 0.9 },
    { cue: 'hurt', delayMs: 42 },
  ],
  attackBlocked: [
    { cue: 'attack', gain: 0.84 },
    { cue: 'block', delayMs: 38, gain: 1.08 },
  ],
  attackPartial: [
    { cue: 'attack', gain: 0.82 },
    { cue: 'block', delayMs: 38, gain: 0.92 },
    { cue: 'hurt', delayMs: 55, gain: 0.86 },
  ],
  blockGain: [
    { cue: 'gainBlock' },
    { cue: 'block', delayMs: 35, gain: 0.42 },
  ],
  buffApplied: [
    { cue: 'buff' },
    { cue: 'energy', delayMs: 38, gain: 0.36 },
  ],
  debuffApplied: [
    { cue: 'debuff' },
    { cue: 'hurt', delayMs: 45, gain: 0.24 },
  ],
  poisonApplied: [
    { cue: 'debuff', gain: 0.78 },
    { cue: 'poison', delayMs: 34 },
  ],
  healApplied: [
    { cue: 'heal' },
    { cue: 'buff', delayMs: 48, gain: 0.35 },
  ],
  finisher: [
    { cue: 'finisher' },
    { cue: 'attack', delayMs: 52, gain: 0.68 },
  ],
  contingency: [
    { cue: 'contingencyTrigger' },
    { cue: 'cardPlay', delayMs: 42, gain: 0.42 },
  ],
  reenact: [
    { cue: 'reenact' },
    { cue: 'cardPlay', delayMs: 52, gain: 0.38 },
  ],
}

const CLASS_ACCENT: Record<CharacterSoundClass, SeCueName> = {
  Striker: 'attack',
  Guardian: 'block',
  Tactician: 'contingencyTrigger',
  Enhancer: 'buff',
}

export const BGM_DUCK_PRESETS = {
  tactical: {
    multiplier: 0.67,
    attackMs: 45,
    holdMs: 110,
    releaseMs: 280,
  },
  ultimate: {
    multiplier: 0.35,
    attackMs: 38,
    holdMs: 300,
    releaseMs: 520,
  },
  finisher: {
    multiplier: 0.48,
    attackMs: 42,
    holdMs: 190,
    releaseMs: 410,
  },
  defeat: {
    multiplier: 0.24,
    attackMs: 90,
    holdMs: 500,
    releaseMs: 700,
  },
} as const satisfies Record<string, BgmDuckEnvelopeOptions>

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}

function scaledCue(cue: AudioCue, gain = 1): AudioCue {
  return {
    ...cue,
    volume: clamp((cue.volume ?? 1) * gain, 0, 1),
  }
}

/**
 * Build a deterministic layered sound from the existing licensed asset set.
 * Callers may create one recipe per logical hit; its own cooldown prevents a
 * duplicated event projection from firing the complete stack twice.
 */
export function selectBattleSoundRecipe(
  name: BattleSoundRecipeName,
  seed: string | number,
  options: SoundRecipeOptions = {},
): LayeredAudioCue {
  const pan = clamp(options.pan ?? 0, -1, 1)
  return {
    id: `battle:${name}`,
    cooldownMs: 16,
    layers: RECIPES[name].map((layer, index) => ({
      cue: scaledCue(
        selectSe(layer.cue, `${seed}:${name}:${index}`, { pan }),
        layer.gain,
      ),
      delayMs: layer.delayMs,
    })),
  }
}

/** A class accent is mixed under the common tactical/ultimate signature. */
export function selectCharacterSkillSound(
  kind: CharacterSkillSoundKind,
  characterClass: CharacterSoundClass,
  seed: string | number,
  options: SoundRecipeOptions = {},
): LayeredAudioCue {
  const pan = clamp(options.pan ?? 0, -1, 1)
  const accent = CLASS_ACCENT[characterClass]
  return {
    id: `character-skill:${kind}:${characterClass}`,
    cooldownMs: kind === 'ultimate' ? 320 : 180,
    layers: [
      {
        cue: selectSe(kind, `${seed}:${kind}`, { pan }),
      },
      {
        cue: scaledCue(
          selectSe(accent, `${seed}:${characterClass}:accent`, { pan }),
          kind === 'ultimate' ? 0.72 : 0.48,
        ),
        delayMs: kind === 'ultimate' ? 72 : 48,
      },
    ],
  }
}

/** Convert a participant's visual slot into a restrained stereo position. */
export function panForBattleSlot(
  side: 'player' | 'enemy',
  index: number,
  slotCount: number,
): number {
  const safeCount = Math.max(1, Math.trunc(slotCount))
  const normalizedIndex = safeCount === 1
    ? 0.5
    : clamp(Math.trunc(index), 0, safeCount - 1) / (safeCount - 1)
  const withinSide = -0.18 + (normalizedIndex * 0.36)
  return clamp((side === 'player' ? -0.46 : 0.46) + withinSide, -0.78, 0.78)
}

/** Useful when a VFX already knows its stage-space x coordinate. */
export function panFromStageX(x: number, stageWidth = 1280): number {
  if (!Number.isFinite(x) || !Number.isFinite(stageWidth) || stageWidth <= 0) return 0
  return clamp(((x / stageWidth) * 2) - 1, -0.82, 0.82)
}
