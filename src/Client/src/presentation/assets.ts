/**
 * 画像素材のパス（表示用）。規格: docs/current-art-asset-spec.md
 *
 * コードから画像パスを直接書かず、必ずここの関数を通す。
 * 表示用ファイルは tools/art-pipeline/build-assets.mjs が art/source/ から生成する。
 */
import actorAssets from './generated/actor-assets.json'

const ROOT = '/assets'
const SAFE_ID = /^[a-z0-9][a-z0-9_-]*$/i

/** camelCase の ID をファイル名（snake_case）にする。例: handCostReduction → hand_cost_reduction */
export function toAssetName(id: string): string {
  return id.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase()
}

function safe(id: string | null | undefined, fallback: string): string {
  const trimmed = id?.trim()
  return trimmed && SAFE_ID.test(trimmed) ? trimmed : fallback
}

// ---- 背景 ----
export type BackgroundKind = 'battle' | 'map' | 'events'
export const background = (kind: BackgroundKind, id: string) => `${ROOT}/backgrounds/${kind}/${id}.png`

// ---- アイコン ----
/** 状態（バフ・デバフ）アイコン。16×16。ID は camelCase のままでよい。 */
export const statusIcon = (statusId: string) => `${ROOT}/icons/status/${toAssetName(statusId)}.png`
/** 敵の行動予測アイコン。16×16。 */
export type IntentIconId = 'attack' | 'attack_all' | 'attack_random' | 'block' | 'buff' | 'debuff' | 'heal'
export const intentIcon = (id: IntentIconId) => `${ROOT}/icons/intent/${id}.png`
/** 設定・地図・山札などのシステムアイコン。16×16。 */
export type SystemIconId =
  | 'abandon' | 'bestiary' | 'close' | 'continue' | 'deck' | 'discard' | 'gold' | 'loading' | 'lock'
  | 'map' | 'merchant' | 'pile_discard' | 'pile_draw' | 'pile_exhaust' | 'rest' | 'reward' | 'reward_gold'
  | 'settings' | 'time' | 'unavailable' | 'upgrade'
export const systemIcon = (id: SystemIconId) => `${ROOT}/icons/system/${id}.png`
export const relicIcon = (relicId: string) => `${ROOT}/icons/relics/${safe(relicId, 'unknown')}.png`
export const potionIcon = (potionId: string) => `${ROOT}/icons/potions/${safe(potionId, 'unknown')}.png`
export type MapIconId = 'boss' | 'elite' | 'enemy' | 'event' | 'merchant' | 'rest' | 'start' | 'treasure' | 'unknown'
export const mapIcon = (id: MapIconId) => `${ROOT}/icons/map/${id}.png`
/** 休憩画面などの場面の絵。48×48（×4 表示）。 */
export type SceneArtId = 'rest_fire' | 'rest_flame' | 'rest_heal' | 'rest_logs'
export const sceneArt = (id: SceneArtId) => `${ROOT}/icons/scene/${id}.png`

// ---- カード ----
export type CardFramePart = 'affiliations' | 'types' | 'costs' | 'masks'
export const cardFrame = (part: CardFramePart, id: string) => `${ROOT}/cards/frames/${part}/${id}.png`

// ---- キャラクター ----
export const DEFAULT_HERO_ID = 'default'
/** バストアップの立ち絵（キャラ選択・カットインなど）。 */
export const portrait = (characterId: string | null | undefined) =>
  `${ROOT}/portraits/${safe(characterId, DEFAULT_HERO_ID)}.png`

export type ActorKind = 'heroes' | 'units' | 'enemies'
/** 戦闘キャラのモーション（128×128 のコマを横に並べたシート）。 */
export const actorSheet = (kind: ActorKind, id: string, motion = 'stand') =>
  `${ROOT}/actors/${kind}/${safe(id, 'unknown')}/${motion}.png`

/** 戦闘キャラ共通キャンバス（docs/current-art-asset-spec.md §3）。 */
export const ACTOR_CANVAS = { size: 128, footRow: 123, scale: 2 } as const

export type ActorMetrics = {
  /** 見た目の中心の高さ（キャンバス上端からの割合）。 */
  centerY: number
  /** 横幅（ドット）。影の幅に使う。 */
  footprint: number
  /** 一番上のドットの行。 */
  top: number
  /** 一番下のドットの行（地面の線 123 より上なら空中にいる）。 */
  bottom: number
}
const ACTOR_METRICS = actorAssets.actors as Record<string, ActorMetrics>
export const actorMetrics = (kind: ActorKind, id: string): ActorMetrics | undefined => ACTOR_METRICS[`${kind}/${id}`]

export type HeroMotionInfo = { frames: number; durationMs: number; loop: boolean }
const HERO_MOTIONS = actorAssets.heroes as Record<string, Record<string, HeroMotionInfo>>
/** 主人公ごとに用意されているモーション（ビルド時に生成）。 */
export const heroMotions = (heroId: string): Record<string, HeroMotionInfo> | undefined => HERO_MOTIONS[heroId]

// ---- UI ----
export type HeroUiPart = 'combo' | 'tactical' | 'ultimate' | 'ready'
/** 主人公ごとの戦闘操作UI。専用の絵が無い主人公は共通の枠（default_frame）。 */
export const HEROES_WITH_OWN_UI: ReadonlySet<string> = new Set<string>(['default'])
export const heroUi = (heroId: string | null | undefined, part: HeroUiPart) => {
  const id = heroId && HEROES_WITH_OWN_UI.has(heroId) ? heroId : 'default_frame'
  return `${ROOT}/ui/heroes/${id}/${part}.png`
}
export type EnemyUiPart = 'intent_plate' | 'action_plate' | 'action_button'
export const enemyUi = (part: EnemyUiPart) => `${ROOT}/ui/enemy/${part}.png`
