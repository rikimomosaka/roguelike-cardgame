import { DEFAULT_HERO_ID, portrait } from './assets'

export const DEFAULT_CHARACTER_PORTRAIT = portrait(DEFAULT_HERO_ID)

/**
 * Returns the shared bust-up portrait used outside the battle stage and by cut-ins.
 * Battle stand sprites intentionally live on a separate visual path.
 * パスの規則は presentation/assets.ts（不正な ID は既定の立ち絵になる）。
 */
export function resolveCharacterPortrait(characterId: string | null | undefined): string {
  return portrait(characterId)
}
