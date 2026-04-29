export type AccountDto = {
  id: string
  createdAt: string
}

export type CardInstanceDto = {
  id: string
  upgraded: boolean
}

export type MerchantOfferDto = {
  kind: string
  id: string
  price: number
  sold: boolean
}

export type MerchantInventoryDto = {
  cards: MerchantOfferDto[]
  relics: MerchantOfferDto[]
  potions: MerchantOfferDto[]
  discardSlotUsed: boolean
  discardPrice: number
}

export type EventChoiceSnapshotDto = {
  label: string
  conditionSummary: string | null
  conditionMet: boolean
  resultMessage: string
}

export type EventInstanceDto = {
  eventId: string
  name: string
  startMessage: string
  choices: EventChoiceSnapshotDto[]
  chosenIndex: number | null
}

export type AudioSettingsDto = {
  schemaVersion: number
  master: number
  bgm: number
  se: number
  ambient: number
}

export type TileKind =
  | 'Start'
  | 'Enemy'
  | 'Elite'
  | 'Rest'
  | 'Merchant'
  | 'Treasure'
  | 'Event'
  | 'Unknown'
  | 'Boss'

export type RunProgress = 'InProgress' | 'Cleared' | 'GameOver' | 'Abandoned'

export type BattleOutcome = 'Pending' | 'Victory'
export type CardRewardStatus = 'Pending' | 'Claimed' | 'Skipped'

export type PlaceholderEnemyInstanceDto = {
  enemyDefinitionId: string
  name: string
  imageId: string
  currentHp: number
  maxHp: number
  currentMoveId: string
}

export type BattlePlaceholderStateDto = {
  encounterId: string
  enemies: PlaceholderEnemyInstanceDto[]
  outcome: BattleOutcome
}

export type RewardStateDto = {
  gold: number
  goldClaimed: boolean
  potionId: string | null
  potionClaimed: boolean
  cardChoices: string[]
  cardStatus: CardRewardStatus
  relicId: string | null
  relicClaimed: boolean
  isBossReward: boolean
}

export type ActStartRelicChoiceDto = {
  relicIds: string[]
}

export type RunResultCardDto = {
  id: string
  upgraded: boolean
}

export type RunResultJourneyEntryDto = {
  act: number
  nodeId: number
  kind: TileKind
  resolvedKind: TileKind | null
}

export type RunResultDto = {
  schemaVersion: number
  accountId: string
  runId: string
  outcome: RunProgress
  actReached: number
  nodesVisited: number
  playSeconds: number
  characterId: string
  finalHp: number
  finalMaxHp: number
  finalGold: number
  finalDeck: RunResultCardDto[]
  finalRelics: string[]
  endedAtUtc: string
  seenCardBaseIds: string[]
  acquiredRelicIds: string[]
  acquiredPotionIds: string[]
  encounteredEnemyIds: string[]
  journeyLog: RunResultJourneyEntryDto[]
}

export type RunHistoryDto = RunResultDto[]

export type RunStateDto = {
  schemaVersion: number
  currentAct: number
  currentNodeId: number
  visitedNodeIds: number[]
  unknownResolutions: Record<number, TileKind>
  characterId: string
  currentHp: number
  maxHp: number
  gold: number
  deck: CardInstanceDto[]
  potions: string[]
  potionSlotCount: number
  activeBattle: BattlePlaceholderStateDto | null
  activeReward: RewardStateDto | null
  activeMerchant: MerchantInventoryDto | null
  activeEvent: EventInstanceDto | null
  activeRestPending: boolean
  activeRestCompleted: boolean
  relics: string[]
  playSeconds: number
  progress: RunProgress
  savedAtUtc: string
  activeActStartRelicChoice: ActStartRelicChoiceDto | null
}

export type MapNodeDto = {
  id: number
  row: number
  column: number
  kind: TileKind
  outgoingNodeIds: number[]
}

export type MapDto = {
  startNodeId: number
  bossNodeId: number
  nodes: MapNodeDto[]
}

export type RunSnapshotDto = {
  run: RunStateDto
  map: MapDto
}

