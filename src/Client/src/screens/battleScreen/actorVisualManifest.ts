import type { CharacterMotion, SpriteSheetDemo } from '../BattleScreen'
import {
  ACTOR_CANVAS,
  actorMetrics,
  actorSheet,
  heroMotions,
  portrait,
  type ActorKind,
} from '../../presentation/assets'

/**
 * 戦闘キャラの表示（128×128 共通キャンバス、×2 表示）。規格: docs/current-art-asset-spec.md §3。
 * 大きさ・空中の位置は絵の中に描かれているので、表示側では拡大縮小・持ち上げをしない。
 */
export type ActorStage = {
  /** キャンバスの表示高さ（px）。全キャラ共通。 */
  displayHeight: number
  /** 影の幅（px）。キャラの横幅から決める。 */
  shadowWidth: number
  /** 見た目の中心の高さ（キャンバス上端からの割合）。効果の表示位置に使う。 */
  centerY: number
  /** 空中にいるキャラ（絵の中で浮かせてある）。 */
  airborne: boolean
}

export type ActorVisual = {
  image: string
  portrait?: string
  animations?: Partial<Record<CharacterMotion, SpriteSheetDemo>>
  stage?: ActorStage
}

export type ActorSizeClass = 'small' | 'medium' | 'large'

export type ActorStageProfile = {
  airborne: boolean
  /** Fraction of the rendered actor height lifted above the grounded anchor. 新規格では常に 0（絵の中で浮かせる）。 */
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

const DISPLAY = ACTOR_CANVAS.size * ACTOR_CANVAS.scale
/** 影はキャラの横幅より少し狭くする（足元に収まるように）。 */
const SHADOW_RATIO = 0.8

/** ビルドが記録したキャラの寸法から、表示用の値を作る。 */
function stageFor(kind: ActorKind, id: string): ActorStage | undefined {
  const m = actorMetrics(kind, id)
  if (!m) return undefined
  return {
    displayHeight: DISPLAY,
    shadowWidth: Math.max(24, Math.round(m.footprint * ACTOR_CANVAS.scale * SHADOW_RATIO)),
    centerY: m.centerY,
    // 地面の線より上に浮いている（足元の下に 4 ドット以上の隙間がある）キャラ。
    airborne: ACTOR_CANVAS.footRow - m.bottom >= 4,
  }
}

/** ビルド時のモーション名（ファイル名）→ 表示側のモーション名。 */
const MOTION_FILES: Readonly<Record<string, CharacterMotion>> = {
  idle: 'idle',
  attack: 'attack',
  attack_alt: 'attackAlt',
  skill: 'skill',
  guard: 'guard',
  hurt: 'hurt',
  down: 'down',
}

function heroVisual(id: HeroId): ActorVisual {
  const motions = heroMotions(id) ?? {}
  const animations: Partial<Record<CharacterMotion, SpriteSheetDemo>> = {}
  for (const [file, motion] of Object.entries(MOTION_FILES)) {
    const info = motions[file]
    if (!info) continue
    animations[motion] = {
      src: actorSheet('heroes', id, file),
      frameWidth: ACTOR_CANVAS.size,
      frameHeight: ACTOR_CANVAS.size,
      frameCount: info.frames,
      durationMs: info.durationMs,
      loop: info.loop,
    }
  }
  return {
    image: actorSheet('heroes', id, 'stand'),
    portrait: portrait(id),
    animations,
    stage: stageFor('heroes', id),
  }
}

/** 画像パス（/assets/actors/<区分>/<id>/<motion>.png）からキャラを特定する。 */
function actorFromPath(image: string | undefined): { kind: ActorKind; id: string } | undefined {
  const m = image?.match(/\/actors\/(heroes|units|enemies)\/([a-z0-9_-]+)\//i)
  return m ? { kind: m[1] as ActorKind, id: m[2] } : undefined
}

/** 新規格のキャラは絵の中で浮かせてあるので、表示側の持ち上げは 0。 */
export function resolveActorStageProfile(image: string | undefined): ActorStageProfile {
  const actor = actorFromPath(image)
  const stage = actor ? stageFor(actor.kind, actor.id) : undefined
  return { airborne: stage?.airborne ?? false, liftRatio: 0 }
}

/** Returns the visual (alpha-bounds) centre, expressed from image top to bottom. */
export function resolveActorImpactCenterRatio(image: string | undefined): number {
  const actor = actorFromPath(image)
  return (actor ? actorMetrics(actor.kind, actor.id)?.centerY : undefined) ?? 0.6
}

/**
 * Pixel y-coordinate of the visible actor centre inside the 260px battle slot.
 * The sprite's canvas is bottom-anchored at y=groundedCanvasBottom.
 */
export function resolveActorImpactAnchorY(
  image: string | undefined,
  displayHeight: number,
  groundedCanvasBottom = 196,
): number {
  return Math.round(groundedCanvasBottom - displayHeight * (1 - resolveActorImpactCenterRatio(image)))
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
  const kind: ActorKind = side === 'Ally' ? 'units' : 'enemies'
  return {
    image: actorSheet(kind, imageId),
    stage: stageFor(kind, imageId),
  }
}

export const PLAYABLE_ACTOR_VISUAL_IDS = HERO_IDS
