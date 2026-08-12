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
  rerollUsed: boolean       // Phase 10.6.B T7
  rerollAvailable: boolean  // Phase 10.6.B T7: server-derived capability flag
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
  | 'UseCharacterSkill' | 'ComboGaugeChanged'
  | 'ModifyCardCost'
  // Phase 10.5.F: engine 新 actions
  | 'AddCard' | 'RecoverFromDiscard' | 'GainMaxEnergy'

export type IntentDto = {
  /** 通常攻撃 (single) の予定ダメージ。 */
  attackSingle: number | null
  /** ランダム攻撃 (random) の予定ダメージ。 */
  attackRandom: number | null
  /** 全体攻撃 (all) の予定ダメージ。 */
  attackAll: number | null
  /** 全 scope を合計した攻撃回数。 */
  attackHits: number | null
  /** single の攻撃回数。 */
  attackSingleHits: number | null
  /** random の攻撃回数。 */
  attackRandomHits: number | null
  /** all の攻撃回数。 */
  attackAllHits: number | null
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
  /** Card definition cost used exclusively for combo chaining. */
  originalCost: number | null
  /** Authoritative cost payable in the DTO's current zone/context. */
  currentCost: number | null
  /** Compatibility alias: currentCost when it differs from originalCost. */
  costOverride: number | null
  /**
   * Phase 10.5.C: hero (caster) の statuses を反映した description。
   * 戦闘外 (catalog 等) では null。Client は null なら catalog の description にフォールバック。
   */
  adjustedDescription: string | null
  /**
   * Phase 10.5.C: 強化版の context 反映後 description。
   * IsUpgradable でない、または context 不要なら null。
   */
  adjustedUpgradedDescription: string | null
}

export type BattleEventDto = {
  kind: BattleEventKind
  order: number
  casterInstanceId: string | null
  targetInstanceId: string | null
  amount: number | null
  cardId: string | null
  note: string | null
  /** DealDamage 時に Block が実際に吸収した量。 */
  blockedAmount: number | null
  /** 同じ論理ヒットを共有するイベントの 0 始まり番号。 */
  hitIndex: number | null
  /** 攻撃・毒・固定ダメージなど、ダメージ計算ルールの区分。 */
  damageType?: string | null
  /** カード、敵行動、召喚など、この効果を発生させた出所。 */
  source?: CombatSourceDto | null
  /** 反射などで派生した効果を、最初の原因まで遡るための決定的な列。 */
  causeChain?: DamageCauseFrameDto[] | null
  /** Blockと過剰ダメージを除き、対象のHPを実際に減らした量。 */
  actualHpLoss?: number | null
  /** 残りHPを超えた分。 */
  overkill?: number | null
  /** ダメージが0でも、対象へヒット自体が成立したか。 */
  hitLanded?: boolean | null
}

export type CombatSourceDto = {
  kind: string
  /** 所有者が存在しない環境・旧データではnull。プレイヤー0へ誤帰属させない。 */
  ownerSlot: number | null
  ownerInstanceId: string | null
  actorInstanceId: string | null
  definitionId: string | null
  instanceId: string | null
  moveId: string | null
  effectIndex: number | null
  scope: string | null
}

export type DamageCauseFrameDto = {
  depth: number
  damageType: string
  source: CombatSourceDto
  hitIndex: number | null
  cause: string
}

/**
 * Phase 10.5.M2-Choose: choose modal の表示中の保留状態。
 * pile=hand なら手札 UI、それ以外 (draw/discard) はリスト UI で選択させる。
 */
export type PendingChoiceDto = {
  action: 'discard' | 'exhaustCard' | 'upgrade' | 'recoverFromDiscard' | 'modifyCardCost'
  pile: 'hand' | 'draw' | 'discard' | 'exhaust'
  count: number
  candidateInstanceIds: string[]
}

export type PendingCardPlayDto = {
  cardInstanceId: string
  effectIndex: number
  choice: PendingChoiceDto
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
  /** Amount added at the start of each turn; this is not a holding cap. */
  energyPerTurn: number
  drawPile: BattleCardInstanceDto[]
  hand: BattleCardInstanceDto[]
  discardPile: BattleCardInstanceDto[]
  exhaustPile: BattleCardInstanceDto[]
  summonHeld: BattleCardInstanceDto[]
  powerCards: BattleCardInstanceDto[]
  comboCount: number
  maxComboThisTurn: number
  comboGauge: number
  usedCharacterSkillIdsThisTurn: string[]
  lastPlayedOrigCost: number | null
  nextCardComboFreePass: boolean
  ownedRelicIds: string[]
  potions: string[]
  encounterId: string
  /** Phase 10.5.M2-Choose: choose 中なら non-null。modal を表示。 */
  pendingCardPlay: PendingCardPlayDto | null
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
