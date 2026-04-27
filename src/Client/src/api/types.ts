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