export type BestiaryDto = {
  schemaVersion: number
  discoveredCardBaseIds: string[]
  discoveredRelicIds: string[]
  discoveredPotionIds: string[]
  encounteredEnemyIds: string[]
  allKnownCardBaseIds: string[]
  allKnownRelicIds: string[]
  allKnownPotionIds: string[]
  allKnownEnemyIds: string[]
}

// ===== Phase 10.3-MVP Battle DTOs =====
// 注意: 旧来の `BattleStateDto` は Task 0 で `BattlePlaceholderStateDto` にリネーム済み。
// ここでの新 `BattleStateDto` は Phase 10.3-MVP の本格バトル用。

export type BattlePhase = 'PlayerInput' | 'PlayerAttacking' | 'EnemyAttacking' | 'Resolved'
export type BattleOutcomeKind = 'Pending' | 'Victory' | 'Defeat'
export type ActorSide = 'Ally' | 'Enemy'
export type BattleEventKind =
  | 'BattleStart' | 'TurnStart' | 'PlayCard'
  | 'AttackFire' | 'DealDamage' | 'GainBlock'
  | 'ActorDeath' | 'EndTurn' | 'BattleEnd'
  | 'ApplyStatus' | 'RemoveStatus' | 'PoisonTick'
  | 'Heal' | 'Draw' | 'Discard' | 'Upgrade' | 'Exhaust'
  | 'GainEnergy' | 'Summon' | 'UsePotion'

export type IntentDto = {
  /** 通常攻撃 (single) の予定ダメージ。 */
  attackSingle: number | null
  /** ランダム攻撃 (random) の予定ダメージ。 */
  attackRandom: number | null
  /** 全体攻撃 (all) の予定ダメージ。 */
  attackAll: number | null
  /** 攻撃回数 (effects 中 attack の数)。 */
  attackHits: number | null
  /** block 予定値。 */
  block: number | null
  hasBuff: boolean
  hasDebuff: boolean
  hasHeal: boolean
}

export type CombatActorDto = {
  instanceId: string
  definitionId: string
  side: ActorSide
  slotIndex: number
  currentHp: number
  maxHp: number
  blockDisplay: number
  attackSingleDisplay: number
  attackRandomDisplay: number
  attackAllDisplay: number
  statuses: Record<string, number>
  currentMoveId: string | null
  remainingLifetimeTurns: number | null
  associatedSummonHeldInstanceId: string | null
  intent: IntentDto | null
}

export type BattleCardInstanceDto = {
  instanceId: string
  cardDefinitionId: string
  isUpgraded: boolean
  costOverride: number | null
}

export type BattleEventDto = {
  kind: BattleEventKind
  order: number
  casterInstanceId: string | null
  targetInstanceId: string | null
  amount: number | null
  cardId: string | null
  note: string | null
}

export type BattleStateDto = {
  turn: number
  phase: BattlePhase
  outcome: BattleOutcomeKind
  allies: CombatActorDto[]
  enemies: CombatActorDto[]
  targetAllyIndex: number | null
  targetEnemyIndex: number | null
  energy: number
  energyMax: number
  drawPile: BattleCardInstanceDto[]
  hand: BattleCardInstanceDto[]
  discardPile: BattleCardInstanceDto[]
  exhaustPile: BattleCardInstanceDto[]
  summonHeld: BattleCardInstanceDto[]
  powerCards: BattleCardInstanceDto[]
  comboCount: number
  lastPlayedOrigCost: number | null
  nextCardComboFreePass: boolean
  ownedRelicIds: string[]
  potions: string[]
  encounterId: string
}

export type BattleEventStepDto = {
  event: BattleEventDto
  snapshotAfter: BattleStateDto
}

export type BattleActionResponseDto = {
  state: BattleStateDto
  steps: BattleEventStepDto[]
}

export type EnemyCatalogEntryDto = {
  id: string
  name: string
  imageId: string
  hp: number
  initialMoveId: string
  heightTier: number
}

export type UnitCatalogEntryDto = {
  id: string
  name: string
  imageId: string
  hp: number
  initialMoveId: string
  lifetimeTurns: number | null
  heightTier: number
}

export type CharacterCatalogEntryDto = {
  id: string
  name: string
  maxHp: number
  startingGold: number
  potionSlotCount: number
  heightTier: number
}
