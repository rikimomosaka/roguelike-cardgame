import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { PhaseAura, phaseRingStyle, slotTooltipContent } from './PhaseMarks'

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

describe('phaseRingStyle', () => {
  it('sits on the bottom edge of the dots and matches their width', () => {
    // キャンバス下端 198px、足元の下に 4 ドット（8px）の隙間 → ドットの下端は 190px。
    const style = phaseRingStyle({ bodyWidth: 90, footGap: 8 })
    expect(style.width).toBe('90px')
    const height = parseFloat(String(style.height))
    const top = parseFloat(String(style.top))
    expect(top + height).toBeLessThanOrEqual(194)
    expect(top + height).toBeGreaterThanOrEqual(190)
  })

  it('moves up with a floating actor', () => {
    const grounded = parseFloat(String(phaseRingStyle({ bodyWidth: 90, footGap: 8 }).top))
    const floating = parseFloat(String(phaseRingStyle({ bodyWidth: 90, footGap: 48 }).top))
    expect(grounded - floating).toBe(40)
  })
})

describe('slotTooltipContent', () => {
  it('appends the current phase and its effect after the trait', () => {
    const tip = slotTooltipContent({ traitName: '残光', traitDescription: '特性の説明。', phase: 'calm' })
    expect(tip?.name).toBe('特性：残光')
    expect(tip?.desc).toContain('特性の説明。')
    expect(tip?.desc).toContain('位相：[P:calm]')
    expect(tip?.desc).toContain('[K:bipolar]')
  })

  it('uses the phase as the title when there is no trait', () => {
    const tip = slotTooltipContent({ phase: 'motion' })
    const title = render(<>{tip?.name}</>).container
    expect(title.textContent).toBe('位相：動')
    expect(title.querySelector('.card-desc-phase--motion')).toBeInTheDocument()
    expect(tip?.desc).toContain('1.5倍')
    expect(tip?.desc).not.toContain('位相：')
  })

  it('keeps the trait-only tooltip and returns null without trait or phase', () => {
    expect(slotTooltipContent({ traitName: '残光', traitDescription: '説明' })).toEqual({ name: '特性：残光', desc: '説明' })
    expect(slotTooltipContent({ traitDescription: '説明' })?.name).toBe('特性')
    expect(slotTooltipContent({})).toBeNull()
  })
})
