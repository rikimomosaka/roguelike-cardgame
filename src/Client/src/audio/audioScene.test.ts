import { describe, expect, it } from 'vitest'
import { resolveMapBgmScene } from './audioScene'

describe('resolveMapBgmScene', () => {
  it('gives battle priority over map overlays and map peeking', () => {
    expect(resolveMapBgmScene({
      activeBattle: true,
      currentNodeKind: 'Elite',
      resolvedNodeKind: null,
      merchantVisible: true,
    })).toBe('battleElite')
  })

  it('uses the resolved Unknown-node battle type', () => {
    expect(resolveMapBgmScene({
      activeBattle: true,
      currentNodeKind: 'Unknown',
      resolvedNodeKind: 'Boss',
      merchantVisible: false,
    })).toBe('battleBoss')
  })

  it('uses a dedicated shop track and the map track for other map activities', () => {
    expect(resolveMapBgmScene({
      activeBattle: false,
      currentNodeKind: 'Merchant',
      resolvedNodeKind: null,
      merchantVisible: true,
    })).toBe('shop')
    expect(resolveMapBgmScene({
      activeBattle: false,
      currentNodeKind: 'Rest',
      resolvedNodeKind: null,
      merchantVisible: false,
    })).toBe('map')
  })
})
