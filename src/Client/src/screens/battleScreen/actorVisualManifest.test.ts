import { describe, expect, it } from 'vitest'
import {
  actorSizeClassForTier,
  PLAYABLE_ACTOR_VISUAL_IDS,
  resolveActorImpactAnchorY,
  resolveActorImpactCenterRatio,
  resolveActorStageProfile,
  resolveActorVisual,
} from './actorVisualManifest'

describe('actorVisualManifest', () => {
  it.each(PLAYABLE_ACTOR_VISUAL_IDS)('provides a complete motion set for %s', (id) => {
    const visual = resolveActorVisual({
      isHero: true,
      characterId: id,
      side: 'Ally',
    })

    expect(visual?.image).toBe(`/characters/${id}_stand.png`)
    expect(visual?.portrait).toBe(`/characters/portraits/${id}.png`)
    // セラ（default）は新規格で作り直したモーション。他のキャラは旧素材の値。
    const remade = id === 'default'
    expect(visual?.animations?.idle?.frameCount).toBe(8)
    expect(visual?.animations?.idle?.durationMs).toBe(remade ? 1200 : 1600)
    expect(visual?.animations?.attack?.frameCount).toBe(remade ? 6 : 4)
    expect(visual?.animations?.attack?.frameWidth).toBe(384)
    expect(visual?.animations?.attack?.durationMs).toBe(remade ? 600 : 480)
    expect(visual?.animations?.hurt?.frameCount).toBe(4)
    expect(visual?.animations?.hurt?.durationMs).toBe(remade ? 400 : 440)
  })

  it('registers attack B, skill and down only for remade characters', () => {
    const sera = resolveActorVisual({ isHero: true, characterId: 'default', side: 'Ally' })
    expect(sera?.animations?.attackAlt?.src).toBe('/characters/default_attack_alt.png')
    expect(sera?.animations?.attackAlt?.frameCount).toBe(6)
    // 奥義は上に余白を足した枠。表示の基準高さは他のシートと同じ 256。
    expect(sera?.animations?.skill?.frameHeight).toBe(288)
    expect(sera?.animations?.skill?.baseFrameHeight).toBe(256)
    expect(sera?.animations?.skill?.frameCount).toBe(8)
    expect(sera?.animations?.down?.loop).toBe(false)
    expect(sera?.animations?.down?.frameCount).toBe(6)

    const mireille = resolveActorVisual({ isHero: true, characterId: 'guardian_mireille', side: 'Ally' })
    expect(mireille?.animations?.attackAlt).toBeUndefined()
    expect(mireille?.animations?.skill).toBeUndefined()
    expect(mireille?.animations?.down).toBeUndefined()
  })

  it('routes enemy and summon art to separate asset namespaces', () => {
    expect(resolveActorVisual({
      isHero: false,
      imageId: 'slime_king',
      side: 'Enemy',
    })?.image).toBe('/characters/enemies/slime_king.png')
    expect(resolveActorVisual({
      isHero: false,
      imageId: 'wisp',
      side: 'Ally',
    })?.image).toBe('/characters/units/wisp.png')
  })

  it('falls back to the striker visual for an unknown hero id', () => {
    expect(resolveActorVisual({
      isHero: true,
      characterId: 'future_character',
      side: 'Ally',
    })?.image).toBe('/characters/default_stand.png')
  })

  it('classifies height tiers into stable presentation sizes', () => {
    expect(actorSizeClassForTier(1)).toBe('small')
    expect(actorSizeClassForTier(3)).toBe('small')
    expect(actorSizeClassForTier(4)).toBe('medium')
    expect(actorSizeClassForTier(6)).toBe('medium')
    expect(actorSizeClassForTier(7)).toBe('large')
    expect(actorSizeClassForTier(10)).toBe('large')
  })

  it.each([
    ['/characters/enemies/cave_bat_a.png', 0.42],
    ['/characters/enemies/cave_bat_b.png', 0.42],
    ['/characters/units/wisp.png', 0.38],
    ['/characters/enemies/six_ghost.png', 0.18],
  ] as const)('lifts airborne art while leaving grounded art untouched: %s', (image, liftRatio) => {
    expect(resolveActorStageProfile(image)).toEqual({ airborne: true, liftRatio })
    expect(resolveActorStageProfile('/characters/enemies/slime.png')).toEqual({
      airborne: false,
      liftRatio: 0,
    })
  })

  it('anchors impact feedback to the visible silhouette instead of the PNG canvas centre', () => {
    expect(resolveActorImpactCenterRatio('/characters/enemies/cave_bat_a.png')).toBe(0.773)
    expect(resolveActorImpactCenterRatio('/characters/enemies/louse_red.png')).toBe(0.748)
    expect(resolveActorImpactCenterRatio('/characters/default_stand.png')).toBe(0.516)
    expect(resolveActorImpactCenterRatio('/characters/enemies/future_enemy.png')).toBe(0.6)
    // Cave bat A: tier 2 => 82px display height and 34px flight lift.
    expect(resolveActorImpactAnchorY('/characters/enemies/cave_bat_a.png', 82)).toBe(143)
    // A grounded louse remains close to its small visible body near the floor.
    expect(resolveActorImpactAnchorY('/characters/enemies/louse_red.png', 60)).toBe(181)
  })
})
