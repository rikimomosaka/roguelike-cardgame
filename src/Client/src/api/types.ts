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
  voice: number
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
  restHealPreview?: RestHealPreviewDto | null
  hostMemberId?: string
  partyMembers?: RunPartyMemberStateDto[]
}

export type RunPartyMemberStateDto = {
  memberId: string
  slotIndex: number
  characterId: string
  controlMode: 'Human' | 'Cpu'
  currentHp: number
  maxHp: number
  gold: number
  deck: CardInstanceDto[]
  potions: string[]
  potionSlotCount: number
  relics: string[]
  activeReward: RewardStateDto | null
  activeMerchant: MerchantInventoryDto | null
  activeEvent: EventInstanceDto | null
  activeRestPending: boolean
  activeRestCompleted: boolean
  activeActStartRelicChoice: ActStartRelicChoiceDto | null
  restHealPreview: RestHealPreviewDto | null
  discardUsesSoFar: number
}

export type RestHealPreviewDto = {
  currentHpBefore: number
  maxHpBefore: number
  basePercent: number
  baseHealAmount: number
  passiveBonusAmount: number
  currentHpAfter: number
  maxHpAfter: number
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
  playerCharacters: BestiaryCharacterDto[]
  allyCharacters: BestiaryCharacterDto[]
}

export type BestiaryCharacterDto = {
  id: string
  name: string
  kind: 'player' | 'ally'
  maxHp: number
  imageId: string | null
  heightTier: number
  lifetimeTurns: number | null
  isDiscovered: boolean
  description: string
  traitDescription: string
  actions: BestiaryCharacterActionDto[]
  summonCards: BestiarySummonCardDto[]
  characterClass?: import('./catalog').CharacterClass | null
  /** 特性名と書式付き効果文（[K:..]/[S:..] マーカー入り）。 */
  traitName?: string | null
  traitEffects?: string | null
  /** 操作キャラの初期デッキ（カード ID。重複あり）。 */
  deck?: string[] | null
  /** エナジー（ターン開始時の規定値）。 */
  energyPerTurn?: number
  /** ドロー（ターン開始時の枚数）。 */
  drawPerTurn?: number
  attack?: number
  defense?: number
}

export type BestiaryCharacterActionDto = {
  category: string
  name: string
  description: string
  energyCost: number
  comboGaugeCost: number
}

export type BestiarySummonCardDto = {
  id: string
  name: string
  rarity: number
  cardType: string
  cost: number | null
  description: string
  upgradedDescription: string | null
  poolClass?: import('./catalog').CardPoolClass | null
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
  | 'AddCard' | 'RecoverFromDiscard' | 'RecoverFromExhaust' | 'GainMaxEnergy'
  | 'ParticipantReadyChanged' | 'ResolutionStarted'
  | 'ContingencyArmed' | 'ContingencyTriggered' | 'ContingencyResolved'
  | 'FinisherResolved' | 'ReenactCardEffects' | 'CoverBlockSpent'
  | 'CharacterTraitTriggered' | 'Shuffle'
  | 'GainAttack'
  /** カード制御の敵が予約したカードの実行を始めた（cardId = カード定義 ID）。 */
  | 'EnemyCardAction'
  /** キャラの位相が変わった（note = 前->後:理由）。 */
  | 'PhaseChanged'
  /** カードに後付け能力が足された（targetInstanceId = カード、note = キーワード:後の数）。 */
  | 'CardAugmented'
  /** 攪乱で行動（カード 1 枚・技 1 回）が失敗した（casterInstanceId、cardId、amount = 失敗率%）。 */
  | 'ActionFailed'
  /** 先制行動のカードを使った（casterInstanceId、card = 表示用の写し。説明の一行目に [K:opening]）。 */
  | 'OpeningAction'

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
  /** Number of Block.Add calls still contributing Dexterity retroactively. */
  blockAddCount?: number
  /** Raw Block sum before Dexterity; present on current servers. */
  blockRawSum?: number | null
  attackSingleDisplay: number
  attackRandomDisplay: number
  attackAllDisplay: number
  statuses: Record<string, number>
  /** Status ids in acquisition order; newly gained statuses are appended. */
  statusOrder?: string[] | null
  /** 位相（none = なし、calm = 静、motion = 動）。旧サーバーでは省略。 */
  phase?: 'none' | 'calm' | 'motion'
  /** 基礎攻撃力・基礎守備力（筋力・敏捷は含まない）。旧サーバーでは省略。 */
  attackPower?: number
  defensePower?: number
  currentMoveId: string | null
  remainingLifetimeTurns: number | null
  associatedSummonHeldInstanceId: string | null
  intent: IntentDto | null
}

