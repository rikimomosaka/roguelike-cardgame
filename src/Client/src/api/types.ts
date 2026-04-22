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
}

export type EventInstanceDto = {
  eventId: string
  name: string
  description: string
  choices: EventChoiceSnapshotDto[]
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
  | 'Unknown'
  | 'Boss'

export type RunProgress = 'InProgress' | 'Cleared' | 'GameOver' | 'Abandoned'

export type BattleOutcome = 'Pending' | 'Victory'
export type CardRewardStatus = 'Pending' | 'Claimed' | 'Skipped'

export type EnemyInstanceDto = {
  enemyDefinitionId: string
  name: string
  imageId: string
  currentHp: number
  maxHp: number
  currentMoveId: string
}

export type BattleStateDto = {
  encounterId: string
  enemies: EnemyInstanceDto[]
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
}

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
  activeBattle: BattleStateDto | null
  activeReward: RewardStateDto | null
  activeMerchant: MerchantInventoryDto | null
  activeEvent: EventInstanceDto | null
  activeRestPending: boolean
  relics: string[]
  playSeconds: number
  progress: RunProgress
  savedAtUtc: string
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
