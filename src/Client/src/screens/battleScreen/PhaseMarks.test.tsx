import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { PhaseAura, slotTooltipContent } from './PhaseMarks'

describe('PhaseMarks', () => {
  it('shows a calm aura ring for 静 and a motion ring for 動', () => {
    const calm = render(<PhaseAura phase="calm" />)
    expect(calm.container.querySelector('.slot-phase-aura--calm')).toBeInTheDocument()
    const motion = render(<PhaseAura phase="motion" />)
    expect(motion.container.querySelector('.slot-phase-aura--motion')).toBeInTheDocument()
  })

  it('renders nothing without a phase', () => {
    const { container } = render(<PhaseAura />)
    expect(container.innerHTML).toBe('')
  })
})

describe('slotTooltipContent', () => {
  it('appends the current phase and its effect after the trait', () => {
    const tip = slotTooltipContent({ traitName: '残光', traitDescription: '特性の説明。', phase: 'calm' })
    expect(tip?.name).toBe('残光')
    expect(tip?.desc).toContain('特性の説明。')
    expect(tip?.desc).toContain('位相：静')
    expect(tip?.desc).toContain('[K:bipolar]')
  })

  it('uses the phase as the title when there is no trait', () => {
    const tip = slotTooltipContent({ phase: 'motion' })
    expect(tip?.name).toBe('位相：動')
    expect(tip?.desc).toContain('1.5倍')
    expect(tip?.desc).not.toContain('位相：動')
  })

  it('keeps the trait-only tooltip and returns null without trait or phase', () => {
    expect(slotTooltipContent({ traitName: '残光', traitDescription: '説明' })).toEqual({ name: '残光', desc: '説明' })
    expect(slotTooltipContent({ traitDescription: '説明' })?.name).toBe('特性')
    expect(slotTooltipContent({})).toBeNull()
  })
})
