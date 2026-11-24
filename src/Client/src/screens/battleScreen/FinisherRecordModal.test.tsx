import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { FinisherRecordDto } from '../../api/types'
import { TooltipHost } from '../../components/Tooltip'
import { PlayHistoryModal } from './FinisherRecordModal'

const record: FinisherRecordDto = {
  afterimageStacks: 2,
  savedByFinisher: true,
  isPlannedAfterimageIntentAvailable: true,
  plannedAfterimageIntent: {
    attackSingle: 20,
    attackSingleHits: 2,
    attackRandom: 8,
    attackRandomHits: 1,
    attackAll: null,
    attackAllHits: 0,
    attackHits: 3,
    block: 0,
    hasBuff: false,
    hasDebuff: false,
    hasHeal: false,
  },
  cards: [{
    playSequence: 7,
    card: {
      cardInstanceId: 'finish-card-1',
      definitionId: 'finishing_slash',
      displayName: '決着斬り',
      cardType: 'Attack',
      rarity: 'Common',
      isUpgraded: true,
      originalCost: 3,
      currentCost: 3,
      paidCost: 2,
      description: '敵単体に[N:10]アタック。',
      costChanges: [],
      poolClass: 'Striker',
    },
  }],
}

const otherPlay = {
  playSequence: 6,
  card: {
    ...record.cards[0].card,
    cardInstanceId: 'guard-1',
    definitionId: 'basic_defense',
    displayName: '防御の基本',
    cardType: 'Skill',
    isUpgraded: false,
  },
}

describe('PlayHistoryModal', () => {
  it('lists every play this turn and numbers only the afterimage-saved cards', () => {
    const { container } = render(
      <TooltipHost>
        <PlayHistoryModal
          history={[otherPlay, record.cards[0]]}
          record={record}
          onClose={() => {}}
        />
      </TooltipHost>,
    )

    expect(screen.getByRole('dialog', { name: 'このターンのプレイ履歴' })).toBeInTheDocument()
    const names = Array.from(container.querySelectorAll('.finisher-record__cards .card__name'))
      .map(node => node.textContent)
    expect(names).toEqual(['防御の基本', '決着斬り+'])
    expect(container.querySelectorAll('.finisher-record__order')).toHaveLength(1)
    expect(container.querySelector('[data-afterimage-order="1"] .card__name')).toHaveTextContent('決着斬り+')
  })

  it('does not number or explain the ordinary last-attack save without Finisher', () => {
    const { container } = render(
      <TooltipHost>
        <PlayHistoryModal
          history={[otherPlay, record.cards[0]]}
          record={{ ...record, savedByFinisher: false }}
          onClose={() => {}}
        />
      </TooltipHost>,
    )
    expect(container.querySelector('.finisher-record__order')).toBeNull()
    expect(container.querySelector('.finisher-record__note')).toBeNull()
    expect(container.querySelectorAll('.finisher-record__cards .card__name')).toHaveLength(2)
  })

  it('works as a plain history without any saved afterimage', () => {
    const { container } = render(
      <TooltipHost>
        <PlayHistoryModal history={[otherPlay]} record={null} onClose={() => {}} />
      </TooltipHost>,
    )
    expect(container.querySelector('.finisher-record__order')).toBeNull()
    expect(container.querySelector('.finisher-record__note')).toBeNull()
    expect(container.querySelector('.card__name')).toHaveTextContent('防御の基本')
  })

  it('shows the saved card face and afterimage count', () => {
    render(
      <TooltipHost>
        <PlayHistoryModal
          history={[]}
          record={record}
          onClose={() => {}}
          plannedIntent={<div data-testid="planned-intent">20 / 8</div>}
        />
      </TooltipHost>,
    )

    expect(screen.getByText(
      '攻撃終了時に、番号の付いた1枚を番号順に2回ずつ再実行する（残光2）。',
    )).toBeInTheDocument()
    expect(document.querySelector('.card__name')).toHaveTextContent('決着斬り+')
    expect(screen.getByRole('region', { name: '残光の予定アタック値' })).toBeInTheDocument()
    expect(screen.getByTestId('planned-intent')).toHaveTextContent('20 / 8')
    expect(screen.queryByText(/対象側のブロック/)).not.toBeInTheDocument()
  })

  it('closes from the close button', () => {
    const onClose = vi.fn()
    render(
      <TooltipHost>
        <PlayHistoryModal history={[]} record={record} onClose={onClose} />
      </TooltipHost>,
    )

    fireEvent.click(screen.getByRole('button', { name: '閉じる' }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('explains when Core cannot project a deterministic afterimage value', () => {
    render(
      <TooltipHost>
        <PlayHistoryModal
          history={[]}
          record={{
            ...record,
            plannedAfterimageIntent: null,
            isPlannedAfterimageIntentAvailable: false,
          }}
          onClose={() => {}}
        />
      </TooltipHost>,
    )

    expect(screen.getByText('後続効果により予定値を算出できません。')).toBeInTheDocument()
  })
})
