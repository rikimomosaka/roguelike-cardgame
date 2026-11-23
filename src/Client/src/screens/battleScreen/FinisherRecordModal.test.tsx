import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { FinisherRecordDto } from '../../api/types'
import { TooltipHost } from '../../components/Tooltip'
import { FinisherRecordModal } from './FinisherRecordModal'

const record: FinisherRecordDto = {
  afterimageStacks: 2,
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

describe('FinisherRecordModal', () => {
  it('shows the saved card face and afterimage count', () => {
    render(
      <TooltipHost>
        <FinisherRecordModal
          record={record}
          onClose={() => {}}
          plannedIntent={<div data-testid="planned-intent">20 / 8</div>}
        />
      </TooltipHost>,
    )

    expect(screen.getByRole('dialog', { name: '保存された残光' })).toBeInTheDocument()
    expect(screen.getByText(
      '通常攻撃後に、以下の1枚を使用した順に2回ずつ再実行する（残光2）。',
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
        <FinisherRecordModal record={record} onClose={onClose} />
      </TooltipHost>,
    )

    fireEvent.click(screen.getByRole('button', { name: '閉じる' }))
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('explains when Core cannot project a deterministic afterimage value', () => {
    render(
      <TooltipHost>
        <FinisherRecordModal
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
