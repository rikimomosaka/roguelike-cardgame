import type { TileKind } from '../api/types'

export type BgmScene =
  | 'map'
  | 'shop'
  | 'battleNormal'
  | 'battleElite'
  | 'battleBoss'

export type MapAudioState = {
  activeBattle: boolean
  currentNodeKind: TileKind
  resolvedNodeKind: TileKind | null
  merchantVisible: boolean
}

/**
 * MapScreen contains most of the game's nested screens. Resolve them in one
 * place so transient overlays cannot accidentally stop a higher-priority
 * battle track (for example while peeking at the map).
 */
export function resolveMapBgmScene(state: MapAudioState): BgmScene {
  if (state.activeBattle) {
    const battleKind = state.resolvedNodeKind ?? state.currentNodeKind
    if (battleKind === 'Boss') return 'battleBoss'
    if (battleKind === 'Elite') return 'battleElite'
    return 'battleNormal'
  }
  if (state.merchantVisible) return 'shop'
  return 'map'
}
