import { createRoot } from 'react-dom/client'
import type {
  BattleParticipantDto,
  BattleStateDto,
  CombatActorDto,
  RunSnapshotDto,
} from '../src/api/types'
import { TooltipHost } from '../src/components/Tooltip'
import { BattleScreen } from '../src/screens/BattleScreen'

function actor(
  instanceId: string,
  definitionId: string,
  side: 'Ally' | 'Enemy',
  slotIndex: number,
  currentHp: number,
  blockDisplay = 0,
): CombatActorDto {
  return {
    instanceId,
    definitionId,
    side,
    slotIndex,
    currentHp,
    maxHp: 30,
    blockDisplay,
    attackSingleDisplay: 0,
    attackRandomDisplay: 0,
    attackAllDisplay: 0,
    statuses: {},
    currentMoveId: null,
    remainingLifetimeTurns: null,
    associatedSummonHeldInstanceId: null,
    intent: null,
  }
}

function participant(
  participantId: string,
  actorInstanceId: string,
  controllerKind: 'Human' | 'Cpu',
): BattleParticipantDto {
  return {
    participantId,
    actorInstanceId,
    controllerKind,
    ownerPlayerSlot: 0,
    characterId: controllerKind === 'Cpu' ? 'wisp' : 'default',
    targetAllyIndex: 0,
    targetEnemyIndex: 0,
    energy: 2,
    energyPerTurn: 1,
    drawPerTurn: 3,
    drawPile: [],
    hand: [],
    discardPile: [],
    exhaustPile: [],
    summonHeld: [],
    powerCards: [],
    comboCount: 0,
    maxComboThisTurn: 0,
    comboGauge: 0,
    lastPlayedOrigCost: null,
    nextCardComboFreePass: false,
    wildUsedInCurrentCombo: false,
    usedCharacterSkillIdsThisTurn: [],
    characterSkillUsesThisTurn: {},
    cardUsesThisTurn: {},
    cardUseLockedThisTurn: false,
    ownedRelicIds: [],
    potions: [],
    potionUsesThisTurn: {},
    pendingCardPlay: null,
    cpuCardPlaysThisTurn: 0,
    isReady: true,
  }
}

export const primary = participant('player:0', 'hero-1', 'Human')
export const cpu = participant('cpu:wisp-1', 'wisp-1', 'Cpu')
export const before: BattleStateDto = {
  turn: 1,
  phase: 'PlayerInput',
  outcome: 'Pending',
  allies: [
    { ...actor('hero-1', 'hero', 'Ally', 0, 80), maxHp: 80 },
    actor('wisp-1', 'wisp', 'Ally', 1, 30),
  ],
  enemies: [actor('enemy-1', 'slime', 'Enemy', 0, 30, 2)],
  targetAllyIndex: 0,
  targetEnemyIndex: 0,
  energy: 2,
  energyPerTurn: 1,
  drawPile: [],
  hand: [],
  discardPile: [],
  exhaustPile: [],
  summonHeld: [],
  powerCards: [],
  comboCount: 0,
  maxComboThisTurn: 0,
  comboGauge: 0,
  usedCharacterSkillIdsThisTurn: [],
  lastPlayedOrigCost: null,
  nextCardComboFreePass: false,
  ownedRelicIds: [],
  potions: [],
  encounterId: 'replay-e2e',
  pendingCardPlay: null,
  actionRevision: 1,
  participants: [primary, cpu],
}
export const afterCard: BattleStateDto = {
  ...before,
  actionRevision: 2,
  participants: [primary, { ...cpu, energy: 1, comboCount: 1, maxComboThisTurn: 1 }],
}
export const afterResolution: BattleStateDto = {
  ...afterCard,
  actionRevision: 3,
  enemies: [actor('enemy-1', 'slime', 'Enemy', 0, 18, 0)],
}

const snapshot: RunSnapshotDto = {
  run: {
    schemaVersion: 3,
    currentAct: 1,
    currentNodeId: 1,
    visitedNodeIds: [0, 1],
    unknownResolutions: {},
    characterId: 'default',
    currentHp: 80,
    maxHp: 80,
    gold: 0,
    deck: [],
    potions: [],
    potionSlotCount: 0,
    activeBattle: null,
    activeReward: null,
    activeMerchant: null,
    activeEvent: null,
    activeRestPending: false,
    activeRestCompleted: false,
    relics: [],
    playSeconds: 0,
    progress: 'InProgress',
    savedAtUtc: '2026-08-17T00:00:00Z',
    activeActStartRelicChoice: null,
  },
  map: {
    act: 1,
    currentNodeId: 1,
    nodes: [],
    edges: [],
  },
}

export async function mountParticipantReplayFixture() {
  document.getElementById('participant-replay-fixture')?.remove()
  const host = document.createElement('div')
  host.id = 'participant-replay-fixture'
  document.body.append(host)
  createRoot(host).render(
    <TooltipHost>
      <BattleScreen
        accountId="replay-e2e"
        snapshot={snapshot}
        onBattleResolved={() => {}}
        onTogglePeek={() => {}}
      />
    </TooltipHost>,
  )
  await new Promise<void>(resolve => requestAnimationFrame(() => resolve()))
  document.documentElement.dataset.participantReplayReady = 'true'
}