export type BattleCardInstanceDto = {
  instanceId: string
  cardDefinitionId: string
  isUpgraded: boolean
  /** Acquisition class used by the canonical card frame; null for mate/token cards. */
  poolClass?: import('./catalog').CardPoolClass | null
  /** Card definition cost used exclusively for combo chaining. */
  originalCost: number | null
  /** Authoritative cost payable in the DTO's current zone/context. */
  currentCost: number | null
  /** Core判定: このカードの使用でコンボが継続するか。 */
  comboReductionEligible?: boolean
  /** Core判定: このカードを使うとコンボ自体が継続するか。 */
  comboContinues?: boolean
  /** 継続バフを消費せずコンボ継続できる時だけカード外周オーラを表示する。 */
  comboRingHighlighted?: boolean
  wildDisabled?: boolean
  /** True when a per-turn effect limit on this card has been exhausted. */
  cardUsageLimitReached?: boolean
  /** Core判定: 状態変化によって現在このカードを使用できない。 */
  cardPlayRestricted?: boolean
  cardPlayRestrictionReason?: string | null
  /** Ordered Core-authored explanation of every active cost influence. */
  costChanges?: CardCostChangeDto[] | null
  /** True only while this card is armed and waiting in the normal Exhaust pile. */
  isContingencyArmed?: boolean
  /** Authoritative Japanese trigger label, for example `3コンボ達成時`. */
  contingencyTriggerText?: string | null
  /** ターン終了時に手札へ残る（待機・自己保持）。ドロー演出で引き直しと区別する。 */
  retainsAtTurnEnd?: boolean
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
  /** Combo count immediately after this card play, before a Finisher resets it. */
  comboCountAfter?: number | null
  /** DealDamage の対象自身がBlockで吸収した量。護衛者分はCoverBlockSpent。 */
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
  /** Exact effect-only card face for Reprise/Afterimage presentation. */
  card?: BattleReplayCardDto | null
}

export type CardCostChangeDto = {
  kind: 'fixed' | 'modifier' | 'status' | 'combo' | 'unknown'
  /** Replacement cost for fixed; signed delta for every other kind. */
  value: number
  sourceId: string | null
  sourceName: string | null
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
  action:
    | 'discard'
    | 'exhaustCard'
    | 'upgrade'
    | 'recoverFromDiscard'
    | 'recoverFromExhaust'
    | 'modifyCardCost'
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
  characterSkillUsesThisTurn?: Record<string, number>
  lastPlayedOrigCost: number | null
  nextCardComboFreePass: boolean
  ownedRelicIds: string[]
  potions: string[]
  potionUsesThisTurn?: Record<string, number>
  encounterId: string
  /** Phase 10.5.M2-Choose: choose 中なら non-null。modal を表示。 */
  pendingCardPlay: PendingCardPlayDto | null
  /** True while 封印 prevents tactical and ultimate skills. */
  characterSkillsRestricted?: boolean
  /** Monotonic command revision used by participant-aware battle commands. */
  actionRevision?: number
  /** Independent card/resource state for the human and card-controlled allies. */
  participants?: BattleParticipantDto[]
  /** カード制御の敵のカード資源の概要（行動再生の入口・開発確認用）。 */
  enemyParticipants?: EnemyParticipantSummaryDto[]
  /** Latest Finisher record owned by the primary participant, if one is saved. */
  finisherRecord?: FinisherRecordDto | null
  /** The first card after 連環 continues the combo at full cost. */
  comboCarryoverPending?: boolean
  /** このターンに使用したカード（使用順）。プレイ履歴表示用。 */
  playHistoryThisTurn?: FinisherRecordCardDto[]
}

export type FinisherRecordCardDto = {
  /** Stable ordering/key even if the same physical card was reused in one combo. */
  playSequence: number
  card: BattleReplayCardDto
}

export type FinisherRecordDto = {
  afterimageStacks: number
  cards: FinisherRecordCardDto[]
  /** Attacker-side planned values; target mitigation and random outcomes are excluded. */
  plannedAfterimageIntent: IntentDto | null
  /** null when 残光 is inactive; false when Core cannot determine an exact preview. */
  isPlannedAfterimageIntentAvailable: boolean | null
  /** 終撃が保存した記録なら true。false（最後のアタックの通常保存）は UI で強調しない。 */
  savedByFinisher?: boolean
}

