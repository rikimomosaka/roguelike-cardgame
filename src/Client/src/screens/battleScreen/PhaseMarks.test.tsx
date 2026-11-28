import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { actorStatLines, PhaseAura, phaseRingStyle, slotTooltipContent } from './PhaseMarks'

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

  it('lists max HP, attack/defense and draw/energy-per-turn with highlighted numbers', () => {
    expect(actorStatLines({
      hpMax: 74,
      stats: { attack: 6, defense: 5, drawPerTurn: 5, energyPerTurn: 3 },
    })).toEqual([
      'HP [N:74]',
      '攻撃力 [N:6] / 守備力 [N:5]',
      'ドロー [N:5] / エナジー [N:3]',
    ])
    // 分からない行は省き、片方だけ分からない値は「—」。
    expect(actorStatLines({ hpMax: 40, stats: { attack: 6 } }))
      .toEqual(['HP [N:40]', '攻撃力 [N:6] / 守備力 —'])
  })

  it('shows the trait name as a heading above the trait effect, then the phase', () => {
    const tip = slotTooltipContent({
      ...base,
      stats: { attack: 6, defense: 5, drawPerTurn: 5, energyPerTurn: 3 },
      traitName: '策応',
      traitDescription: '特性の説明。',
      phase: 'calm',
    })
    const status = render(<>{tip.statusLine}</>).container
    expect(status.querySelector('.tip-actor-stats')?.textContent).toContain('HP 74')
    expect(status.querySelector('.tip__name.tip-actor-trait')?.textContent).toBe('特性：策応')
    const lines = tip.desc.split('\n')
    expect(lines.slice(0, 3)).toEqual(['特性の説明。', '', '位相：[P:calm]'])
    expect(lines.slice(3).join('\n')).toContain('[K:bipolar]')
  })

  it('lists opening actions after the trait so they can be checked later', () => {
    const tip = slotTooltipContent({
      ...base,
      traitDescription: '特性の説明。',
      openingActionNames: ['詠唱', '強防御'],
    })
    expect(tip.desc.split('\n')).toEqual(['特性の説明。', '', '先制行動：詠唱、強防御'])
  })

  it('has an empty body without a trait or phase', () => {
    const tip = slotTooltipContent({ ...base, stats: { attack: 6, defense: 5 } })
    expect(tip.desc).toBe('')
    expect(render(<>{tip.statusLine}</>).container.querySelector('.tip-actor-trait')).toBeNull()
  })
})
