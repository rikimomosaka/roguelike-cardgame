import { describe, expect, it } from 'vitest'
import { render } from '@testing-library/react'
import { Card } from './Card'
import { CardArtOwnerProvider, ownerArtTone } from './CardArtOwner'

describe('enemy cards', () => {
  it('use the enemy frame', () => {
    const { container } = render(<Card name="通常攻撃" cost={1} type="attack" rarity="t" cardId="enemy_common_attack" />)
    expect(container.querySelector('.card__affiliation-frame')).toHaveAttribute('src', '/assets/cards/frames/affiliations/enemy.png')
  })

  it('show the owner sprite with a tone by card kind', () => {
    const { container } = render(
      <CardArtOwnerProvider owner={{ kind: 'enemies', imageId: 'jaw_worm' }}>
        <Card name="通常攻撃" cost={1} type="attack" rarity="t" cardId="enemy_common_attack" />
        <Card name="通常防御" cost={1} type="skill" rarity="t" cardId="enemy_common_defend" description="自身に[N:5]ブロック。" />
      </CardArtOwnerProvider>,
    )
    const arts = container.querySelectorAll('.card-owner-art')
    expect(arts[0]).toHaveClass('card-owner-art--attack')
    expect(arts[1]).toHaveClass('card-owner-art--defense')
    expect(arts[0].querySelector('img')).toHaveAttribute('src', '/assets/actors/enemies/jaw_worm/stand.png')
  })

  it('keep the placeholder without an owner, and class cards ignore the owner', () => {
    const { container } = render(
      <CardArtOwnerProvider owner={{ kind: 'enemies', imageId: 'jaw_worm' }}>
        <Card name="攻撃" cost={1} type="attack" rarity="c" cardId="strike" poolClass="Striker" />
      </CardArtOwnerProvider>,
    )
    expect(container.querySelector('.card-owner-art')).toBeNull()
    expect(ownerArtTone('skill', '筋力を得る')).toBe('other')
    expect(ownerArtTone('skill', '何も起こらない。')).toBe('other')
    // 攻撃とブロックの両方を含むカードは紫。
    expect(ownerArtTone('attack', '敵全体に[N:7]アタック。自身に[N:5]ブロック。')).toBe('other')
    expect(ownerArtTone('skill', '自身に[N:5]ブロック。')).toBe('defense')
  })
})