export type BattleParticipantControllerKind = 'Human' | 'Cpu'

/** Card/resources owned by one independently controlled combat actor. */
export type EnemyParticipantSummaryDto = {
  participantId: string
  actorInstanceId: string
  energy: number
  handCount: number
  drawPileCount: number
  discardPileCount: number
  exhaustPileCount: number
  /** ターン開始時のエナジーの規定値・ドロー枚数。旧サーバーでは省略。 */
  energyPerTurn?: number
  drawPerTurn?: number
}

export type BattleParticipantDto = {
  participantId: string
  actorInstanceId: string
  controllerKind: BattleParticipantControllerKind
  ownerPlayerSlot: number | null
  characterId: string
  targetAllyIndex: number | null
  targetEnemyIndex: number | null
  energy: number
  energyPerTurn: number
  drawPerTurn: number
  drawPile: BattleCardInstanceDto[]
  hand: BattleCardInstanceDto[]
  discardPile: BattleCardInstanceDto[]
  exhaustPile: BattleCardInstanceDto[]
  summonHeld: BattleCardInstanceDto[]
  powerCards: BattleCardInstanceDto[]
  comboCount: number
  maxComboThisTurn: number
  comboGauge: number
  lastPlayedOrigCost: number | null
  nextCardComboFreePass: boolean
  wildUsedInCurrentCombo: boolean
  usedCharacterSkillIdsThisTurn: string[]
  characterSkillUsesThisTurn: Record<string, number>
  cardUsesThisTurn: Record<string, number>
  cardUseLockedThisTurn: boolean
  ownedRelicIds: string[]
  potions: string[]
  potionUsesThisTurn: Record<string, number>
  pendingCardPlay: PendingCardPlayDto | null
  characterSkillsRestricted?: boolean
  /** Debug-only CPU planning counter; production UI does not expose it. */
  cpuCardPlaysThisTurn: number
  isReady: boolean
  finisherRecord?: FinisherRecordDto | null
  /** The first card after 連環 continues the combo at full cost. */
  comboCarryoverPending?: boolean
  /** このターンに使用したカード（使用順）。プレイ履歴表示用。 */
  playHistoryThisTurn?: FinisherRecordCardDto[]
}

export type BattleReplayDto = {
  fromSequenceExclusive: number
  latestSequence: number
  returnedThroughSequence: number
  turn: number | null
  participantId: string | null
  actorInstanceId: string | null
  feedState: 'live' | 'ready' | 'complete' | string
  isReady: boolean
  isComplete: boolean
  participantFeeds: BattleReplayParticipantFeedDto[]
  entries: BattleReplayEntryDto[]
}

export type BattleReplayParticipantFeedDto = {
  turn: number
  participantId: string
  state: 'live' | 'ready' | 'complete' | string
  isReady: boolean
  isComplete: boolean
  lastSequence: number
}

export type BattleReplayEntryDto = {
  sequence: number
  entryId?: string
  turn: number
  commandId: string
  kind: string
  viewKind: 'action' | 'externalEffect' | 'resolution' | 'feedState' | string
  participantId: string | null
  actorInstanceId: string | null
  revisionBefore: number
  revisionAfter: number
  card: BattleReplayCardDto | null
  targetActorInstanceIds: string[]
  effects: BattleReplayEffectDto[]
  intents: BattleReplayIntentDto[]
  feedChanges: BattleReplayFeedChangeDto[]
  isResolution: boolean
  note: string | null
  /** Combo count immediately after the replayed card decision. */
  comboCountAfter?: number | null
  selectedInstanceIds?: string[]
  ability?: BattleReplayAbilityDto | null
  /** Full decision-time battle presentation; optional for older journals. */
  stateBefore?: BattleStateDto | null
  stateAfter?: BattleStateDto | null
  /** Compact origin retained even when the full source card/ability is hidden. */
  source?: BattleReplaySourceDto | null
}

export type BattleReplaySourceDto = {
  actorInstanceId: string | null
  actorDisplayName: string
  actionKind: string
  actionDefinitionId: string | null
  actionDisplayName: string
}

