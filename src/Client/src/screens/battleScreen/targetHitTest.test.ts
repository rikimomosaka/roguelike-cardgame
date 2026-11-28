import { describe, expect, it } from 'vitest'
import { pickTargetAtPoint, type TargetRegion } from './targetHitTest'

const A: TargetRegion = { actorId: 'A', left: 0, right: 120, top: 0, bottom: 200 }
const B: TargetRegion = { actorId: 'B', left: 90, right: 210, top: 0, bottom: 200 }

describe('pickTargetAtPoint', () => {
  it('picks the only region under the point', () => {
    expect(pickTargetAtPoint([A, B], 40, 100, 'B')).toBe('A')
    expect(pickTargetAtPoint([A, B], 180, 100, 'A')).toBe('B')
  })

  it('prefers the region that is not currently targeted where two overlap', () => {
    // A の右側（B と重なる所）を押した: B を選んでいれば A
    expect(pickTargetAtPoint([A, B], 110, 100, 'B')).toBe('A')
    // 同じ所でも A を選んでいれば B
    expect(pickTargetAtPoint([A, B], 100, 100, 'A')).toBe('B')
  })

  it('falls back to the nearest centre when neither overlapping region is targeted', () => {
    expect(pickTargetAtPoint([A, B], 95, 100, null)).toBe('A')
    expect(pickTargetAtPoint([A, B], 118, 100, 'C')).toBe('B')
  })

  it('returns null outside every region', () => {
    expect(pickTargetAtPoint([A, B], 300, 100, null)).toBeNull()
    expect(pickTargetAtPoint([A, B], 50, 260, null)).toBeNull()
  })
})
