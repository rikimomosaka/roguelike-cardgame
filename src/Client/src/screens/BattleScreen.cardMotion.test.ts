import { describe, expect, it } from 'vitest'
import { skillCardMotion } from './BattleScreen'

const step = (kind: string, casterInstanceId: string | null, amount: number | null = null) => ({
  event: { kind, casterInstanceId, amount },
})

describe('skillCardMotion', () => {
  it('guards when the caster gains block while the played card resolves', () => {
    const steps = [step('PlayCard', 'hero'), step('GainBlock', 'hero', 5)]
    expect(skillCardMotion(steps, 0, 'hero')).toBe('guard')
  })

  it('casts when the skill neither grants block nor attacks', () => {
    const steps = [step('PlayCard', 'hero'), step('ApplyStatus', 'hero', 2), step('Draw', 'hero')]
    expect(skillCardMotion(steps, 0, 'hero')).toBe('cast')
  })

  it('leaves skills that attack to the attack motion', () => {
    const steps = [step('PlayCard', 'hero'), step('GainBlock', 'hero', 5), step('AttackFire', 'hero')]
    expect(skillCardMotion(steps, 0, 'hero')).toBeNull()
  })

  it('only looks at events until the next card play', () => {
    const steps = [step('PlayCard', 'hero'), step('Draw', 'hero'), step('PlayCard', 'hero'), step('GainBlock', 'hero', 5)]
    expect(skillCardMotion(steps, 0, 'hero')).toBe('cast')
  })

  it('ignores block gained by someone else and zero-amount block', () => {
    const steps = [step('PlayCard', 'hero'), step('GainBlock', 'enemy', 5), step('GainBlock', 'hero', 0)]
    expect(skillCardMotion(steps, 0, 'hero')).toBe('cast')
  })
})