export type BattleReplayCardDto = {
  cardInstanceId: string
  definitionId: string
  displayName: string
  cardType: string
  rarity: string
  isUpgraded: boolean
  originalCost: number
  currentCost: number
  paidCost: number
  description: string
  costChanges: BattleReplayCardCostChangeDto[]
  poolClass?: import('./catalog').CardPoolClass | null
}

export type BattleReplayCardCostChangeDto = {
  kind: string
  value: number
  sourceId: string | null
  sourceName: string | null
}

export type BattleReplayAbilityDto = {
  definitionId: string
  displayName: string
  kind: string
  description: string
  energyCost: number
  comboGaugeCost: number
  usesBefore: number
  usesPerTurn: number
}

export type BattleReplayEffectDto = {
  kind: string
  sourceActorInstanceId: string | null
  targetParticipantId: string | null
  targetActorInstanceId: string | null
  amount: number | null
  statusId: string | null
  description: string | null
  actualHpLoss?: number | null
  blockedAmount?: number | null
  hitIndex?: number | null
  hitLanded?: boolean | null
  cardDefinitionId?: string | null
  cardInstanceId?: string | null
  /** Decision-time card face captured for ReenactCardEffects. */
  card?: BattleReplayCardDto | null
}

export type BattleReplayIntentDto = {
  kind: string
  scope: string
  amount: number
  hits: number
  targetParticipantId: string | null
  targetActorInstanceId: string | null
}

export type BattleReplayFeedChangeDto = {
  participantId: string
  isReady: boolean | null
  isComplete: boolean | null
}

export type BattleEventStepDto = {
  event: BattleEventDto
  snapshotAfter: BattleStateDto
}

export type BattleActionResponseDto = {
  state: BattleStateDto
  steps: BattleEventStepDto[]
  /** 戦闘開始の応答だけ: 先制行動を使う直前の状態。画面はこれを先に出し、演出の後で state に切り替える。 */
  initialState?: BattleStateDto | null
}

export type EnemyTier = 'Weak' | 'Strong' | 'Elite' | 'Boss'

export type EnemyCatalogEntryDto = {
  id: string
  name: string
  imageId: string
  hp: number
  initialMoveId: string
  heightTier: number
  /** 基礎攻撃力・守備力（筋力・敏捷とは別の能力値） */
  attack?: number
  defense?: number
  initialEnergy?: number
  energyPerTurn?: number
  drawPerTurn?: number
  handLimit?: number
  /** カード制御の敵の通常行動デッキ（旧 Move 敵は null） */
  deck?: UnitDeckEntryDto[] | null
  /** 先制行動（登場時に発動するカード ID、記載順） */
  openingActions?: string[]
  /** 生息地（出現する幕）。 */
  act?: number
  /** ポジション（雑魚・強敵・エリート・ボス）。 */
  tier?: EnemyTier
  /** 図鑑用のキャラクター説明。 */
  description?: string | null
}

export type UnitCatalogEntryDto = {
  id: string
  name: string
  imageId: string
  hp: number
  initialMoveId: string
  lifetimeTurns: number | null
  heightTier: number
  deck?: UnitDeckEntryDto[] | null
  initialEnergy?: number
  energyPerTurn?: number
  drawPerTurn?: number
  handLimit?: number
  aiProfileId?: string | null
  traitDescription?: string | null
  skills?: CharacterSkillCatalogEntryDto[]
  ownedRelicIds?: string[]
  potions?: string[]
  attack?: number
  defense?: number
}

export type UnitDeckEntryDto = {
  cardId: string
  count: number
  upgraded: boolean
  priority: number
}

export type CharacterCatalogEntryDto = {
  id: string
  name: string
  maxHp: number
  startingGold: number
  potionSlotCount: number
  heightTier: number
  skills?: CharacterSkillCatalogEntryDto[]
  /** Pool-aware servers always return these; optional supports older cached responses. */
  characterClass?: 'Striker' | 'Guardian' | 'Tactician' | 'Enhancer'
  deck?: string[]
  description?: string | null
  traitDescription?: string | null
  energyPerTurn?: number
  drawPerTurn?: number
  traitName?: string | null
  /** Marker-bearing passive effect text formatted by the server like a card description. */
  traitEffects?: string | null
  attack?: number
  defense?: number
}

export type CharacterSkillCatalogEntryDto = {
  id: string
  name: string
  energyCost: number
  comboGaugeCost: number
  oncePerTurn: boolean
  usesPerTurn?: number
  description: string
  requiresCardsInHand: boolean
}
