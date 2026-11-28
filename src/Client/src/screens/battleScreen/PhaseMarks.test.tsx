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
  const base = { name: 'phasetest1', charName: '作戦参謀ユノ', hpCur: 74, hpMax: 74 }

  it('titles the tooltip with the character name and controlling account', () => {
    expect(slotTooltipContent({ ...base, controllerLabel: 'phasetest1' }).name).toBe('作戦参謀ユノ(phasetest1)')
    expect(slotTooltipContent({ ...base, charName: undefined, name: 'ダイア・ウルフ' }).name).toBe('ダイア・ウルフ')
  })

  it('lists HP, attack/defense and draw/energy before the trait and phase', () => {
    const tip = slotTooltipContent({
      ...base,
      stats: { attack: 6, defense: 5, drawPerTurn: 5, energy: 2, energyPerTurn: 3 },
      traitName: '策応',
      traitDescription: '特性の説明。',
      phase: 'calm',
    })
    const lines = tip.desc.split('\n')
    expect(lines.slice(0, 8)).toEqual([
      'HP74/74',
      '攻撃力6 / 守備力5',
      'ドロー5 / エナジー2/3',
      '',
      '特性：策応',
      '特性の説明。',
      '',
      '位相：[P:calm]',
    ])
    // 位相の効果（静の説明）が続く。
    expect(lines.slice(8).join('\n')).toContain('[K:bipolar]')
  })

  it('omits unknown stat rows and the phase when there is none', () => {
    const tip = slotTooltipContent({ ...base, stats: { attack: 6, defense: 5 } })
    expect(tip.desc).toBe('HP74/74\n攻撃力6 / 守備力5')
  })
})
