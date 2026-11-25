import { describe, expect, it } from 'vitest'
import {
  actorSizeClassForTier,
  PLAYABLE_ACTOR_VISUAL_IDS,
  resolveActorImpactAnchorY,
  resolveActorImpactCenterRatio,
  resolveActorStageProfile,
  resolveActorVisual,
} from './actorVisualManifest'
import actorAssets from '../../presentation/generated/actor-assets.json'

const metrics = actorAssets.actors as Record<string, { centerY: number; footprint: number; top: number; bottom: number }>

describe('actorVisualManifest', () => {
  it.each(PLAYABLE_ACTOR_VISUAL_IDS)('provides 128x128 sheets and stage metrics for %s', (id) => {
    const visual = resolveActorVisual({
      isHero: true,
      characterId: id,
      side: 'Ally',
    })

    expect(visual?.image).toBe(`/assets/actors/heroes/${id}/stand.png`)
    expect(visual?.portrait).toBe(`/assets/portraits/${id}.png`)
    // 全主人公が待機・攻撃・被弾を持ち、コマは 128×128。
    for (const motion of ['idle', 'attack', 'hurt'] as const) {
      const sheet = visual?.animations?.[motion]
      expect(sheet?.src).toBe(`/assets/actors/heroes/${id}/${motion}.png`)
      expect(sheet?.frameWidth).toBe(128)
      expect(sheet?.frameHeight).toBe(128)
    }
    expect(visual?.animations?.idle?.loop).toBe(true)
    // 表示は全キャラ共通の ×2（256px）。
    expect(visual?.stage?.displayHeight).toBe(256)
  })

  it('uses the remade standard motions for Sera and the converted legacy timing for others', () => {
    const sera = resolveActorVisual({ isHero: true, characterId: 'default', side: 'Ally' })
    expect(sera?.animations?.idle).toMatchObject({ frameCount: 8, durationMs: 1200 })
    expect(sera?.animations?.attack).toMatchObject({ frameCount: 6, durationMs: 600 })
    expect(sera?.animations?.attackAlt).toMatchObject({
      src: '/assets/actors/heroes/default/attack_alt.png', frameCount: 6, durationMs: 600,
    })
    expect(sera?.animations?.skill).toMatchObject({ frameCount: 8, durationMs: 800, frameHeight: 128 })
    expect(sera?.animations?.guard).toMatchObject({
      src: '/assets/actors/heroes/default/guard.png', frameCount: 6, durationMs: 720, loop: false,
    })
    expect(sera?.animations?.cast).toMatchObject({
      src: '/assets/actors/heroes/default/cast.png', frameCount: 6, durationMs: 720, loop: false,
    })
    expect(sera?.animations?.hurt).toMatchObject({ frameCount: 4, durationMs: 400 })
    expect(sera?.animations?.down).toMatchObject({ frameCount: 6, durationMs: 720, loop: false })

    const mireille = resolveActorVisual({ isHero: true, characterId: 'guardian_mireille', side: 'Ally' })
    expect(mireille?.animations?.idle).toMatchObject({ frameCount: 8, durationMs: 1600 })
    expect(mireille?.animations?.attack).toMatchObject({ frameCount: 4, durationMs: 480 })
    expect(mireille?.animations?.attackAlt).toBeUndefined()
    expect(mireille?.animations?.skill).toBeUndefined()
    expect(mireille?.animations?.guard).toBeUndefined()
    expect(mireille?.animations?.down).toBeUndefined()
  })

  it('routes enemy and summon art to separate asset namespaces', () => {
    expect(resolveActorVisual({
      isHero: false,
      imageId: 'slime_king',
      side: 'Enemy',
    })?.image).toBe('/assets/actors/enemies/slime_king/stand.png')
    expect(resolveActorVisual({
      isHero: false,
      imageId: 'wisp',
      side: 'Ally',
    })?.image).toBe('/assets/actors/units/wisp/stand.png')
  })

  it('falls back to the striker visual for an unknown hero id', () => {
    expect(resolveActorVisual({
      isHero: true,
      characterId: 'future_character',
      side: 'Ally',
    })?.image).toBe('/assets/actors/heroes/default/stand.png')
  })

  it('classifies height tiers into stable presentation sizes', () => {
    expect(actorSizeClassForTier(1)).toBe('small')
    expect(actorSizeClassForTier(3)).toBe('small')
    expect(actorSizeClassForTier(4)).toBe('medium')
    expect(actorSizeClassForTier(6)).toBe('medium')
    expect(actorSizeClassForTier(7)).toBe('large')
    expect(actorSizeClassForTier(10)).toBe('large')
  })

  it('draws every actor at the same scale: the size difference lives inside the 128x128 canvas', () => {
    const slime = resolveActorVisual({ isHero: false, imageId: 'slime_green_s', side: 'Enemy' })
    const boss = resolveActorVisual({ isHero: false, imageId: 'act2_boss', side: 'Enemy' })
    expect(slime?.stage?.displayHeight).toBe(256)
    expect(boss?.stage?.displayHeight).toBe(256)
    // 影の幅はキャラの横幅で決まるので、小さい敵ほど狭い。
    expect(slime!.stage!.shadowWidth).toBeLessThan(boss!.stage!.shadowWidth)
  })

  it.each([
    '/assets/actors/enemies/cave_bat_a/stand.png',
    '/assets/actors/enemies/cave_bat_b/stand.png',
    '/assets/actors/units/wisp/stand.png',
    '/assets/actors/enemies/six_ghost/stand.png',
  ])('keeps flying actors in the air inside the art, never lifting the sprite: %s', (image) => {
    expect(resolveActorStageProfile(image)).toEqual({ airborne: true, liftRatio: 0 })
  })

  it('treats grounded and unknown actors as grounded', () => {
    expect(resolveActorStageProfile('/assets/actors/enemies/slime_big/stand.png')).toEqual({ airborne: false, liftRatio: 0 })
    expect(resolveActorStageProfile('/assets/actors/enemies/future_enemy/stand.png')).toEqual({ airborne: false, liftRatio: 0 })
  })

  it('anchors impact feedback to the visible silhouette measured at build time', () => {
    expect(resolveActorImpactCenterRatio('/assets/actors/enemies/cave_bat_a/stand.png'))
      .toBe(metrics['enemies/cave_bat_a'].centerY)
    expect(resolveActorImpactCenterRatio('/assets/actors/heroes/default/stand.png'))
      .toBe(metrics['heroes/default'].centerY)
    expect(resolveActorImpactCenterRatio('/assets/actors/enemies/future_enemy/stand.png')).toBe(0.6)
    // キャンバス下端 y=198、表示 256px のとき、中心は 198 − 256 × (1 − centerY)。
    const bat = metrics['enemies/cave_bat_a'].centerY
    expect(resolveActorImpactAnchorY('/assets/actors/enemies/cave_bat_a/stand.png', 256, 198))
      .toBe(Math.round(198 - 256 * (1 - bat)))
  })
})
