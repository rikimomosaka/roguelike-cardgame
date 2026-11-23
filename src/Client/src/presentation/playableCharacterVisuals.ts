export const DEFAULT_CHARACTER_PORTRAIT = '/characters/portraits/default.png'

const SAFE_CHARACTER_ID = /^[a-z0-9][a-z0-9_-]*$/i

/**
 * Returns the shared bust-up portrait used outside the battle stage and by cut-ins.
 * Battle stand sprites intentionally live on a separate visual path.
 */
export function resolveCharacterPortrait(characterId: string | null | undefined): string {
  const normalizedId = characterId?.trim()
  if (!normalizedId || !SAFE_CHARACTER_ID.test(normalizedId)) {
    return DEFAULT_CHARACTER_PORTRAIT
  }

  return `/characters/portraits/${normalizedId}.png`
}
