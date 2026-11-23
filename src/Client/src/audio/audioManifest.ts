export type BgmAsset = {
  id: string
  src: string
  loop: boolean
  volume: number
}

export type SeAsset = {
  id: string
  src: string
  volume: number
  cooldownMs?: number
  maxVoices?: number
  playbackRate?: number
  pan?: number
  detuneCents?: number
  voiceGroup?: string
  maxGroupVoices?: number
}

export type SeSelectionOptions = {
  /** Position the sound at the acting character without requiring unique files. */
  pan?: number
  /** Disable micro-variation for reference recordings and exact A/B checks. */
  vary?: boolean
}

const bgm = (id: string, volume: number, loop = true): BgmAsset => ({
  id,
  src: `/audio/bgm/${id}.ogg`,
  loop,
  volume,
})

const se = (
  id: string,
  volume: number,
  options: Pick<SeAsset, 'cooldownMs' | 'maxVoices'> = {},
): SeAsset => ({
  id,
  src: `/audio/se/${id}.ogg`,
  volume,
  ...options,
})

export const BGM_TRACKS = {
  menu: bgm('menu', 0.44),
  map: bgm('map', 0.4),
  shop: bgm('shop', 0.4),
  battleNormal: bgm('battle-normal', 0.46),
  battleElite: bgm('battle-elite', 0.48),
  battleBoss: bgm('battle-boss', 0.5),
  victory: bgm('victory', 0.54, false),
  gameOver: bgm('game-over', 0.48, false),
} as const

export const SE_CUES = {
  uiClick: [se('ui-click', 0.42, { cooldownMs: 35, maxVoices: 3 })],
  uiConfirm: [se('ui-confirm', 0.58, { cooldownMs: 80, maxVoices: 2 })],
  uiError: [se('ui-error', 0.58, { cooldownMs: 120, maxVoices: 2 })],
  purchase: [se('purchase', 0.62, { cooldownMs: 100, maxVoices: 2 })],
  reward: [se('reward', 0.68, { cooldownMs: 140, maxVoices: 2 })],
  battleStart: [se('battle-start', 0.66, { cooldownMs: 300, maxVoices: 1 })],
  turnStart: [se('turn-start', 0.64, { cooldownMs: 250, maxVoices: 1 })],
  turnEnd: [se('turn-end', 0.5, { cooldownMs: 200, maxVoices: 1 })],
  cardPlay: [se('card-play', 0.48, { cooldownMs: 18, maxVoices: 5 })],
  cardDraw: [
    se('card-draw-1', 0.42, { cooldownMs: 28, maxVoices: 5 }),
    se('card-draw-2', 0.42, { cooldownMs: 28, maxVoices: 5 }),
    se('card-draw-3', 0.42, { cooldownMs: 28, maxVoices: 5 }),
  ],
  cardDiscard: [se('card-discard', 0.43, { cooldownMs: 35, maxVoices: 4 })],
  cardExhaust: [se('card-exhaust', 0.57, { cooldownMs: 55, maxVoices: 4 })],
  cardAdd: [se('card-add', 0.46, { cooldownMs: 40, maxVoices: 3 })],
  cardRecover: [se('card-recover', 0.5, { cooldownMs: 45, maxVoices: 3 })],
  cardShuffle: [se('card-shuffle', 0.55, { cooldownMs: 250, maxVoices: 1 })],
  cardUpgrade: [se('card-upgrade', 0.65, { cooldownMs: 120, maxVoices: 2 })],
  attack: [se('attack', 0.53, { cooldownMs: 24, maxVoices: 5 })],
  hurt: [
    se('hurt-1', 0.58, { cooldownMs: 24, maxVoices: 5 }),
    se('hurt-2', 0.58, { cooldownMs: 24, maxVoices: 5 }),
    se('hurt-3', 0.58, { cooldownMs: 24, maxVoices: 5 }),
  ],
  block: [
    se('block-1', 0.58, { cooldownMs: 24, maxVoices: 5 }),
    se('block-2', 0.58, { cooldownMs: 24, maxVoices: 5 }),
    se('block-3', 0.58, { cooldownMs: 24, maxVoices: 5 }),
  ],
  gainBlock: [se('gain-block', 0.48, { cooldownMs: 36, maxVoices: 4 })],
  buff: [se('buff', 0.48, { cooldownMs: 45, maxVoices: 4 })],
  debuff: [se('debuff', 0.5, { cooldownMs: 45, maxVoices: 4 })],
  heal: [se('heal', 0.55, { cooldownMs: 70, maxVoices: 3 })],
  poison: [se('poison', 0.5, { cooldownMs: 80, maxVoices: 3 })],
  energy: [se('energy', 0.45, { cooldownMs: 38, maxVoices: 4 })],
  death: [se('death', 0.62, { cooldownMs: 80, maxVoices: 4 })],
  summon: [se('summon', 0.58, { cooldownMs: 100, maxVoices: 3 })],
  potion: [se('potion', 0.55, { cooldownMs: 100, maxVoices: 2 })],
  tactical: [se('tactical', 0.7, { cooldownMs: 220, maxVoices: 1 })],
  ultimate: [se('ultimate', 0.78, { cooldownMs: 350, maxVoices: 1 })],
  contingencyArm: [se('contingency-arm', 0.55, { cooldownMs: 100, maxVoices: 2 })],
  contingencyTrigger: [se('contingency-trigger', 0.7, { cooldownMs: 120, maxVoices: 2 })],
  reenact: [se('reenact', 0.64, { cooldownMs: 85, maxVoices: 3 })],
  finisher: [se('finisher', 0.76, { cooldownMs: 180, maxVoices: 2 })],
  trait: [se('trait', 0.58, { cooldownMs: 80, maxVoices: 3 })],
  ready: [se('ready', 0.45, { cooldownMs: 100, maxVoices: 2 })],
  resolution: [se('resolution', 0.62, { cooldownMs: 180, maxVoices: 1 })],
  victory: [{
    id: 'victory-stinger',
    src: '/audio/bgm/victory.ogg',
    volume: 0.72,
    cooldownMs: 1200,
    maxVoices: 1,
  }],
  gameOver: [{
    id: 'game-over-stinger',
    src: '/audio/bgm/game-over.ogg',
    volume: 0.68,
    cooldownMs: 1200,
    maxVoices: 1,
  }],
} as const

