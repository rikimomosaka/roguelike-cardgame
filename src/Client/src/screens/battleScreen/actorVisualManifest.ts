import type { CharacterMotion, SpriteSheetDemo } from '../BattleScreen'
import { resolveCharacterPortrait } from '../../presentation'

export type ActorVisual = {
  image: string
  portrait?: string
  animations?: Partial<Record<CharacterMotion, SpriteSheetDemo>>
}

export type ActorSizeClass = 'small' | 'medium' | 'large'

export type ActorStageProfile = {
  airborne: boolean
  /** Fraction of the rendered actor height lifted above the grounded anchor. */
  liftRatio: number
}

const HERO_IDS = [
  'default',
  'guardian_mireille',
  'tactician_yuno',
  'enhancer_lyra',
] as const

type HeroId = typeof HERO_IDS[number]

function isHeroId(value: string | undefined): value is HeroId {
  return !!value && (HERO_IDS as readonly string[]).includes(value)
}

type SheetTiming = Pick<SpriteSheetDemo, 'frameCount' | 'durationMs'>

/**
 * 新規格（docs/proposed-graphics-asset-pipeline.md）で作り直したキャラのフレーム数と時間。
 * ここにないキャラは旧素材の値を使う。
 */
const HERO_SHEET_TIMING: Partial<Record<HeroId, Partial<Record<CharacterMotion, SheetTiming>>>> = {
  default: {
    idle: { frameCount: 8, durationMs: 1200 },
    attack: { frameCount: 6, durationMs: 600 },
    hurt: { frameCount: 4, durationMs: 400 },
  },
}

/**
 * 新規格のキャラだけが持つ追加モーション（攻撃B・奥義・ダウン）。
 * 旧素材のキャラはファイルが無いので登録しない（404 を出さないため）。
 */
const HERO_EXTRA_MOTIONS: Partial<Record<HeroId, true>> = {
  default: true,
}

function extraMotions(root: string): Partial<Record<CharacterMotion, SpriteSheetDemo>> {
  return {
    attackAlt: {
      src: `${root}_attack_alt.png`,
      frameWidth: 384,
      frameHeight: 256,
      frameCount: 6,
      durationMs: 600,
      loop: false,
    },
    skill: {
      src: `${root}_skill.png`,
      // 剣を振り上げるため、上に 32px の余白を足した枠（足元の位置は他と同じ）。
      frameWidth: 384,
      frameHeight: 288,
      baseFrameHeight: 256,
      frameCount: 8,
      durationMs: 800,
      loop: false,
    },
    down: {
      src: `${root}_down.png`,
      frameWidth: 384,
      frameHeight: 256,
      frameCount: 6,
      durationMs: 720,
      // 最後のコマ（倒れた姿勢）で止める。
      loop: false,
    },
  }
}

function heroVisual(id: HeroId): ActorVisual {
  const root = `/characters/${id}`
  const timing = HERO_SHEET_TIMING[id] ?? {}
  return {
    image: `${root}_stand.png`,
    portrait: resolveCharacterPortrait(id),
    animations: {
      idle: {
        src: `${root}_idle.png`,
        frameWidth: 192,
        frameHeight: 256,
        frameCount: 8,
        durationMs: 1600,
        loop: true,
        ...timing.idle,
      },
      attack: {
        src: `${root}_attack.png`,
        // Wide strike poses keep the weapon inside the authored cell.
        frameWidth: 384,
        frameHeight: 256,
        frameCount: 4,
        durationMs: 480,
        loop: false,
        ...timing.attack,
      },
      hurt: {
        src: `${root}_hurt.png`,
        frameWidth: 192,
        frameHeight: 256,
        frameCount: 4,
        durationMs: 440,
        loop: false,
        ...timing.hurt,
      },
      ...(HERO_EXTRA_MOTIONS[id] ? extraMotions(root) : {}),
    },
  }
}

const AIRBORNE_STAGE_BY_IMAGE_ID: Readonly<Record<string, number>> = {
  cave_bat_a: 0.42,
  cave_bat_b: 0.42,
  wisp: 0.38,
  six_ghost: 0.18,
}

/**
 * Vertical centre of each actor's non-transparent silhouette in its authored
 * 192x256 canvas.  The canvases intentionally keep different head/ground
 * gutters, so using the image box's geometric centre makes impact particles
 * miss short and airborne actors (most visibly the cave bats).
 */
