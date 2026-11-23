import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { BattleReplayEntryDto } from '../../api/types'
import { BattleReplayControls } from './BattleReplayControls'

function entry(overrides: Partial<BattleReplayEntryDto> = {}): BattleReplayEntryDto {
  return {
    sequence: 1,
    turn: 1,
    commandId: 'command-1',
    kind: 'playCard',
    viewKind: 'action',
    participantId: 'cpu-1',
    actorInstanceId: 'wisp-1',
    revisionBefore: 0,
    revisionAfter: 1,
    card: null,
    targetActorInstanceIds: [],
    effects: [],
    intents: [],
    feedChanges: [],
    isResolution: false,
    note: null,
    ...overrides,
  }
}

const callbacks = {
  onBack: vi.fn(),
  onRestart: vi.fn(),
  onTogglePlaying: vi.fn(),
  onSkip: vi.fn(),
  onSpeedChange: vi.fn(),
}

describe('BattleReplayControls', () => {
  it('shows source actions as full card content and all requested controls', () => {
    const source = entry({
      card: {
        cardInstanceId: 'c1',
        definitionId: 'wisp_spark',
        displayName: '火花',
        cardType: 'Attack',
        rarity: 'Token',
        isUpgraded: false,
        originalCost: 1,
        currentCost: 1,
        paidCost: 1,
        description: '敵単体に3アタック。',
        costChanges: [],
      },
    })
    render(
      <BattleReplayControls
        subjectName="ウィスプ"
        feedState="ready"
        entries={[source]}
        position={1}
        playing={false}
        speed={1}
        renderCard={() => <div>完全なカード表示</div>}
        {...callbacks}
      />,
    )

    expect(screen.getByLabelText('ウィスプの行動再生')).toBeInTheDocument()
    expect(screen.getByText('完全なカード表示')).toBeInTheDocument()
    expect(screen.getByLabelText('行動状態 READY')).toBeInTheDocument()
    expect(screen.queryByLabelText('再生進捗')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: '戻る' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '最初から再生' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'スキップ' })).toBeDisabled()
  })

  it('uses compact external effects and wires playback controls', () => {
    const onBack = vi.fn()
    const onTogglePlaying = vi.fn()
    const onSpeedChange = vi.fn()
    render(
      <BattleReplayControls
        subjectName="ウィスプ"
        feedState="live"
        entries={[entry({
          viewKind: 'externalEffect',
          actorInstanceId: 'hero-1',
          effects: [{
            kind: 'gainEnergy',
            sourceActorInstanceId: 'hero-1',
            targetParticipantId: 'cpu-1',
            targetActorInstanceId: 'wisp-1',
            amount: 1,
            statusId: null,
            description: '冒険者によりエナジー+1。',
          }],
        })]}
        position={0}
        playing={false}
        speed={1}
        onBack={onBack}
        onRestart={vi.fn()}
        onTogglePlaying={onTogglePlaying}
        onSkip={vi.fn()}
        onSpeedChange={onSpeedChange}
      />,
    )

    expect(screen.getByLabelText('行動状態 LIVE')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: '戻る' }))
    fireEvent.click(screen.getByRole('button', { name: '再生' }))
    fireEvent.change(screen.getByLabelText('再生速度'), { target: { value: '2' } })
    expect(onBack).toHaveBeenCalledTimes(1)
    expect(onTogglePlaying).toHaveBeenCalledTimes(1)
    expect(onSpeedChange).toHaveBeenCalledWith(2)
  })

  it('shows ability names and human-readable compact external sources', () => {
    const ability = entry({
      kind: 'useCharacterSkill',
      ability: {
        definitionId: 'wisp-focus',
        displayName: '燐光集中',
        kind: 'characterSkill',
        description: '自身に筋力1を付与。',
        energyCost: 0,
        comboGaugeCost: 2,
        usesBefore: 0,
        usesPerTurn: 1,
      },
      source: {
        actorInstanceId: 'wisp-1',
        actorDisplayName: 'ウィスプ',
        actionKind: 'characterSkill',
        actionDefinitionId: 'wisp-focus',
        actionDisplayName: '燐光集中',
      },
    })
    const inbound = entry({
      sequence: 2,
      commandId: 'external-2',
      viewKind: 'externalEffect',
      participantId: 'hero-player',
      actorInstanceId: 'hero-1',
      source: {
        actorInstanceId: 'hero-1',
        actorDisplayName: '冒険者',
        actionKind: 'potion',
        actionDefinitionId: 'energy-potion',
        actionDisplayName: '活力ポーション',
      },
      effects: [{
        kind: 'gainEnergy',
        sourceActorInstanceId: 'hero-1',
        targetParticipantId: 'cpu-1',
        targetActorInstanceId: 'wisp-1',
        amount: 1,
        statusId: null,
        description: 'エナジーを1得た。',
      }],
    })
    const props = {
      subjectName: 'ウィスプ',
      feedState: 'live',
      entries: [ability, inbound],
      playing: false,
      speed: 1 as const,
      ...callbacks,
    }
    const { rerender } = render(<BattleReplayControls {...props} position={1} />)

    expect(screen.getByText('燐光集中')).toBeInTheDocument()
    expect(screen.getByText('自身に筋力1を付与。')).toBeInTheDocument()

    rerender(<BattleReplayControls {...props} position={2} />)
    expect(screen.getByText('活力ポーションから受けた効果')).toBeInTheDocument()
    expect(screen.getByText('発生元: 冒険者（活力ポーション）')).toBeInTheDocument()
  })
})
