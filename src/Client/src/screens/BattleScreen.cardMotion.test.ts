import { describe, expect, it } from 'vitest'
import {
  contingencyEffectMotion,
  contingencyEffectText,
  playedCardArmsContingency,
  skillCardMotion,
} from './BattleScreen'

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

describe('contingency presentation helpers', () => {
  it('detects a played card that arms a contingency', () => {
    const steps = [step('PlayCard', 'hero'), step('ContingencyArmed', 'hero'), step('PlayCard', 'hero')]
    expect(playedCardArmsContingency(steps, 0, 'hero')).toBe(true)
    expect(playedCardArmsContingency(steps, 2, 'hero')).toBe(false)
  })

  it('picks the trigger motion from the resolved effects', () => {
    expect(contingencyEffectMotion(
      [step('ContingencyTriggered', 'hero'), step('GainBlock', 'hero', 6), step('ContingencyResolved', 'hero')],
      0, 'hero')).toBe('guard')
    expect(contingencyEffectMotion(
      [step('ContingencyTriggered', 'hero'), step('GainAttack', 'hero', 6)], 0, 'hero')).toBe('attack')
    expect(contingencyEffectMotion(
      [step('ContingencyTriggered', 'hero'), step('AttackFire', 'hero', 6)], 0, 'hero')).toBeNull()
    expect(contingencyEffectMotion(
      [step('ContingencyTriggered', 'hero'), step('ApplyStatus', 'hero', 1)], 0, 'hero')).toBe('cast')
  })

  it('copies only the effect lines after the contingency condition', () => {
    expect(contingencyEffectText('[K:wild]\n[K:contingency]：相手の攻撃時。\n自身に[N:8]ブロック。\n1枚引く。'))
      .toBe('自身に[N:8]ブロック。\n1枚引く。')
    // JSON 由来の "\\n"（2 文字）も改行として扱う。
    expect(contingencyEffectText('[K:contingency]：ターン開始時。\\n敵全体に脱力[N:1]を付与する。'))
      .toBe('敵全体に脱力[N:1]を付与する。')
  })
})
