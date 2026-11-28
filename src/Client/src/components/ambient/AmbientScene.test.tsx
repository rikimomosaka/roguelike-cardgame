import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { AmbientScene } from './AmbientScene'
import { AMBIENT_SCENES } from './ambientScenes'

describe('AmbientScene', () => {
  it('renders the layers defined for a scene', () => {
    const { container } = render(<AmbientScene scene="battle_forest" />)
    const def = AMBIENT_SCENES.battle_forest
    expect(container.querySelector('[data-ambient="battle_forest"]')).toBeInTheDocument()
    // 絵そのものを動かす揺れの層は使わない（2026-11-28 に廃止）。
    expect(container.querySelectorAll('.ambient__sway')).toHaveLength(0)
    expect(container.querySelectorAll('.ambient__ray')).toHaveLength(def.rays!.length)
    const particleCount = def.particles!.reduce((sum, group) => sum + group.count, 0)
    expect(container.querySelectorAll('.ambient__particle')).toHaveLength(particleCount)
  })

  it('lays particles out the same way every time (seeded)', () => {
    const a = render(<AmbientScene scene="battle_hell" />).container.querySelector('.ambient__particle')?.getAttribute('style')
    const b = render(<AmbientScene scene="battle_hell" />).container.querySelector('.ambient__particle')?.getAttribute('style')
    expect(a).toBe(b)
  })

  it('renders nothing for an unknown scene', () => {
    expect(render(<AmbientScene scene="no_such_scene" />).container.innerHTML).toBe('')
    expect(render(<AmbientScene scene={null} />).container.innerHTML).toBe('')
  })

  it('keeps every effect inside its background and every light faint', () => {
    for (const [id, scene] of Object.entries(AMBIENT_SCENES)) {
      for (const glow of scene.glows ?? []) {
        expect(glow.max, `${id} glow`).toBeLessThanOrEqual(0.32)
        expect(glow.x, `${id} glow x`).toBeLessThanOrEqual(scene.width)
        expect(glow.y, `${id} glow y`).toBeLessThanOrEqual(scene.height)
      }
      for (const ray of scene.rays ?? []) expect(ray.max, `${id} ray`).toBeLessThanOrEqual(0.25)
    }
  })
})