export type SeCueName = keyof typeof SE_CUES

function stableHash(seed: string | number): number {
  const text = String(seed)
  let hash = 2166136261
  for (let index = 0; index < text.length; index++) {
    hash ^= text.charCodeAt(index)
    hash = Math.imul(hash, 16777619)
  }
  return hash >>> 0
}

function stableSigned(seed: string | number, salt: string): number {
  return ((stableHash(`${seed}:${salt}`) / 0xffff_ffff) * 2) - 1
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}

const LOW_VARIATION_CUES = new Set<SeCueName>([
  'battleStart',
  'turnStart',
  'turnEnd',
  'tactical',
  'ultimate',
  'finisher',
  'victory',
  'gameOver',
])

export function selectSe(
  cue: SeCueName,
  seed: string | number = 0,
  options: SeSelectionOptions = {},
): SeAsset {
  const variants = SE_CUES[cue] as readonly SeAsset[]
  const selected = variants[stableHash(`${seed}:variant`) % variants.length]
  const pan = clamp(options.pan ?? 0, -1, 1)
  if (options.vary === false) return { ...selected, pan }

  // Small deterministic changes prevent rapid multi-hit sounds from phasing or
  // feeling machine-gunned while keeping captures and tests reproducible.
  const amount = LOW_VARIATION_CUES.has(cue) ? 0.35 : 1
  const gainMultiplier = 1 + (stableSigned(seed, `${cue}:gain`) * 0.035 * amount)
  const detuneCents = stableSigned(seed, `${cue}:pitch`) * 34 * amount
  return {
    ...selected,
    volume: clamp(selected.volume * gainMultiplier, 0, 1),
    detuneCents,
    pan,
    voiceGroup: selected.voiceGroup ?? cue,
    maxGroupVoices: selected.maxGroupVoices ?? selected.maxVoices,
  }
}