const IMPACT_CENTER_RATIO_BY_IMAGE_ID: Readonly<Record<string, number>> = {
  default_stand: 0.516,
  enhancer_lyra_stand: 0.533,
  guardian_mireille_stand: 0.533,
  tactician_yuno_stand: 0.533,
  act2_boss: 0.566,
  act2_brute: 0.615,
  act2_elite: 0.666,
  act2_grunt: 0.654,
  act3_boss: 0.602,
  act3_brute: 0.664,
  act3_elite: 0.654,
  act3_grunt: 0.664,
  bandit: 0.613,
  cave_bat_a: 0.773,
  cave_bat_b: 0.777,
  cultist_dark: 0.570,
  dragon_sleep: 0.646,
  goblin_a: 0.621,
  goblin_b: 0.652,
  goblin_c: 0.592,
  guardian_golem: 0.576,
  hobgoblin: 0.656,
  iron_golem_a: 0.566,
  iron_golem_b: 0.549,
  iron_golem_c: 0.596,
  jaw_worm: 0.668,
  louse_red: 0.748,
  mushroom_a: 0.557,
  mushroom_b: 0.553,
  ogre: 0.676,
  orc_blue: 0.660,
  orc_red: 0.646,
  six_ghost: 0.592,
  slime_big: 0.734,
  slime_green_s: 0.801,
  slime_king: 0.652,
  slime_spike_s: 0.709,
  wolf_dire: 0.738,
  wisp: 0.541,
}

function imageIdFromPath(value: string | undefined): string | undefined {
  if (!value) return undefined
  const filename = value.split(/[\\/]/).pop()
  return filename?.replace(/\.png(?:[?#].*)?$/i, '')
}

/**
 * Purely visual staging for actors whose art represents flight. Their shadow
 * stays on the common ground line while only the sprite is lifted.
 */
export function resolveActorStageProfile(image: string | undefined): ActorStageProfile {
  const imageId = imageIdFromPath(image)
  const liftRatio = imageId ? AIRBORNE_STAGE_BY_IMAGE_ID[imageId] ?? 0 : 0
  return { airborne: liftRatio > 0, liftRatio }
}

/** Returns the visual (alpha-bounds) centre, expressed from image top to bottom. */
export function resolveActorImpactCenterRatio(image: string | undefined): number {
  const imageId = imageIdFromPath(image)
  return imageId ? IMPACT_CENTER_RATIO_BY_IMAGE_ID[imageId] ?? 0.6 : 0.6
}

/**
 * Pixel y-coordinate of the visible actor centre inside the 260px battle slot.
 * The sprite's authored canvas is bottom-anchored at y=196 before flight lift.
 */
export function resolveActorImpactAnchorY(
  image: string | undefined,
  displayHeight: number,
  groundedCanvasBottom = 196,
): number {
  const stage = resolveActorStageProfile(image)
  const lift = Math.round(displayHeight * stage.liftRatio)
  return Math.round(
    groundedCanvasBottom
      - lift
      - displayHeight * (1 - resolveActorImpactCenterRatio(image)),
  )
}

export function actorSizeClassForTier(tier: number | undefined): ActorSizeClass {
  const normalized = Math.max(1, Math.min(10, Math.round(tier ?? 5)))
  if (normalized <= 3) return 'small'
  if (normalized <= 6) return 'medium'
  return 'large'
}

/**
 * Presentation-only actor art resolver.  The battle definition remains the
 * authoritative source of identity; this table merely maps that identity to
 * client assets and therefore cannot affect combat rules.
 */
export function resolveActorVisual({
  isHero,
  characterId,
  imageId,
  side,
}: {
  isHero: boolean
  characterId?: string
  imageId?: string
  side: 'Ally' | 'Enemy'
}): ActorVisual | undefined {
  if (isHero) {
    return heroVisual(isHeroId(characterId) ? characterId : 'default')
  }

  if (!imageId) return undefined
  return {
    image: side === 'Ally'
      ? `/characters/units/${imageId}.png`
      : `/characters/enemies/${imageId}.png`,
  }
}

export const PLAYABLE_ACTOR_VISUAL_IDS = HERO_IDS
