import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { PhaseAura, PhaseTag } from './PhaseMarks'

describe('PhaseMarks', () => {
  it('shows a blue calm aura and a 静 tag', () => {
    const { container } = render(<><PhaseAura phase="calm" /><PhaseTag phase="calm" /></>)
    expect(container.querySelector('.slot-phase-aura--calm')).toBeInTheDocument()
    expect(container.querySelector('.slot-phase-tag')?.textContent).toBe('静')
  })

  it('shows a red motion aura and a 動 tag', () => {
    const { container } = render(<><PhaseAura phase="motion" /><PhaseTag phase="motion" /></>)
    expect(container.querySelector('.slot-phase-aura--motion')).toBeInTheDocument()
    expect(container.querySelector('.slot-phase-tag')?.textContent).toBe('動')
  })

  it('renders nothing without a phase', () => {
    const { container } = render(<><PhaseAura /><PhaseTag /></>)
    expect(container.innerHTML).toBe('')
  })
})
