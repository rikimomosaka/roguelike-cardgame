import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { CombatFeedbackStack } from './CombatFeedbackLayer'
import {
  appendCombatFeedback,
  blockGainFeedbackText,
  combatFeedbackLifetimeMs,
  STATUS_FEEDBACK_LIFETIME_MS,
} from './combatFeedbackTiming'

describe('CombatFeedbackStack', () => {
  it('renders semantic target feedback with stable visual kinds', () => {
    render(<CombatFeedbackStack items={[
      { id: 1, targetId: 'enemy-1', kind: 'damage', text: '-12' },
      { id: 2, targetId: 'enemy-1', kind: 'block', text: 'ブロック +4' },
      { id: 3, targetId: 'hero', kind: 'attack', text: '単体アタック +6' },
    ]} />)

    expect(screen.getByText('-12')).toHaveClass('combat-feedback__item--damage')
    expect(screen.getByText('ブロック +4')).toHaveClass('combat-feedback__item--block')
    expect(screen.getByText('単体アタック +6')).toHaveClass('combat-feedback__item--attack')
  })

  it('keeps status names readable for about one second after their entrance', () => {
    expect(combatFeedbackLifetimeMs('buff')).toBe(STATUS_FEEDBACK_LIFETIME_MS)
    expect(combatFeedbackLifetimeMs('debuff')).toBe(STATUS_FEEDBACK_LIFETIME_MS)
    expect(STATUS_FEEDBACK_LIFETIME_MS).toBe(1500)
    expect(combatFeedbackLifetimeMs('damage')).toBe(860)
  })

  it('uses the localized block-gain wording', () => {
    expect(blockGainFeedbackText(8)).toBe('ブロック +8')
  })

  it('never evicts status names when dense impact feedback reaches its cap', () => {
    const status = { id: 1, targetId: 'hero', kind: 'debuff' as const, text: '筋力が2低下' }
    const crowded = Array.from({ length: 40 }, (_, index) => ({
      id: index + 2,
      targetId: 'hero',
      kind: 'damage' as const,
      text: `-${index + 1}`,
    })).reduce(appendCombatFeedback, [status])

    expect(crowded).toContainEqual(status)
    expect(crowded.filter(item => item.kind === 'damage')).toHaveLength(6)
  })
})
