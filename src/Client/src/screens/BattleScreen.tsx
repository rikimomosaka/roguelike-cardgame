// src/Client/src/screens/BattleScreen.tsx
//
// Phase 10.3-MVP Task 14: 本番 API 接続版 BattleScreen。
//
// 旧 demo 実装 (battle-v10.html ポート) の DEFAULT_* と Props ベースの
// データ供給を撤廃し、accountId / onBattleResolved を受け取って
// /battle 系 API を呼び出すコンポーネントに変更。
//
// JSX レイアウト・サブコンポーネント (Relic / Potion / StatusBuff /
// IntentChip / Slot / HandCard) と CSS は demo 版から流用。
// データソースのみ DTO + Catalog → 中間型 (RelicDemo 等) 経由で差し替え。
//
// 中間型 (RelicDemo / CharacterDemo / HandCardDemo / BuffDemo / IntentDemo /
// HpLv) は dtoAdapter から参照できるよう export している。

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import type { CSSProperties, KeyboardEvent, MouseEventHandler, ReactNode } from 'react'
import { Card } from '../components/Card'
import { CardDesc } from '../components/CardDesc'
import type { CardRarity, CardType } from '../components/Card'
import { rarityCodeFromName, rarityCodeFromNumber } from '../components/rarity'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import {
  endTurn,
  createBattleCommandId,
  finalizeBattle,
  getBattle,
  getBattleReplay,
  playCard,
  resolveCardChoice,
  setBattleTarget,
  setBattleReady,
  startBattle,
  usePotion as consumeBattlePotion,
  useTacticalSkill as activateTacticalSkill,
  useUltimateSkill as activateUltimateSkill,
} from '../api/battle'
import type { BattleCommandContext } from '../api/battle'
import type { CardCatalog } from '../api/catalog'
import type {
  ActorSide,
  BattleActionResponseDto,
  BattleCardInstanceDto,
  BattleParticipantDto,
  EnemyParticipantSummaryDto,
  BattleReplayCardDto,
  BattleReplayEntryDto,
  BattleStateDto,
  CombatActorDto,
  FinisherRecordDto,
  RunResultDto,
  RunSnapshotDto,
} from '../api/types'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { useCharacterCatalog } from '../hooks/useCharacterCatalog'
import { useEnemyCatalog } from '../hooks/useEnemyCatalog'
import { useUnitCatalog } from '../hooks/useUnitCatalog'
import { TopBar } from '../components/TopBar'
import {
  BGM_DUCK_PRESETS,
  panForBattleSlot,
  selectBattleSoundRecipe,
  selectCharacterSkillSound,
  useAudio,
  type CharacterSoundClass,
} from '../audio'
import { selectSe } from '../audio/audioManifest'
import {
  DEFAULT_CHARACTER_PORTRAIT,
  resolveCharacterPortrait,
  usePresentationSettings,
} from '../presentation'
import {
  hpLevel,
  toCharacterDemo,
  toHandCardDemo,
  toIntentDemos,
} from './battleScreen/dtoAdapter'
import {
  consumeAnimatedBlock,
  displayAnimatedBlock,
  gainAnimatedBlock,
  readAttackAnimationHit,
  resolveBattleElementCenter,
  updateDisplayedDexterity,
  type AnimatedBlockState,
} from './battleScreen/attackAnimation'
import {
  formatAttackIntentSummary,
  formatAttackScopeLabel,
  toAttackIntentSummarySegments,
} from './battleScreen/intentSummary'
import { CardChoiceModal } from './battleScreen/CardChoiceModal'
import { PlayHistoryModal } from './battleScreen/FinisherRecordModal'
import { pickTargetAtPoint, readTargetRegions } from './battleScreen/targetHitTest'
import {
  DisruptionHalo,
  disruptionHaloStyle,
  PhaseAura,
  phaseRingStyle,
  slotTooltipContent,
  type ActorPhaseDemo,
} from './battleScreen/PhaseMarks'
import { characterSkillCutinDurationMs } from './battleScreen/characterSkillCutinPresentation'
import {
  formatComboDescription,
  formatMaxComboThisTurn,
} from './battleScreen/comboPresentation'
import {
  ENERGY_CARRYOVER_DESCRIPTION,
  formatEnergyOrbValue,
  formatEnergyTooltipStatusLine,
  formatEnergyTooltipTitle,
} from './battleScreen/energyPresentation'
import {
  clampComboGauge,
  COMBO_GAUGE_DESCRIPTION,
  COMBO_GAUGE_MAX,
  getComboSkillAvailability,
  TACTICAL_SKILL_COST,
  TACTICAL_SKILL_DESCRIPTION,
  ULTIMATE_SKILL_COST,
  ULTIMATE_SKILL_DESCRIPTION,
} from './battleScreen/comboSkillPresentation'
import {
  findParticipantForActor,
  findPrimaryParticipant,
  projectParticipantForDisplay,
  placeActorsBySlot,
  replayEntryToActionResponse,
  replayPresentationAtEntryStart,
  replayPresentationAtPosition,
  snapshotActor,
  snapshotBattleState,
  snapshotParticipant,
} from './battleScreen/participantPresentation'
import {
  battleAudioCuesForEvent,
  battleContactSoundRecipe,
  battleSoundRecipeForEvent,
  bgmDuckPresetForEvent,
  comboToneTier,
  statusAudioCue,
  type BattleAudioCue,
} from './battleScreen/battleAudioCues'
import {
  BattleReplayControls,
  type ReplaySpeed,
} from './battleScreen/BattleReplayControls'
import {
  CombatFeedbackStack,
  type CombatFeedbackItem,
  type CombatFeedbackKind,
} from './battleScreen/CombatFeedbackLayer'
import {
  appendCombatFeedback,
  blockGainFeedbackText,
  combatFeedbackLifetimeMs,
} from './battleScreen/combatFeedbackTiming'
import {
  actorSizeClassForTier,
  resolveActorImpactAnchorY,
  resolveActorStageProfile,
  type ActorStage,
} from './battleScreen/actorVisualManifest'
import {
  normalizeMotionPlaybackSpeed,
  scaledOneShotMotionDuration,
} from './battleScreen/motionPresentation'
import {
  hasDrawPresentationTrigger,
  hasShufflePresentationTrigger,
} from './battleScreen/drawPresentation'
import {
  hasInlineDrawEvent,
  planInlineDraws,
  type DrawPileState,
  type InlineDrawAction,
} from './battleScreen/inlineDrawPlan'
import {
  resolveFeedbackStatusId,
  statusFeedbackText,
} from './battleScreen/statusFeedback'
import { attackGainFeedbackText } from './battleScreen/attackGainFeedback'
import { HEROES_WITH_OWN_UI, battleIcon, heroUi, intentIcon, statusIcon, type HeroUiPart } from '../presentation/assets'
import './BattleScreen.css'

// -------------------- Exported intermediate types --------------------

export type HpLv = '0' | '1' | '2' | '3'

export type RelicDemo = {
  icon: string
  rarity: CardRarity
  name: string
  desc: string
}

export type PotionDemo = {
  icon: string
  rarity?: CardRarity
  name?: string
  desc?: string
  empty?: boolean
  onClick?: () => void
}

export type BuffKind = 'block' | 'buff' | 'debuff'

export type BuffDemo = {
  kind: BuffKind
  icon: string
  num: number
  name: string
  desc: string
}

export type IntentKind = 'attack' | 'defend' | 'buff' | 'heal' | 'unknown'

/** 通常/ランダム/全体の攻撃予定を、種類ごとの独立した行へ投影するための内訳。 */
export type IntentAttackBreakdown = {
  single?: number
  random?: number
  all?: number
  hits?: number
  singleHits?: number
  randomHits?: number
  allHits?: number
  total?: number
}

export type IntentDemo = {
  kind: IntentKind
  icon: string
  num?: number
  name: string
  desc: string
  /** kind='attack' のとき per-scope 内訳 (色分け表示用)。 */
  attack?: IntentAttackBreakdown
}

export type SpriteKind = 'hero' | 'ally' | 'enemy' | 'elite'

/** Character animation state. New sprite sheets can be added without changing Slot markup. */
export type CharacterMotion = 'idle' | 'attack' | 'attackAlt' | 'skill' | 'guard' | 'cast' | 'hurt' | 'down' | 'still'

/** 攻撃時に使う攻撃モーション。attackAlt が無いキャラは常に attack を使う。 */
export type AttackMotion = 'attack' | 'attackAlt'

/** Metadata for a horizontal, equally-sized sprite sheet. */
export type SpriteSheetDemo = {
  src: string
  frameWidth: number
  frameHeight: number
  frameCount: number
  durationMs: number
  /** Idle usually loops; one-shot attack/hurt sheets stop on their final valid frame. */
  loop: boolean
  /**
   * 表示高さ（tier の高さ）に対応するフレームの高さ。省略時は frameHeight。
   * frameHeight の方が大きいシートは、足元をそろえたまま上にはみ出して描く（剣を振り上げる奥義など）。
   */
  baseFrameHeight?: number
}

export type CharacterDemo = {
  occupied: boolean
  /** Stable battle identity used only to anchor presentation overlays. */
  instanceId?: string
  name: string
  desc: string
  sprite: string
  spriteKind: SpriteKind
  /** PNG 等の立ち絵 URL。指定時は text sprite ではなく <img> で描画する。 */
  image?: string
  /** Motion-specific sheets. `image` remains the static loading/error fallback. */
  animations?: Partial<Record<CharacterMotion, SpriteSheetDemo>>
  /** 位相（静・動）。位相なしは undefined。 */
  phase?: ActorPhaseDemo
  /** 攪乱の数（0 = なし）。頭上の混乱の輪と、行動失敗の表示に使う。 */
  disruption?: number
  /** キャラのサイズ段階 (1 〜 10)。1 = スライム級の小型、5 = 標準、10 = 巨大ボス級。
   *  立ち絵 image 表示時は heightTier に応じて高さが決定される (縦横比は維持)。 */
  heightTier?: number
  /**
   * 新規格（128×128 共通キャンバス）のキャラの表示寸法。あるときは heightTier より優先し、
   * 全キャラ同じ倍率で表示する（大きさは絵の中に描かれている）。
   */
  stage?: ActorStage
  /** 主人公の ID（キャラ専用の戦闘UIを選ぶ）。主人公以外は undefined。 */
  uiHeroId?: string
  /** Playable-character passive shown when hovering the character sprite. */
  traitName?: string
  /** Marker-bearing passive text rendered with the common card tooltip. */
  traitDescription?: string
  hpCur: number
  hpMax: number
  hpLv: HpLv
  /** Why: ブロック値は buff/debuff とは別に HP バー右端で表示する。
   *  0 のときは UI を出さない。 */
  blockValue: number
  /** 単一の intent (敵の次行動)。intents が無いときの後方互換。 */
  intent?: IntentDemo
  /** 複数チップ表示 (hero の通常/ランダム/全体予定攻撃を分けて表示する用)。 */
  intents?: IntentDemo[]
  buffs: BuffDemo[]
}

export type HandCardDemo = {
  /** Replay cards retain their source instance so the hand can hide them once played. */
  instanceId?: string
  name: string
  cost: number | string
  comboReductionEligible?: boolean
  comboContinues?: boolean
  comboRingHighlighted?: boolean
  wildDisabled?: boolean
  cardUsageLimitReached?: boolean
  costUnavailable?: boolean
  /** 現在コストと異なる、コンボ参照用の元コスト。変更なしは null/undefined。 */
  costOrig?: number | null
  /** Original definition cost retained for the hover breakdown even when unchanged. */
  costOriginal?: number | null
  costChanges?: BattleCardInstanceDto['costChanges']
  /** Armed 伏策 presentation, normally populated only for Exhaust cards. */
  isContingencyArmed?: boolean
  contingencyTriggerText?: string | null
  type: CardType
  rarity: CardRarity
  poolClass?: import('../api/catalog').CardPoolClass | null
  /** カード定義 ID（イラストの解決用）。 */
  cardId?: string
  art?: ReactNode
  playable?: boolean
  unavailableReason?: string
  desc: string
  /** 強化済みフラグ。Card 側で "+" を描画。 */
  upgraded?: boolean
  /** 通常版テキスト。右クリック長押しで強化版と切替表示するため両方保持。 */
  description?: string
  /** 強化版テキスト (未定義カードでは null/undefined)。 */
  upgradedDescription?: string | null
}

// -------------------- Props --------------------

type Props = {
  accountId: string
  /** ラン全体の最新 snapshot。TopBar (gold/playSeconds/deck/relics) の表示用。 */
  snapshot: RunSnapshotDto
  onBattleResolved: (result: RunSnapshotDto | RunResultDto) => void
  /** TopBar の MAP ボタン押下で BattleScreen → MapScreen へ peek 切替する。 */
  onTogglePeek?: () => void
  /** Why: peek 中も親が live battle state を TopBar に表示できるよう、state
   *  更新ごとに親へ通知する。null は battle 終了 (clear) を意味する。 */
  onBattleStateChange?: (state: BattleStateDto | null) => void
  /** TopBar メニューボタン / ESC でゲーム内メニューを開閉する。
   *  state は MapScreen が保持する (戦闘中 abandon/exit のフローを共有するため)。 */
  menuOpen?: boolean
  onOpenMenu?: () => void
}

// -------------------- Animation timing --------------------

const STEP_DELAY_MS = 80         // 既存 fallback (汎用 event)
/** 敵が使ったカード名を読ませる間。 */
const ENEMY_CARD_ACTION_READ_MS = 650
/** 攪乱で行動が失敗したときに「失敗！」を読ませる間。 */
const ACTION_FAILED_READ_MS = 700
/** 毒のダメージ（緑の数字と光）を見せる間。 */
const POISON_TICK_READ_MS = 520
/** 敵によるバフ・デバフ・ブロック・回復は発光が見えるまで少し待つ。 */
const ENEMY_SUPPORT_EFFECT_MS = 320
// Readability floors used when an actor has no authored motion duration.
// A valid manifest duration takes precedence, and the slot keyframes receive
// that same value so the sheet and physical lunge always finish together.
// Contact deliberately lands before halfway, leaving a readable recovery.
const ATTACK_SLIDE_MS = 440      // minimum anticipation -> lunge -> recovery
const ATTACK_CONTACT_MS = 180    // hit feedback is emitted at the lunge apex
const HIT_FLASH_MS = 420         // minimum red flash + recoil
// 手札カード exit (捨札方向に縮小フェード / 喪失行きは中央拡大→回転消滅)。
// M6.9: 喪失アニメは 700ms (中央移動 + ホールド + 回転消滅) なので長い方に合わせる。
//  短い discard アニメ (260ms) は完了後に DOM が残るがレイアウト的に害無し
//  (.is-leaving は pointer-events: none)。
const CARD_LEAVE_MS = 700
const CARD_PLAY_CENTER_MS = 360
const CARD_PLAY_DISCARD_MS = 420
const CARD_PLAY_EXHAUST_MS = 460
const REENACT_APPEAR_MS = 140
const REENACT_HOLD_MS = 260
const REENACT_LEAVE_MS = 160
// Why: 死亡アニメーションは廃止 (ユーザ要望)。HP=0 で alive filter から外れて
// 自然消滅するため、後追い blink は入れない。
// 例外: down シートを持つキャラ（新規格で作ったキャラ）は HP 0 でも残り、倒れる姿勢で止まる。
/** 主人公専用の戦闘操作UIを CSS 変数で渡す（専用UIが無い主人公は共通の枠のまま）。 */
function heroUiStyle(heroId: string | undefined): CSSProperties | undefined {
  if (!heroId || !HEROES_WITH_OWN_UI.has(heroId)) return undefined
  const parts: readonly HeroUiPart[] = ['combo', 'tactical', 'ultimate', 'ready', 'energy']
  return Object.fromEntries(parts.map(part => [`--hero-ui-${part}`, `url('${heroUi(heroId, part)}')`])) as CSSProperties
}

/**
 * 新規格のキャラのキャンバス下端（スロット上端からの y）。CSS の .sprite--stage と合わせる。
 * キャンバスの足元の下には 4 ドット（×2 = 8px）の余白があるので、影の中心 y=190 に足が乗るよう
 * キャンバス下端を 198 に置く（slot 高 260 − bottom 62）。
 */
const STAGE_CANVAS_BOTTOM_Y = 198
/** 奥義モーションで技を放つ位置（8 コマ中 5 コマ目の頭）。 */
const SKILL_RELEASE_RATIO = 5 / 8
/** カードに能力（双極など）が付くときの光の長さ。半分の時点で付与後の見た目に差し替える。 */
const CARD_AUGMENT_GLOW_MS = 800
/**
 * スキルカードのモーションで、効果を出すまで待つ割合。
 * guard: 構えが固まる 2/6 コマ目でブロックを付ける。cast: 刀に力が宿る 3/6 コマ目で効果を出す。
 */
const SKILL_CARD_MOTION_RELEASE_RATIO: Readonly<Record<CardMotion, number>> = {
  guard: 2 / 6,
  cast: 3 / 6,
  attack: 3 / 6,
}
/** 伏策が発動したときに、中央のカードを見せる追加の時間（判子と光を読ませる）。 */
const CONTINGENCY_TRIGGER_EXTRA_MS = 420
/** 伏策の発動条件（Core の ContingencyTriggerTiming）→ 判子の下に出す言葉。 */
const CONTINGENCY_TIMING_LABEL: Readonly<Record<string, string>> = {
  TurnStart: 'ターン開始時',
  BeforeEnemyAttack: '相手の攻撃時',
  ComboReached: 'コンボ達成時',
}
/** ダウンモーションの後、敗北処理へ進む前に倒れた姿を見せる時間。 */
const DOWN_HOLD_MS = 500
/** 勝敗が決まってから、勝利の表示・結果画面へ進むまでの間（倒れた側を見せる）。 */
const OUTCOME_HOLD_MS = 1000
/** 倒れる姿勢を持たないキャラが、HP 0 になってから点滅して消えるまでの時間（CSS の is-dying と合わせる）。 */
const DEATH_BLINK_MS = 720

function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

/** Why: attack/hit アニメーション中の slot を className で識別するため
 *  の transient state。
 *  - attackerId: 攻撃中の slot (1 つ、slide 中)
 *  - hitIds: 同じ攻撃 burst で被弾した slot 群 (全体攻撃で複数同時 flash)
 *  死亡演出はユーザ要望で廃止 (HP=0 即フィルタで自然消滅)。 */
type SlotAnim = {
  attackerId?: string
  attackerSide?: 'Ally' | 'Enemy'
  /** 攻撃者が使う攻撃モーション（同じキャラの攻撃ごとに A/B を交互）。 */
  attackMotion?: AttackMotion
  /** 戦技・奥義の発動モーションを再生中のキャラ。 */
  skillCasterId?: string
  /** スキルカードを使い、そのモーション（防御 / それ以外）を再生中のキャラ。 */
  cardMotion?: { casterId: string; motion: CardMotion }
  hitIds?: ReadonlyArray<string>
  blockedIds?: ReadonlyArray<string>
  playbackSpeed?: number
}

// -------------------- Layout helpers --------------------

// Fan layout for the player's hand. Values ported from battle-v10.html feel.
function fanLayout(count: number): { x: number; y: number; r: number }[] {
  if (count === 0) return []
  const step = 60 // px horizontal step
  const center = (count - 1) / 2
  const rotStep = 7 // degrees
  return Array.from({ length: count }, (_, i) => {
    const offset = i - center
    const x = offset * step
    const rot = offset * rotStep
    const y = Math.abs(offset) * 6
    return { x, y, r: rot }
  })
}

/**
 * Why: キャラ立ち絵の高さ段階 (1〜10) → px 換算。
 * tier 1 = スライム級小型 (60px), tier 5 = 標準 (160px),
 * tier 10 = 巨大ボス級 (260px)。線形に約 22px ずつ増加。
 * 縦横比は <img> 自身が維持するので width は auto。
 */
function heightForTier(tier: number): number {
  const clamped = Math.max(1, Math.min(10, Math.round(tier)))
  return 38 + clamped * 22  // 1→60, 5→148, 10→258
}

function emptyCharacterSlot(): CharacterDemo {
  return {
    occupied: false,
    name: '',
    desc: '',
    sprite: '',
    spriteKind: 'enemy',
    hpCur: 0,
    hpMax: 0,
    hpLv: '0',
    blockValue: 0,
    buffs: [],
  }
}

type CharacterSkillBurst = {
  id: number
  kind: 'tactical' | 'ultimate'
  characterId: string
  characterClass: CharacterSoundClass
  name: string
  portrait: string
  durationMs: number
}

function toCharacterSoundClass(value: string | undefined): CharacterSoundClass {
  switch (value?.toLowerCase()) {
    case 'guardian': return 'Guardian'
    case 'tactician': return 'Tactician'
    case 'enhancer': return 'Enhancer'
    default: return 'Striker'
  }
}

function padSlots(chars: Array<CharacterDemo | null>, n: number): CharacterDemo[] {
  const out = chars.slice(0, n).map(character => character ?? emptyCharacterSlot())
  while (out.length < n) {
    out.push(emptyCharacterSlot())
  }
  return out.slice(0, n)
}

// -------------------- Small sub-components --------------------

function useTip(c: TooltipContent | null) {
  return useTooltipTarget(c)
}

// Relic / Potion 内部コンポーネントは battle__hud 撤去 (TopBar 統合) で
// 不要になったため削除。RelicDemo / PotionDemo 型は他から参照されていれば残す。

function StatusBuff({ buff }: { buff: BuffDemo }) {
  const tip = useTip({ name: buff.name, desc: buff.desc, suppressRefs: true })
  const isImageIcon = buff.icon.startsWith('/')
  return (
    <span className={`status-buff status-buff--${buff.kind}`} {...tip}>
      {isImageIcon ? (
        <img className="status-buff__icon-img" src={buff.icon}
             alt="" draggable={false} />
      ) : (
        buff.icon
      )}
      <span className="status-buff__num">{buff.num}</span>
    </span>
  )
}

/** Why: ブロック値を HP バー右端で表示する専用コンポーネント。
 *  buff/debuff と異なり、数字はアイコン中央に大きめに重ねる仕様 (ユーザ要望)。 */
function StatusBlock({ value }: { value: number }) {
  const tip = useTip({ name: 'ブロック', desc: `ダメージを${value}軽減する。` })
  return (
    <div className="status-block" {...tip}>
      <img className="status-block__icon-img"
           src={statusIcon('block')}
           alt="" draggable={false} />
      <span className="status-block__num">{value}</span>
    </div>
  )
}

/** Why: HP バー幅 (100px) に収まるよう、長い名前は font-size を縮める。
 *  max-width: 12ch でのトランケート (...) は Japanese full-width に弱く
 *  「ケーブ・…」のように短い名前まで切れていたため廃止。 */
function StatusName({ name }: { name: string }) {
  const ref = useRef<HTMLDivElement>(null)
  const [fontSize, setFontSize] = useState(12)
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    // Measure scrollWidth at baseline; if overflow, scale down proportionally.
    el.style.fontSize = '12px'
    const sw = el.scrollWidth
    const containerWidth = 100  // matches HP bar / slot width
    if (sw > containerWidth) {
      const next = Math.max(7, Math.floor(12 * containerWidth / sw))
      setFontSize(next)
    } else {
      setFontSize(12)
    }
  }, [name])
  return (
    <div
      ref={ref}
      className="status-name"
      style={{ fontSize: `${fontSize}px` }}
      title={name}
    >
      {name}
    </div>
  )
}

/** 単一 intent の中身 (icon + 値群)。tooltip 用 props は外から渡される。 */
function IntentSegment({
  intent,
  tipProps,
}: {
  intent: IntentDemo
  tipProps: ReturnType<typeof useTooltipTarget>
}) {
  const isImageIcon = intent.icon.startsWith('/')

  const renderIcon = () => (isImageIcon ? (
    <img className="intent__icon intent__icon--img" src={intent.icon}
         alt="" draggable={false} />
  ) : (
    <span className="intent__icon">{intent.icon}</span>
  ))

  const renderAttackScopeIcon = (scope: 'single' | 'random' | 'all') => {
    const src = scope === 'all'
      ? intentIcon('attack_all')
      : scope === 'random'
        ? intentIcon('attack_random')
        : intentIcon('attack')
    return <img className="intent__icon intent__icon--img" src={src} alt="" draggable={false} />
  }

  if (intent.kind === 'attack' && intent.attack) {
    const segments = toAttackIntentSummarySegments(intent.attack)
    return (
      <span className="intent__seg intent__seg--attack intent__seg--attack-scopes" {...tipProps}>
        <span className="intent__nums">
          {segments.map(segment => (
            <span className="intent__scope-row" key={segment.scope}>
              {renderAttackScopeIcon(segment.scope)}
              <span
                className="intent__scope-summary"
                data-attack-summary={formatAttackIntentSummary(segment)}
              >
                <span className={`intent__num intent__num--${segment.scope}`}>
                  {segment.total}
                </span>
                <span className="intent__scope-meta">
                  <span
                    className={`intent__scope-label intent__scope-label--${segment.scope}`}
                  >
                    {formatAttackScopeLabel(segment.scope)}
                  </span>
                  <span className="intent__hit-count">({segment.hits}HIT)</span>
                </span>
              </span>
            </span>
          ))}
        </span>
      </span>
    )
  }

  return (
    <span className={`intent__seg intent__seg--${intent.kind}`} {...tipProps}>
      {renderIcon()}
      {intent.num !== undefined ? (
        <span className="intent__num">{intent.num}</span>
      ) : null}
    </span>
  )
}

function IntentChip({ intent }: { intent: IntentDemo }) {
  const tip = useTip({ name: intent.name, desc: intent.desc })
  return (
    <div className={`intent intent--${intent.kind}`}>
      <IntentSegment intent={intent} tipProps={tip} />
    </div>
  )
}

/** Why: 複数 intent (攻撃 + 防御 など) を 1 行動につき 1 行で並べる。
 *  - 攻撃内の Single / Random / All もそれぞれ独立した行にする
 *  - hover でどの segment に当てても全 intent の name/desc を統合した
 *    tooltip を表示 (片方だけでなく両方の説明を見たい、というユーザ要望) */
export function IntentChipRow({ intents }: { intents: IntentDemo[] }) {
  // Why: tooltip 重複削除。タイトルは ＆ 区切り、本文は intent ごとに ■ + desc
  // のみ (■ の後の name 重複を廃止)、複数 intent は改行で並べる。
  const mergedName = intents.map(i => i.name).join(' ＆ ')
  const mergedDesc = intents.map(i => `■ ${i.desc}`).join('\n')
  const tip = useTip({ name: mergedName, desc: mergedDesc })
  return (
    <div className={`intent intent--row${intents.length > 1 ? ' intent--multi' : ''}`}>
      {intents.map((it, i) => (
        <IntentSegment key={i} intent={it} tipProps={tip} />
      ))}
    </div>
  )
}

type SlotProps = {
  char: CharacterDemo
  isTargeted?: boolean
  /** Why: 同サイドの occupied slot が 1 だけ (= 選択肢が無い) のとき、
   *  ターゲティング三角形 + sprite glow を描かない。内部の isTargeted
   *  state は維持して、視覚だけ抑制する (ユーザ要望)。 */
  showTargetVisual?: boolean
  /** Why: 攻撃中 / 被弾中の演出クラス付与用 (死亡演出は廃止)。 */
  attackingDir?: 'toward-enemy' | 'toward-ally'
  /** 攻撃中に使う攻撃モーション（A/B の交互）。省略時は attack。 */
  attackMotion?: AttackMotion
  /** 戦技・奥義の発動モーション中。 */
  isSkill?: boolean
  /** スキルカードのモーション中（防御効果あり = guard、それ以外 = cast）。 */
  cardMotion?: CardMotion | null
  isHit?: boolean
  isBlocked?: boolean
  /** 撃破された直後（点滅して消える）。 */
  isDying?: boolean
  /** Replay speed applied only while this slot is in a one-shot motion. */
  motionPlaybackSpeed?: number
  onClick?: () => void
  /** Opens this ally's participant action feed without changing battle targeting. */
  onViewActions?: () => void
  replayFeedState?: 'live' | 'ready' | 'complete'
  participantReady?: boolean
  feedbackItems?: readonly CombatFeedbackItem[]
  /** 敵がこの瞬間に使っているカード名（頭上に札として表示）。 */
  actionLabel?: { id: number; text: string } | null
  /** バフ・デバフ・ブロック・回復を受けた瞬間の発光エフェクト。 */
  aura?: { id: number; kind: SlotAuraKind } | null
}

type SlotAuraKind = 'buff' | 'debuff' | 'block' | 'heal' | 'poison'

/**
 * 敵の行動再生用の参加者表現。敵の手札の中身は公開しないため、
 * 再生に必要な識別子とエナジーだけを持つ（計画はターン開始時に完了済み＝Ready）。
 */
function enemyReplayParticipant(summary: EnemyParticipantSummaryDto): BattleParticipantDto {
  return {
    participantId: summary.participantId,
    actorInstanceId: summary.actorInstanceId,
    controllerKind: 'Cpu',
    ownerPlayerSlot: null,
    characterId: '',
    targetAllyIndex: null,
    targetEnemyIndex: null,
    energy: summary.energy,
    energyPerTurn: 0,
    drawPerTurn: 0,
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

type BattleReplayViewState = {
  /** Frozen fallback for legacy entries that predate decision snapshots. */
  battle: BattleStateDto
  participant: BattleParticipantDto
  actor: CombatActorDto
  subjectName: string
  /** The replay feed is immutable across live turn transitions. */
  turn: number
  entries: BattleReplayEntryDto[]
  feedState: string
  latestSequence: number
  position: number
  playing: boolean
  speed: ReplaySpeed
  loading: boolean
  /** Forces a fresh playback even when restart is pressed at position zero. */
  playbackEpoch: number
  /** True while playback is parked at the append-only live/ready tail. */
  followTail: boolean
}

type ReplayActionCardState = {
  demo: HandCardDemo
  stage: 'idle' | 'centering' | 'holding' | 'leaving'
  /** .app-stage (1280×720) ローカル座標。手札位置 → 中央 → 捨札。 */
  startX: number
  startY: number
  destX: number
  destY: number
}

type ReenactCardState = {
  id: string
  demo: HandCardDemo
  stage: 'appearing' | 'holding' | 'leaving'
  movement?: 'popup'
    | 'contingency-discard'
    | 'contingency-exhaust'
    | 'contingency-draw'
    | 'contingency-hand'
    | 'contingency-none'
  fromX?: number
  fromY?: number
  toX?: number
  toY?: number
  /** 伏策の発動: 判子の下に出す発動条件（例: 相手の攻撃時）。空文字なら条件は出さない。 */
  contingencyTrigger?: string
  /** 伏策の発動: 判子に転記する、発動した効果の説明文（伏策条件の行より後）。 */
  contingencyEffect?: string
}

/** カードの説明文から、伏策条件の行より後（発動した時に実行される効果）を取り出す。 */
export function contingencyEffectText(description: string | null | undefined): string {
  const lines = (description ?? '').replace(/\\n/g, '\n').split('\n')
  const at = lines.findIndex(line => line.includes('[K:contingency]'))
  return (at >= 0 ? lines.slice(at + 1) : lines).filter(line => line.trim() !== '').join('\n')
}

type PendingContingencyCard = {
  card: BattleCardInstanceDto
  destination: 'discard' | 'exhaust'
}

function battleCardDescription(
  card: BattleCardInstanceDto,
  catalog: CardCatalog | null,
): string {
  const definition = catalog?.[card.cardDefinitionId]
  return card.isUpgraded
    ? card.adjustedUpgradedDescription
      ?? card.adjustedDescription
      ?? definition?.upgradedDescription
      ?? definition?.description
      ?? ''
    : card.adjustedDescription ?? definition?.description ?? ''
}

function battleCardDestination(
  card: BattleCardInstanceDto,
  catalog: CardCatalog | null,
): 'discard' | 'exhaust' {
  const description = battleCardDescription(card, catalog)
  return /\[K:(?:exhaust|contingency)\]/.test(description)
    ? 'exhaust'
    : 'discard'
}

function findPendingContingencyCard(
  battle: BattleStateDto,
  catalog: CardCatalog | null,
): PendingContingencyCard | null {
  const owners = [
    {
      pendingCardPlay: battle.pendingCardPlay,
      exhaustPile: battle.exhaustPile,
    },
    ...(battle.participants ?? []).map(participant => ({
      pendingCardPlay: participant.pendingCardPlay,
      exhaustPile: participant.exhaustPile,
    })),
  ]

  for (const owner of owners) {
    const pendingId = owner.pendingCardPlay?.cardInstanceId
    if (!pendingId) continue
    const card = owner.exhaustPile.find(candidate => candidate.instanceId === pendingId)
    if (!card) continue
    const description = battleCardDescription(card, catalog)
    if (!/\[K:contingency\]/.test(description)) continue
    return {
      card,
      destination: /\[K:exhaust\]/.test(description) ? 'exhaust' : 'discard',
    }
  }

  return null
}

type PlayStepsOptions = {
  /** Replay uses its immutable before-state instead of the changing live state. */
  baselineState?: BattleStateDto
  /** Never commits/finalizes live battle state; animations only. */
  presentationOnly?: boolean
  speed?: number
  isCancelled?: () => boolean
}

type ActiveCharacterAnimation = {
  motion: CharacterMotion
  sheet: SpriteSheetDemo
}

type CharacterMotionFlags = {
  /** 攻撃中なら使う攻撃モーション。攻撃していなければ null。 */
  attack: AttackMotion | null
  isHit: boolean
  isSkill: boolean
  cardMotion: CardMotion | null
  /** 静の位相: 待機・被弾・奥義の動きをせず、目を閉じたコマ（still）で止まる。 */
  isStill: boolean
  /** HP 0 で倒れている（down シートを持つキャラだけ表示が残る）。 */
  isDown: boolean
}

/** スキルカードを使ったときのモーション。 */
export type SkillCardMotion = 'guard' | 'cast'
/** カードの使用・伏策の発動で再生するモーション（伏策の発動で攻撃を得たときは attack）。 */
export type CardMotion = SkillCardMotion | 'attack'

/** このカード使用（次のカード使用まで）で、使ったキャラが伏策を仕込んだか。 */
export function playedCardArmsContingency(
  steps: ReadonlyArray<{ event: { kind: string; casterInstanceId: string | null } }>,
  playCardIndex: number,
  casterId: string,
): boolean {
  for (let j = playCardIndex + 1; j < steps.length; j++) {
    const ev = steps[j].event
    if (ev.kind === 'PlayCard') return false
    if (ev.kind === 'ContingencyArmed' && ev.casterInstanceId === casterId) return true
  }
  return false
}

/**
 * 伏策が発動したときのモーションを、発動から解決までの出来事で決める。
 * - その場で攻撃が当たる: null（攻撃モーションに任せる）
 * - 攻撃を得る: attack
 * - ブロックを得る: guard
 * - それ以外: cast
 */
export function contingencyEffectMotion(
  steps: ReadonlyArray<{ event: { kind: string; casterInstanceId: string | null; amount: number | null } }>,
  triggerIndex: number,
  casterId: string,
): CardMotion | null {
  let gainsAttack = false
  let gainsBlock = false
  for (let j = triggerIndex + 1; j < steps.length; j++) {
    const ev = steps[j].event
    if (ev.kind === 'ContingencyResolved' || ev.kind === 'ContingencyTriggered' || ev.kind === 'PlayCard') break
    if (ev.kind === 'AttackFire' && ev.casterInstanceId === casterId) return null
    if (ev.kind === 'GainAttack' && ev.casterInstanceId === casterId && (ev.amount ?? 0) > 0) gainsAttack = true
    if (ev.kind === 'GainBlock' && ev.casterInstanceId === casterId && (ev.amount ?? 0) > 0) gainsBlock = true
  }
  return gainsAttack ? 'attack' : gainsBlock ? 'guard' : 'cast'
}

/**
 * スキルカードの解決中（次のカード使用まで）の出来事から、使ったキャラのモーションを決める。
 * - 使ったキャラが攻撃する: null（攻撃モーションに任せる）
 * - 使ったキャラがブロックを付与する: guard（防御モーション）
 * - それ以外: cast（発動モーション）
 */
export function skillCardMotion(
  steps: ReadonlyArray<{ event: { kind: string; casterInstanceId: string | null; amount: number | null } }>,
  playCardIndex: number,
  casterId: string,
): SkillCardMotion | null {
  let gainsBlock = false
  for (let j = playCardIndex + 1; j < steps.length; j++) {
    const ev = steps[j].event
    if (ev.kind === 'PlayCard') break
    if (ev.kind === 'AttackFire' && ev.casterInstanceId === casterId) return null
    if (ev.kind === 'GainBlock' && ev.casterInstanceId === casterId && (ev.amount ?? 0) > 0) gainsBlock = true
  }
  return gainsBlock ? 'guard' : 'cast'
}

function readableMotionDuration(
  sheet: SpriteSheetDemo | undefined,
  minimumMs: number,
): number {
  const authored = sheet?.durationMs
  return Number.isFinite(authored) && (authored ?? 0) > 0
    ? Math.max(minimumMs, Math.round(authored!))
    : minimumMs
}

/**
 * Pick the highest-priority animation that is both active and usable.
 * Failed sheets are skipped so a lower-priority motion (ultimately the static image)
 * can take over without an interval or another network request loop.
 */
/**
 * 再生候補のモーション（優先順）。
 * 優先度: ダウン > 被弾 > 奥義 > スキルカード（防御 / 発動）> 攻撃（指定の型 → 基本の attack）> 待機。
 * 静の位相では被弾・奥義・待機の動きをせず、目を閉じたコマ（still）で止まる。
 * 攻撃の実行・ダウン・静を解くカードのモーション（cardMotion は呼び出し側で絞り込み済み）は再生する。
 */
export function characterMotionCandidates(flags: CharacterMotionFlags): CharacterMotion[] {
  const candidates: CharacterMotion[] = []
  if (flags.isDown) candidates.push('down')
  if (flags.isHit && !flags.isStill) candidates.push('hurt')
  if (flags.isSkill && !flags.isStill) candidates.push('skill')
  if (flags.cardMotion) candidates.push(flags.cardMotion)
  if (flags.attack) candidates.push(flags.attack, 'attack')
  candidates.push(flags.isStill ? 'still' : 'idle')
  return candidates
}

/** 使ったカードの解決中（次のカード使用まで）に、使ったキャラの静が解けるか。 */
export function playedCardReleasesCalm(
  steps: ReadonlyArray<{ event: { kind: string; targetInstanceId: string | null; note: string | null } }>,
  playCardIndex: number,
  casterId: string,
): boolean {
  for (let j = playCardIndex + 1; j < steps.length; j++) {
    const ev = steps[j].event
    if (ev.kind === 'PlayCard') return false
    if (ev.kind === 'PhaseChanged' && ev.targetInstanceId === casterId && (ev.note ?? '').startsWith('calm->')) return true
  }
  return false
}

function selectCharacterAnimation(
  animations: CharacterDemo['animations'],
  flags: CharacterMotionFlags,
  failedSources: ReadonlySet<string>,
): ActiveCharacterAnimation | null {
  // still が無いキャラは null（立ち絵の静止画）になる。
  const candidates = characterMotionCandidates(flags)

  for (const motion of candidates) {
    const sheet = animations?.[motion]
    if (
      sheet
      && sheet.frameWidth > 0
      && sheet.frameHeight > 0
      && sheet.frameCount > 0
      && sheet.durationMs > 0
      && !failedSources.has(sheet.src)
    ) {
      return { motion, sheet }
    }
  }

  return null
}

function Slot({
  char,
  isTargeted,
  showTargetVisual,
  attackingDir,
  attackMotion = 'attack',
  isSkill = false,
  cardMotion = null,
  isHit,
  isBlocked,
  isDying = false,
  motionPlaybackSpeed = 1,
  onClick,
  onViewActions,
  replayFeedState,
  participantReady,
  feedbackItems = [],
  actionLabel = null,
  aura = null,
}: SlotProps) {
  // 特性と今の位相（名前と効果）をまとめて出す。
  const traitTip = useTip(slotTooltipContent(char))
  // Why: 影の横幅は描画後の sprite 横幅と同じにしたい。silhouette は決定論的
  //  (tier-height × 0.55) で同期計算できる。画像は naturalWidth/Height が
  //  load 後にしか取れないので onLoad で aspect を State に保持し、それ以降
  //  の render で width = tier-height × aspect を渡す。影の高さは固定 (CSS)。
  const [imageMeasurement, setImageMeasurement] = useState<{
    source: string | undefined
    aspect: number
  } | null>(null)
  const [failedAnimationSources, setFailedAnimationSources] = useState<ReadonlySet<string>>(
    () => new Set<string>(),
  )
  const activeAnimation = selectCharacterAnimation(
    char.animations,
    {
      attack: attackingDir !== undefined ? attackMotion : null,
      isHit: isHit === true,
      isSkill,
      cardMotion,
      isStill: char.phase === 'calm',
      isDown: char.hpCur <= 0,
    },
    failedAnimationSources,
  )
  const imageAspect = imageMeasurement !== null
    && imageMeasurement.source === char.image
    ? imageMeasurement.aspect
    : null
  const handleImageLoad = (e: React.SyntheticEvent<HTMLImageElement>) => {
    const img = e.currentTarget
    if (img.naturalHeight > 0) {
      setImageMeasurement({
        source: char.image,
        aspect: img.naturalWidth / img.naturalHeight,
      })
    }
  }
  const handleAnimationError = (source: string) => {
    setFailedAnimationSources(previous => {
      if (previous.has(source)) return previous
      const next = new Set(previous)
      next.add(source)
      return next
    })
  }
  const displayHeight = char.stage?.displayHeight
    ?? (char.heightTier !== undefined ? heightForTier(char.heightTier) : 148)
  const stageProfile = resolveActorStageProfile(char.image)
  const actorSizeClass = actorSizeClassForTier(char.heightTier)
  const spriteLift = Math.round(displayHeight * stageProfile.liftRatio)
  const actorImpactY = resolveActorImpactAnchorY(
    char.image,
    displayHeight,
    char.stage ? STAGE_CANVAS_BOTTOM_Y : undefined,
  )
  const oneShotPlaybackSpeed = attackingDir !== undefined || isHit || isSkill || cardMotion !== null
    ? normalizeMotionPlaybackSpeed(motionPlaybackSpeed)
    : 1
  const animationFrameAspect = activeAnimation !== null
    ? activeAnimation.sheet.frameWidth / activeAnimation.sheet.frameHeight
    : null
  // 上に余白を足したシートは、その分だけ表示枠を上へ伸ばす（足元は bottom 基準なので動かない）。
  const animationFrameHeight = activeAnimation !== null
    ? displayHeight * activeAnimation.sheet.frameHeight
      / (activeAnimation.sheet.baseFrameHeight ?? activeAnimation.sheet.frameHeight)
    : displayHeight
  const animationFrameWidth = animationFrameAspect !== null
    ? animationFrameHeight * animationFrameAspect
    : null
  const idleFrameAspect = char.animations?.idle
    ? char.animations.idle.frameWidth / char.animations.idle.frameHeight
    : null
  const animationStepCount = activeAnimation !== null
    ? activeAnimation.sheet.frameCount
    : null
  const animationStepPosition = activeAnimation !== null
    && !activeAnimation.sheet.loop
    && activeAnimation.sheet.frameCount > 1
    ? 'jump-none'
    : 'end'
  const animationEndX = activeAnimation !== null && animationFrameWidth !== null
    ? -animationFrameWidth * (
        activeAnimation.sheet.loop
          ? activeAnimation.sheet.frameCount
          : Math.max(0, activeAnimation.sheet.frameCount - 1)
      )
    : null
  const shadowWidth: number | null = (() => {
    if (char.stage) return char.stage.shadowWidth
    if (char.heightTier === undefined) return null // text fallback: CSS default
    const h = heightForTier(char.heightTier)
    // Wide attack cells reserve room for weapons. They must not make the
    // grounded shadow expand during the strike, so stage it from the neutral
    // idle footprint whenever one exists.
    const groundedAspect = idleFrameAspect ?? animationFrameAspect
    if (groundedAspect !== null && Number.isFinite(groundedAspect)) {
      return h * groundedAspect
    }
    if (char.image) {
      return imageAspect !== null && Number.isFinite(imageAspect)
        ? h * imageAspect
        : null
    }
    // silhouette: 既存の width: calc(--tier-height * 0.55) と同じ
    return h * 0.55
  })()
  const shadowStyle: CSSProperties | undefined =
    shadowWidth !== null ? { width: `${Math.round(shadowWidth)}px` } : undefined
  const attackMotionDuration = scaledOneShotMotionDuration(readableMotionDuration(
    char.animations?.attack,
    ATTACK_SLIDE_MS,
  ), oneShotPlaybackSpeed)
  const hitMotionDuration = scaledOneShotMotionDuration(readableMotionDuration(
    char.animations?.hurt,
    HIT_FLASH_MS,
  ), oneShotPlaybackSpeed)
  const slotMotionStyle = {
    // 頭上の行動予告の下地（主人公ごとのデザイン。主人公以外は CSS 側の共通の枠）。
    ...(char.uiHeroId ? { '--slot-intent-plate': `url('${heroUi(char.uiHeroId, 'intent_plate')}')` } : {}),
    '--slot-attack-duration': `${attackMotionDuration}ms`,
    '--slot-hit-duration': `${hitMotionDuration}ms`,
    '--slot-attack-duration-reduced': `${Math.max(1, Math.round(attackMotionDuration * 0.48))}ms`,
    '--slot-hit-duration-reduced': `${Math.max(1, Math.round(hitMotionDuration * 0.48))}ms`,
    '--sprite-lift': `${spriteLift}px`,
    '--actor-impact-y': `${actorImpactY}px`,
    '--actor-effect-height': `${Math.max(54, Math.round(displayHeight * 0.78))}px`,
    '--actor-effect-width': `${Math.max(54, Math.min(148, Math.round(shadowWidth ?? displayHeight * 0.64)))}px`,
  } as CSSProperties

  if (!char.occupied) {
    return <div className="battle__slot" data-occupied="0" />
  }
  const pct = Math.max(0, Math.min(100, (char.hpCur / char.hpMax) * 100))
  const hpFillStyle: CSSProperties = { width: `${pct}%` }
  const cls = [
    'battle__slot',
    isTargeted && (showTargetVisual ?? true) ? 'is-targeted' : '',
    attackingDir === 'toward-enemy' ? 'is-attacking-enemy' : '',
    attackingDir === 'toward-ally' ? 'is-attacking-ally' : '',
    isHit ? 'is-hit' : '',
    isBlocked ? 'is-blocked' : '',
    isDying ? 'is-dying' : '',
    aura ? `has-aura has-aura--${aura.kind}` : '',
    char.phase ? `has-phase has-phase--${char.phase}` : '',
    char.phase === 'calm' ? 'is-still' : '',
  ].filter(Boolean).join(' ')
  // Why: スプレッドにすることで onClick が undefined のとき role/tabIndex 属性自体が
  // JSX に出ない。axe/aria リンターの "role must be valid ARIA role: {expression}"
  // 誤検出（条件式値の静的解析不可）を回避する目的。
  // マウスのクリックは側（味方・敵）の枠でまとめて判定する（隣と重なった所は今の対象でない方）。
  // ここでは Enter / Space だけを受ける。
  const interactiveProps = onClick
    ? {
        onKeyDown: (event: KeyboardEvent<HTMLDivElement>) => {
          if (event.key !== 'Enter' && event.key !== ' ') return
          event.preventDefault()
          onClick()
        },
        role: 'button' as const,
        tabIndex: 0,
        'aria-label': `${char.name}を対象にする`,
        'aria-pressed': isTargeted === true,
      }
    : {}
  return (
    <div
      className={cls}
      data-occupied="1"
      data-actor-id={char.instanceId}
      data-targetable={onClick ? '1' : undefined}
      data-body-width={char.stage ? Math.round(char.stage.bodyWidth) : undefined}
      data-size-class={actorSizeClass}
      data-airborne={stageProfile.airborne ? 'true' : 'false'}
      style={slotMotionStyle}
      {...interactiveProps}
    >
      <CombatFeedbackStack items={feedbackItems} />
      <PhaseAura
        phase={char.phase}
        style={char.stage
          ? phaseRingStyle(char.stage)
          : phaseRingStyle({ bodyWidth: (shadowWidth ?? 78) / 0.8, footGap: 8 })}
      />
      <DisruptionHalo
        stacks={char.disruption}
        style={disruptionHaloStyle(char.stage
          ?? { bodyWidth: (shadowWidth ?? 78) / 0.8, displayHeight, headTop: 40 })}
      />
      {actionLabel ? (
        <div key={actionLabel.id} className="slot-action-label" role="status">
          <span className="slot-action-label__text">{actionLabel.text}</span>
        </div>
      ) : null}
      {aura ? (
        <span
          key={aura.id}
          className={`slot-aura slot-aura--${aura.kind}`}
          data-presentation="flash"
          aria-hidden="true"
        />
      ) : null}
      {isHit ? (
        <span className="battle-hit-flash" data-presentation="flash" aria-hidden="true" />
      ) : null}
      {isBlocked && !isHit ? (
        <span className="battle-block-spark" data-presentation="flash" aria-hidden="true">
          <i /><i /><i /><i />
        </span>
      ) : null}
      {participantReady !== undefined ? (
        <div
          className={`status-readiness ${participantReady ? 'is-ready' : 'is-planning'}`}
          data-ready={participantReady ? 'true' : 'false'}
          aria-label={participantReady ? 'READY' : '行動中'}
        >
          {participantReady ? 'READY' : '行動中…'}
        </div>
      ) : null}
      {char.intents && char.intents.length > 0 ? (
        // Why: 複数 intent と攻撃範囲の内訳を、行動1つにつき1行で並べる。
        <div className="intents">
          <IntentChipRow intents={char.intents} />
        </div>
      ) : char.intent ? (
        <IntentChip intent={char.intent} />
      ) : null}
      {/* Why: image があれば <img>、無くても heightTier があればシルエット
          placeholder（実アセット届くまでの仮置き）、両方無ければ text sprite。
          --tier-height CSS 変数で高さを段階制御 (1〜10)。 */}
      {activeAnimation || char.image ? (
        <div
          className={`sprite sprite--image sprite--${char.spriteKind}${char.stage ? ' sprite--stage' : ''}`}
          data-motion={activeAnimation?.motion ?? 'idle'}
          tabIndex={char.traitDescription ? 0 : undefined}
          aria-label={char.traitDescription
            ? `${char.name}の特性 ${char.traitName ?? '特性'}`
            : undefined}
          {...traitTip}
          style={
            ({
              '--tier-height': `${displayHeight}px`,
              ...(activeAnimation && animationFrameWidth !== null
                ? {
                    '--sprite-frame-width': `${animationFrameWidth}px`,
                    '--sprite-frame-height': `${animationFrameHeight}px`,
                    '--sprite-frame-count': activeAnimation.sheet.frameCount,
                    '--sprite-step-count': animationStepCount,
                    '--sprite-step-position': animationStepPosition,
                    '--sprite-duration': `${scaledOneShotMotionDuration(activeAnimation.sheet.durationMs, oneShotPlaybackSpeed)}ms`,
                    '--sprite-duration-reduced': `${scaledOneShotMotionDuration(activeAnimation.sheet.durationMs * 0.48, oneShotPlaybackSpeed)}ms`,
                    '--sprite-end-x': `${animationEndX}px`,
                    '--sprite-iteration-count': activeAnimation.sheet.loop ? 'infinite' : '1',
                    '--sprite-fill-mode': activeAnimation.sheet.loop ? 'none' : 'forwards',
                  }
                : {}),
            } as CSSProperties)
          }
        >
          {activeAnimation ? (
            <div className="sprite-sheet__frame">
              <div className="sprite-sheet__viewport">
                <img
                  key={`${activeAnimation.motion}:${activeAnimation.sheet.src}`}
                  className="sprite-sheet__image"
                  src={activeAnimation.sheet.src}
                  alt={char.name}
                  draggable={false}
                  onError={() => handleAnimationError(activeAnimation.sheet.src)}
                />
              </div>
            </div>
          ) : char.image ? (
            <img
              className="sprite__static-image"
              src={char.image}
              alt={char.name}
              draggable={false}
              onLoad={handleImageLoad}
            />
          ) : null}
        </div>
      ) : char.heightTier !== undefined ? (
        <div
          className={`sprite sprite--silhouette sprite--${char.spriteKind}`}
          style={{ '--tier-height': `${heightForTier(char.heightTier)}px` } as CSSProperties}
          aria-label={char.traitDescription
            ? `${char.name}の特性 ${char.traitName ?? '特性'}`
            : `${char.name} (placeholder)`}
          tabIndex={char.traitDescription ? 0 : undefined}
          {...traitTip}
        />
      ) : (
        <div
          className={`sprite sprite--${char.spriteKind}${char.sprite.length > 2 ? ' sprite--text' : ''}`}
          tabIndex={char.traitDescription ? 0 : undefined}
          aria-label={char.traitDescription
            ? `${char.name}の特性 ${char.traitName ?? '特性'}`
            : undefined}
          {...traitTip}
        >
          {char.sprite}
        </div>
      )}
      <div className="sprite-shadow" style={shadowStyle} />
      <div
        className="status-hp"
        data-has-block={char.blockValue > 0 ? 'true' : undefined}
      >
        <div className="status-hp__track" data-lv={char.hpLv}>
          <div className="status-hp__fill" style={hpFillStyle} />
          <span className="status-hp__num">
            {char.hpCur}/{char.hpMax}
          </span>
        </div>
      </div>
      {char.blockValue > 0 && <StatusBlock value={char.blockValue} />}
      <StatusName name={char.name} />
      {onViewActions ? (
        <button
          type="button"
          className="status-replay-button"
          data-feed-state={replayFeedState ?? 'live'}
          aria-label={`${char.name}の行動を見る`}
          onClick={event => {
            event.stopPropagation()
            onViewActions()
          }}
        >
          行動
        </button>
      ) : null}
      {/* Why: バフ/デバフ/ブロック値が現れてもキャラ位置が動かないよう、常に
          status-buffs 行を確保。空配列ならエンプティ row として min-height だけ
          残す (CSS 側で 24px 確保)。 */}
      <div className="status-buffs">
        {char.buffs.map((b, i) => (
          <StatusBuff key={i} buff={b} />
        ))}
      </div>
    </div>
  )
}

type HandCardProps = {
  card: HandCardDemo
  /** 能力（双極など）が付く瞬間の光。 */
  augmenting?: boolean
  fan: { x: number; y: number; r: number }
  onClick?: () => void
  /** Why: 直前まで hand にいたが手放されたカード (play / discard / exhaust /
   *  turn end discard 等) の exit アニメーション中の表示用。CSS で
   *  .is-leaving class が付き、捨札方向に縮小フェードする。 */
  leaving?: boolean
  /** leaving 時の行先方向 (px)。card-leave keyframe が --leave-dx/--leave-dy を読む。 */
  leaveDx?: number
  leaveDy?: number
  /**
   * Phase 10.5.M6.6: 喪失/除外と通常捨札で leaving 演出を切替える。
   *  'discard': 既存の捨札方向への縮小フェード (位置移動あり)。
   *  'exhaust': その場で高速回転しながら縮小消滅 (位置移動なし)。
   */
  leavingMode?: 'discard' | 'exhaust'
  /** 親が DOM 要素を識別 (rect 計測) するための callback。 */
  onCardElement?: (el: HTMLDivElement | null) => void
  /** Why: 同じカードが <PlayingCard> overlay で表示されている間は手札側を
   *  非表示にする (visibility: hidden で fan layout 維持)。重複表示防止。 */
  hidden?: boolean
  /** Busy/choice/replay locks must not masquerade as a card-rule failure. */
  interactionLocked?: boolean
}

type BattleCardVisualProps = {
  card: HandCardDemo
  className?: string
  detailsDisabled?: boolean
  ariaHidden?: boolean
  onClick?: MouseEventHandler<HTMLDivElement>
}

/**
 * Canonical battle card face. Hand cards and every animated copy use this
 * exact prop mapping, so zooming/moving a card never drops its description,
 * cost history, keyword state, or other face elements.
 */
function BattleCardVisual({
  card,
  className,
  detailsDisabled,
  ariaHidden,
  onClick,
}: BattleCardVisualProps) {
  return (
    <Card
      cardId={card.cardId}
      name={card.name}
      cost={card.cost}
      costOrig={card.costOrig}
      costOriginal={card.costOriginal}
      costChanges={card.costChanges}
      comboReductionEligible={card.comboReductionEligible}
      comboContinues={card.comboContinues}
      comboRingHighlighted={card.comboRingHighlighted}
      wildDisabled={card.wildDisabled}
      costUnavailable={card.costUnavailable}
      type={card.type}
      rarity={card.rarity}
      poolClass={card.poolClass}
      art={card.art}
      upgraded={card.upgraded}
      description={card.description ?? card.desc}
      upgradedDescription={card.upgradedDescription}
      contingencyArmed={card.isContingencyArmed}
      contingencyTriggerText={card.contingencyTriggerText}
      className={className}
      detailsDisabled={detailsDisabled}
      ariaHidden={ariaHidden}
      onClick={onClick}
    />
  )
}

function toReplayCardDemo(
  card: BattleReplayCardDto,
  catalog: import('../api/catalog').CardCatalog | null,
): HandCardDemo {
  const rawType = card.cardType.toLowerCase()
  const type: CardType = (
    rawType === 'attack' || rawType === 'skill' || rawType === 'power'
      || rawType === 'curse' || rawType === 'status' || rawType === 'unit'
  ) ? rawType : 'skill'
  const costChanges: NonNullable<BattleCardInstanceDto['costChanges']> = card.costChanges.map(change => ({
    kind: change.kind === 'fixed' || change.kind === 'modifier' || change.kind === 'status'
      || change.kind === 'combo' || change.kind === 'unknown'
      ? change.kind
      : 'unknown',
    value: change.value,
    sourceId: change.sourceId,
    sourceName: change.sourceName,
  }))
  return {
    instanceId: card.cardInstanceId,
    name: card.displayName,
    cost: card.currentCost,
    costOrig: card.currentCost !== card.originalCost ? card.originalCost : null,
    costOriginal: card.originalCost,
    costChanges,
    type,
    rarity: rarityCodeFromName(card.rarity) ?? 'c',
    poolClass: card.poolClass ?? catalog?.[card.definitionId]?.poolClass ?? null,
    desc: card.description,
    description: card.description,
    upgraded: card.isUpgraded,
    playable: false,
  }
}

function HandCard({
  card,
  augmenting = false,
  fan,
  onClick,
  leaving,
  leaveDx,
  leaveDy,
  leavingMode,
  onCardElement,
  hidden,
  interactionLocked = false,
}: HandCardProps) {
  // We need the rendered `.card` element to be a direct child of
  // `.hand` (CSS selector `.hand > .card` keys off this). Card.tsx
  // does not expose a ref. We use a sibling probe <span> (via
  // ref-callback) to capture the previous DOM sibling — which is the
  // `.card` root — and imperatively apply CSS vars (fan-x/y/r,
  // leave-dx/dy) on it.
  const [cardEl, setCardEl] = useState<HTMLDivElement | null>(null)
  const dragRef = useRef<{
    pointerId: number
    startX: number
    startY: number
    moved: boolean
  } | null>(null)
  const suppressNextClickRef = useRef(false)
  const attemptTimerRef = useRef<number | null>(null)
  const handEntryTimerRef = useRef<number | null>(null)
  const [showUnavailableReason, setShowUnavailableReason] = useState(false)

  const showFailedAttempt = useCallback(() => {
    if (!card.unavailableReason || interactionLocked) return
    if (attemptTimerRef.current !== null) window.clearTimeout(attemptTimerRef.current)
    setShowUnavailableReason(true)
    // Re-trigger the short rejection nudge on repeated attempts.
    cardEl?.classList.remove('is-play-rejected')
    requestAnimationFrame(() => cardEl?.classList.add('is-play-rejected'))
    attemptTimerRef.current = window.setTimeout(() => {
      attemptTimerRef.current = null
      setShowUnavailableReason(false)
      cardEl?.classList.remove('is-play-rejected')
    }, 1450)
  }, [card.unavailableReason, cardEl, interactionLocked])

  const activateCard = useCallback(() => {
    if (onClick) {
      onClick()
      return
    }
    showFailedAttempt()
  }, [onClick, showFailedAttempt])

  useEffect(() => () => {
    if (attemptTimerRef.current !== null) window.clearTimeout(attemptTimerRef.current)
    if (handEntryTimerRef.current !== null) window.clearTimeout(handEntryTimerRef.current)
  }, [])

  const probeRefCb = useCallback((probe: HTMLSpanElement | null) => {
    if (!probe) {
      setCardEl(null)
      return
    }
    const prev = probe.previousElementSibling
    if (prev instanceof HTMLDivElement && prev.classList.contains('card')) {
      setCardEl(prev)
    }
  }, [])

  useEffect(() => {
    if (!cardEl) return
    cardEl.style.setProperty('--fan-x', `${fan.x}px`)
    cardEl.style.setProperty('--fan-y', `${fan.y}px`)
    cardEl.style.setProperty('--fan-r', `${fan.r}deg`)
  }, [cardEl, fan.x, fan.y, fan.r])

  // `card-enter` used to live permanently on every hand card. A temporary
  // rejection animation replaced it, then removing the rejection class
  // re-applied `card-enter` about 1.45s after a failed drag/click and looked
  // exactly like an erroneous draw. Scope entry motion to the initial mount.
  useEffect(() => {
    if (!cardEl || leaving) return
    const finishEntry = () => {
      cardEl.classList.remove('is-hand-entering')
      if (handEntryTimerRef.current !== null) {
        window.clearTimeout(handEntryTimerRef.current)
        handEntryTimerRef.current = null
      }
    }
    const onAnimationEnd = (event: AnimationEvent) => {
      if (event.target === cardEl
          && (!event.animationName || event.animationName === 'card-enter')) finishEntry()
    }
    cardEl.classList.add('is-hand-entering')
    cardEl.addEventListener('animationend', onAnimationEnd)
    handEntryTimerRef.current = window.setTimeout(finishEntry, 300)
    return () => {
      cardEl.removeEventListener('animationend', onAnimationEnd)
      finishEntry()
    }
  }, [cardEl, leaving])

  // Why: leaving 時の行先 dx/dy は keyframe で参照される CSS 変数として注入。
  //  static keyframe (旧 +320/+10 hardcode) を動的にしてゾーン間移動の終点を
  //  実 pile 中心に合わせる (B 要件)。
  useEffect(() => {
    if (!cardEl) return
    if (leaving) {
      cardEl.style.setProperty('--leave-dx', `${leaveDx ?? 320}px`)
      cardEl.style.setProperty('--leave-dy', `${leaveDy ?? 10}px`)
    }
  }, [cardEl, leaving, leaveDx, leaveDy])

  // 親 (BattleScreen) に DOM 要素を通知して rect 計測ができるようにする。
  useEffect(() => {
    if (!onCardElement) return
    onCardElement(cardEl)
    return () => onCardElement(null)
  }, [cardEl, onCardElement])

  useEffect(() => {
    if (!cardEl || (!onClick && (!card.unavailableReason || interactionLocked)) || leaving || hidden) return
    const resetDragVisual = () => {
      cardEl.classList.remove('is-dragging')
      cardEl.style.removeProperty('--drag-x')
      cardEl.style.removeProperty('--drag-y')
    }
    const onPointerDown = (event: PointerEvent) => {
      if (event.button !== 0) return
      dragRef.current = {
        pointerId: event.pointerId,
        startX: event.clientX,
        startY: event.clientY,
        moved: false,
      }
      // Synthetic accessibility/E2E PointerEvents do not have an active
      // browser pointer and therefore may reject capture. Drag state itself
      // remains valid; real mouse/touch pointers still take capture normally.
      try {
        cardEl.setPointerCapture?.(event.pointerId)
      } catch {
        // No active native pointer to capture.
      }
      cardEl.classList.add('is-dragging')
      cardEl.style.setProperty('--drag-x', '0px')
      cardEl.style.setProperty('--drag-y', '0px')
    }
    const onPointerMove = (event: PointerEvent) => {
      const drag = dragRef.current
      if (!drag || drag.pointerId !== event.pointerId) return
      const dx = Math.max(-180, Math.min(180, event.clientX - drag.startX))
      const dy = Math.max(-220, Math.min(34, event.clientY - drag.startY))
      if (Math.abs(dx) + Math.abs(dy) > 8) drag.moved = true
      cardEl.style.setProperty('--drag-x', `${dx}px`)
      cardEl.style.setProperty('--drag-y', `${dy}px`)
      if (Math.abs(dx) + Math.abs(dy) > 8) event.preventDefault()
    }
    const finishPointer = (event: PointerEvent) => {
      const drag = dragRef.current
      if (!drag || drag.pointerId !== event.pointerId) return
      const dx = event.clientX - drag.startX
      const dy = event.clientY - drag.startY
      dragRef.current = null
      resetDragVisual()
      if (drag.moved) {
        // A drag that returns below the play threshold is still a drag, not a
        // click. Suppress the synthetic click browsers emit after pointerup.
        suppressNextClickRef.current = true
        window.setTimeout(() => { suppressNextClickRef.current = false }, 0)
      }
      if (dy <= -82 && Math.abs(dx) <= 210) {
        activateCard()
      }
    }
    const cancelPointer = (event: PointerEvent) => {
      if (dragRef.current?.pointerId !== event.pointerId) return
      dragRef.current = null
      resetDragVisual()
    }
    cardEl.addEventListener('pointerdown', onPointerDown)
    cardEl.addEventListener('pointermove', onPointerMove)
    cardEl.addEventListener('pointerup', finishPointer)
    cardEl.addEventListener('pointercancel', cancelPointer)
    return () => {
      dragRef.current = null
      resetDragVisual()
      cardEl.removeEventListener('pointerdown', onPointerDown)
      cardEl.removeEventListener('pointermove', onPointerMove)
      cardEl.removeEventListener('pointerup', finishPointer)
      cardEl.removeEventListener('pointercancel', cancelPointer)
    }
  }, [activateCard, card.unavailableReason, cardEl, hidden, interactionLocked, leaving, onClick])

  const handleCardClick: MouseEventHandler<HTMLDivElement> | undefined =
    (onClick || (card.unavailableReason && !interactionLocked))
    ? (event) => {
        if (suppressNextClickRef.current) {
          event.preventDefault()
          return
        }
        activateCard()
      }
    : undefined

  return (
    <>
      <BattleCardVisual
        card={card}
        className={[
          card.playable && !card.cardUsageLimitReached ? 'is-playable' : '',
          leaving ? 'is-leaving' : '',
          // M6.6: 喪失行きは別 class でその場回転縮小消滅アニメへ。
          leaving && leavingMode === 'exhaust' ? 'is-leaving-exhaust' : '',
          hidden ? 'is-playing-hidden' : '',
          augmenting ? 'is-augmenting' : '',
        ].filter(Boolean).join(' ') || undefined}
        onClick={leaving ? undefined : handleCardClick}
      />
      <span ref={probeRefCb} aria-hidden="true" className="hand__probe" />
      {card.unavailableReason && !leaving && showUnavailableReason ? (
        <span
          className="hand-card-unavailable is-visible"
          style={{
            '--fan-x': `${fan.x}px`,
            '--fan-y': `${fan.y}px`,
          } as CSSProperties}
          role="status"
        >
          {card.unavailableReason}
        </span>
      ) : null}
    </>
  )
}

// -------------------- Pile modal --------------------

type PileKind = 'draw' | 'discard' | 'exhaust'

type PileModalProps = {
  kind: PileKind
  cards: import('../api/types').BattleCardInstanceDto[]
  onClose: () => void
}

function PileModal({ kind, cards, onClose }: PileModalProps) {
  const { catalog } = useCardCatalog()
  const title = kind === 'draw' ? '山札' : kind === 'discard' ? '捨札' : '除外'
  // Why: 山札はシャッフル順を伏せるためカード名でソート (TopBar デッキ表示と
  // 同じ方式)。捨札 / 除外は実際の積み順を保持する。
  const ordered = kind === 'draw'
    ? [...cards].sort((a, b) => {
        const an = catalog?.[a.cardDefinitionId]?.displayName ?? catalog?.[a.cardDefinitionId]?.name ?? a.cardDefinitionId
        const bn = catalog?.[b.cardDefinitionId]?.displayName ?? catalog?.[b.cardDefinitionId]?.name ?? b.cardDefinitionId
        return an.localeCompare(bn, 'ja')
      })
    : cards
  return (
    <div className="pile-modal-backdrop" onClick={onClose} role="presentation">
      <div
        className="pile-modal"
        role="dialog"
        aria-label={title}
        onClick={(e) => e.stopPropagation()}
      >
        <header className="pile-modal__header">
          <span>
            {title} ({cards.length}枚)
            {kind === 'draw' ? (
              <span className="pile-modal__note"> ※山札の順番は不明</span>
            ) : null}
          </span>
          <button type="button" className="pile-modal__close" onClick={onClose} aria-label="閉じる">
            ×
          </button>
        </header>
        {ordered.length === 0 ? (
          <p className="pile-modal__empty">{title}は空です</p>
        ) : (
          <ul className="pile-modal__list">
            {ordered.map((card, i) => {
              const def = catalog?.[card.cardDefinitionId]
              const fallbackName = def?.displayName ?? def?.name ?? card.cardDefinitionId
              // Why: 10.5.C - hero context 反映済の adjustedDescription を優先採用、
              //   未指定 (catalog 経路 fallback) は static description を使う。
              const disp = {
                name: fallbackName,
                cost: card.currentCost ?? 'X',
                costOrig: card.currentCost !== card.originalCost
                  ? card.originalCost
                  : null,
                type: ((def?.cardType ?? 'attack').toLowerCase()) as CardType,
                rarity: rarityCodeFromNumber(def?.rarity ?? 1),
                description: card.adjustedDescription ?? def?.description ?? '',
                upgradedDescription:
                  card.adjustedUpgradedDescription ?? def?.upgradedDescription ?? null,
              }
              return (
                <li key={`${card.instanceId}-${i}`} className="pile-modal__item">
                  <Card
                    cardId={card.cardDefinitionId}
                    name={disp.name}
                    cost={disp.cost}
                    costOrig={disp.costOrig}
                    costOriginal={card.originalCost}
                    costChanges={card.costChanges}
                    comboReductionEligible={card.comboReductionEligible ?? false}
                    comboContinues={card.comboContinues ?? card.comboReductionEligible ?? false}
                    comboRingHighlighted={card.comboRingHighlighted ?? false}
                    wildDisabled={card.wildDisabled ?? false}
                    costUnavailable={(card.cardUsageLimitReached ?? false) || (card.cardPlayRestricted ?? false)}
                    type={disp.type}
                    rarity={disp.rarity}
                    poolClass={card.poolClass ?? def?.poolClass ?? null}
                    description={disp.description}
                    upgradedDescription={disp.upgradedDescription}
                    upgraded={card.isUpgraded}
                    contingencyArmed={card.isContingencyArmed}
                    contingencyTriggerText={card.contingencyTriggerText}
                  />
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </div>
  )
}

/**
 * COMBO chip with hover tooltip.
 * Why: normal next-cost and pending Wild-family pass reductions must be
 * understandable without inspecting every card in hand.
 */
export function ComboChip({
  count,
  maxComboThisTurn,
  lastPlayedOrigCost,
  nextCardComboFreePass,
  comboCarryoverPending = false,
}: {
  count: number
  maxComboThisTurn: number
  lastPlayedOrigCost: number | null
  nextCardComboFreePass: boolean
  comboCarryoverPending?: boolean
}) {
  const desc = formatComboDescription({
    count,
    lastPlayedOrigCost,
    nextCardComboFreePass,
    comboCarryoverPending,
  })
  const tip = useTip({
    name: 'コンボ',
    statusLine: formatMaxComboThisTurn(maxComboThisTurn),
    desc,
  })
  return (
    <div
      className={`battle__combo${count > 1 ? ' is-active' : ''}`}
      aria-label={`現在${count}コンボ${maxComboThisTurn > count ? ` 最大${maxComboThisTurn}` : ''}`}
      {...tip}
    >
      <span className="battle__combo-v">
        <span
          key={count}
          className="battle__combo-current"
          data-milestone={count >= 15 ? '15' : count >= 10 ? '10' : count >= 5 ? '5' : undefined}
          data-motion="decorative"
        >
          {count}
        </span>
        {maxComboThisTurn > count ? (
          <span className="battle__combo-max">(MAX{maxComboThisTurn})</span>
        ) : null}
      </span>
      <span className="battle__combo-k">COMBO</span>
    </div>
  )
}

export function ComboGauge({ value }: { value: number }) {
  const clampedValue = clampComboGauge(value)
  const accessibleName = `コンボゲージ ${clampedValue}/${COMBO_GAUGE_MAX}`
  const tip = useTip({
    name: 'コンボゲージ',
    statusLine: `(現在${clampedValue}/${COMBO_GAUGE_MAX})`,
    desc: COMBO_GAUGE_DESCRIPTION,
  })
  return (
    <div
      className="combo-gauge"
      role="meter"
      aria-label={accessibleName}
      aria-valuemin={0}
      aria-valuemax={COMBO_GAUGE_MAX}
      aria-valuenow={clampedValue}
      tabIndex={0}
      {...tip}
    >
      {Array.from({ length: COMBO_GAUGE_MAX }, (_, index) => (
        <span
          className={`combo-gauge__segment${index < clampedValue ? ' is-filled' : ''}`}
          aria-hidden="true"
          key={index}
        />
      ))}
      <span className="combo-gauge__value" aria-hidden="true">
        {clampedValue}/{COMBO_GAUGE_MAX}
      </span>
    </div>
  )
}

type ComboSkillKind = 'tactical' | 'ultimate'

const CATALOG_USAGE_HEADER_RE = /^(?:(?:ターン中|1ターンに)\d+度だけ使用可能\(\d+\/\d+\)。(?:\r?\n|$))+/u

function withoutCatalogUsageHeader(description: string): string {
  return description.replace(CATALOG_USAGE_HEADER_RE, '')
}

function ComboSkillControl({
  kind,
  disabled,
  onClick,
  skillName,
  gaugeCost,
  energyCost,
  description,
  oncePerTurn,
  usesPerTurn,
  usesThisTurn,
  unconfigured,
  disabledReason,
}: {
  kind: ComboSkillKind
  disabled: boolean
  onClick: () => void
  skillName?: string
  gaugeCost?: number
  energyCost?: number
  description?: string
  oncePerTurn?: boolean
  usesPerTurn?: number
  usesThisTurn?: number
  unconfigured?: boolean
  disabledReason?: string
}) {
  const tactical = kind === 'tactical'
  const baseLabel = tactical ? '戦技' : '奥義'
  const configured = !unconfigured
    && (skillName !== undefined || gaugeCost !== undefined
      || energyCost !== undefined || description !== undefined)
  const label = unconfigured
    ? `${baseLabel}：未設定`
    : skillName
    ? `${baseLabel}：${skillName}`
    : baseLabel
  const cost = gaugeCost ?? (tactical ? TACTICAL_SKILL_COST : ULTIMATE_SKILL_COST)
  const costText = energyCost && energyCost > 0
    ? `エナジー${energyCost} / コンボゲージ${cost}`
    : `コンボゲージ${cost}`
  const compactCostText = unconfigured
    ? '未設定'
    : energyCost && energyCost > 0
      ? `消費 E${energyCost} / G${cost}`
      : `消費 ${cost}`
  // The built-in fallback skills also have the current one-use-per-turn rule. Keep the
  // counter visible even before character metadata has loaded so the tooltip never falls
  // back to the obsolete uncounted wording.
  const usageLimit = usesPerTurn ?? (oncePerTurn || !configured ? 1 : 0)
  const usageCount = usesThisTurn ?? 0
  // Current catalogs expose usesPerTurn separately. Strip a legacy catalog's initial
  // (0/N) line as well, then render exactly one counter from the live battle state.
  const effectDescription = withoutCatalogUsageHeader(
    description ?? (tactical ? TACTICAL_SKILL_DESCRIPTION : ULTIMATE_SKILL_DESCRIPTION),
  )
  const liveUsageDescription = `ターン中${usageLimit}度だけ使用可能(${usageCount}/${usageLimit})。`
  const desc = unconfigured
    ? 'このキャラクターには設定されていません。'
    : usageLimit > 0
    ? effectDescription.length > 0
      ? `${liveUsageDescription}\n${effectDescription}`
      : liveUsageDescription
    : effectDescription
  const accessibleName = unconfigured
    ? `${baseLabel}（未設定）`
    : `${label}（${configured ? costText : `コンボゲージ${cost}`}消費）`
  const statusLine = unconfigured ? '(未設定)' : configured ? (
    <>
      <div>({costText}消費)</div>
      {disabledReason ? <div className="combo-skill-tip__reason">使用不可：{disabledReason}</div> : null}
    </>
  ) : `(コンボゲージ${cost}消費)`
  const tip = useTip({
    name: label,
    statusLine,
    desc,
  })

  return (
    <div
      className={`combo-skill-control combo-skill-control--${kind}`}
      data-combo-skill={kind}
      role="group"
      aria-label={unconfigured ? `${baseLabel} 未設定` : `${accessibleName} ${costText}`}
      aria-disabled={disabled || unconfigured}
      tabIndex={disabled || unconfigured ? 0 : undefined}
      {...tip}
    >
      <button
        type="button"
        className="combo-skill-button"
        data-dual-cost={energyCost && energyCost > 0 ? 'true' : undefined}
        aria-label={accessibleName}
        aria-describedby={tip['aria-describedby']}
        disabled={disabled || unconfigured}
        onClick={onClick}
      >
        <span className="combo-skill-button__name">{baseLabel}</span>
        <span className="combo-skill-button__cost">{compactCostText}</span>
      </button>
    </div>
  )
}

export function BattleComboControls({
  count,
  maxComboThisTurn,
  lastPlayedOrigCost,
  nextCardComboFreePass,
  comboCarryoverPending = false,
  comboGauge,
  tacticalDisabled,
  ultimateDisabled,
  onUseTactical,
  onUseUltimate,
  tacticalSkill,
  ultimateSkill,
  tacticalUsesThisTurn,
  ultimateUsesThisTurn,
  tacticalUnconfigured,
  ultimateUnconfigured,
  tacticalHidden = false,
  ultimateHidden = false,
  finisherRecord,
  playHistoryCount = 0,
  onOpenFinisherRecord,
  tacticalDisabledReason,
  ultimateDisabledReason,
}: {
  count: number
  maxComboThisTurn: number
  lastPlayedOrigCost: number | null
  nextCardComboFreePass: boolean
  comboCarryoverPending?: boolean
  comboGauge: number
  tacticalDisabled: boolean
  ultimateDisabled: boolean
  onUseTactical: () => void
  onUseUltimate: () => void
  tacticalSkill?: { name: string; comboGaugeCost: number; energyCost: number; description: string; oncePerTurn: boolean; usesPerTurn?: number }
  ultimateSkill?: { name: string; comboGaugeCost: number; energyCost: number; description: string; oncePerTurn: boolean; usesPerTurn?: number; requiresCardsInHand?: boolean }
  tacticalUsesThisTurn?: number
  ultimateUsesThisTurn?: number
  tacticalUnconfigured?: boolean
  ultimateUnconfigured?: boolean
  /** 戦技・奥義を持たない参加者（敵など）の表示中は UI ごと隠す。 */
  tacticalHidden?: boolean
  ultimateHidden?: boolean
  finisherRecord?: FinisherRecordDto | null
  /** このターンに使ったカードの枚数（プレイ履歴ボタンの表示用）。 */
  playHistoryCount?: number
  onOpenFinisherRecord?: () => void
  tacticalDisabledReason?: string
  ultimateDisabledReason?: string
}) {
  // 終撃で保存された時だけ「残光 N」を出す（通常の最後のアタック保存は履歴で分かる）。
  const afterimageSavedCount = finisherRecord?.savedByFinisher
    ? finisherRecord.cards.length
    : 0
  return (
    <div className="battle__combo-controls">
      {tacticalHidden ? <div className="battle__combo-skill-placeholder" aria-hidden="true" /> : (
      <ComboSkillControl
        kind="tactical"
        disabled={tacticalDisabled}
        onClick={onUseTactical}
        skillName={tacticalSkill?.name}
        gaugeCost={tacticalSkill?.comboGaugeCost}
        energyCost={tacticalSkill?.energyCost}
        description={tacticalSkill?.description}
        oncePerTurn={tacticalSkill?.oncePerTurn}
        usesPerTurn={tacticalSkill?.usesPerTurn}
        usesThisTurn={tacticalUsesThisTurn}
        unconfigured={tacticalUnconfigured}
        disabledReason={tacticalDisabledReason}
      />
      )}
      <div className="battle__combo-column">
        <ComboChip
          count={count}
          maxComboThisTurn={maxComboThisTurn}
          lastPlayedOrigCost={lastPlayedOrigCost}
          nextCardComboFreePass={nextCardComboFreePass}
          comboCarryoverPending={comboCarryoverPending}
        />
        <ComboGauge value={comboGauge} />
        {onOpenFinisherRecord ? (
          // プレイ履歴（どのクラスでも使える）。残光が保存されている間は強調表示。
          <button
            type="button"
            className={`finisher-record-button${afterimageSavedCount > 0 ? ' is-afterimage-saved' : ''}`}
            onClick={onOpenFinisherRecord}
            aria-label={afterimageSavedCount > 0
              ? `このターンのプレイ履歴を表示（${playHistoryCount}枚、残光保存${afterimageSavedCount}枚）`
              : `このターンのプレイ履歴を表示（${playHistoryCount}枚）`}
          >
            プレイ履歴 {playHistoryCount}
            {afterimageSavedCount > 0 ? (
              <span className="finisher-record-button__saved">残光 {afterimageSavedCount}</span>
            ) : null}
          </button>
        ) : null}
      </div>
      {ultimateHidden ? <div className="battle__combo-skill-placeholder" aria-hidden="true" /> : (
      <ComboSkillControl
        kind="ultimate"
        disabled={ultimateDisabled}
        onClick={onUseUltimate}
        skillName={ultimateSkill?.name}
        gaugeCost={ultimateSkill?.comboGaugeCost}
        energyCost={ultimateSkill?.energyCost}
        description={ultimateSkill?.description}
        oncePerTurn={ultimateSkill?.oncePerTurn}
        usesPerTurn={ultimateSkill?.usesPerTurn}
        usesThisTurn={ultimateUsesThisTurn}
        unconfigured={ultimateUnconfigured}
        disabledReason={ultimateDisabledReason}
      />
      )}
    </div>
  )
}

export function EnergyOrb({
  cur,
  recoveryPerTurn,
}: {
  cur: number
  recoveryPerTurn: number
}) {
  const value = formatEnergyOrbValue(cur, recoveryPerTurn)
  const title = formatEnergyTooltipTitle()
  const statusLine = formatEnergyTooltipStatusLine(cur, recoveryPerTurn)
  const accessibleName = `${title}${statusLine}`
  const tip = useTip({
    name: title,
    statusLine,
    desc: ENERGY_CARRYOVER_DESCRIPTION,
  })
  return (
    <div className="energy" aria-label={accessibleName} tabIndex={0} {...tip}>
      <div className={`energy__val${value.recoveryText ? ' has-recovery' : ''}`}>
        <span className={`energy__cur${value.capped ? ' is-capped' : ''}`}>
          {value.currentText}
        </span>
        {value.recoveryText ? (
          <span className="energy__recovery">{value.recoveryText}</span>
        ) : null}
      </div>
    </div>
  )
}

// -------------------- Main component --------------------

export function BattleScreen({
  accountId,
  snapshot,
  onBattleResolved,
  onTogglePeek,
  onBattleStateChange,
  menuOpen,
  onOpenMenu,
}: Props) {
  const { playComboTone, playSe, playLayeredSe, duckBgm } = useAudio()
  const { effectiveMotionLevel } = usePresentationSettings()
  const playBattleCue = useCallback((cue: BattleAudioCue, seed: string | number = 0) => {
    void playSe(selectSe(cue, seed))
  }, [playSe])
  const [state, setState] = useState<BattleStateDto | null>(null)
  const [replayView, setReplayView] = useState<BattleReplayViewState | null>(null)
  const [replayActionCard, setReplayActionCard] = useState<ReplayActionCardState | null>(null)
  const [reenactCard, setReenactCard] = useState<ReenactCardState | null>(null)
  const [replayActionInProgress, setReplayActionInProgress] = useState(false)
  const [replayActionBaseline, setReplayActionBaseline] = useState<BattleStateDto | null>(null)
  const replayAnimationTokenRef = useRef(0)
  const replayStartTimerRef = useRef<number | null>(null)
  useEffect(() => () => {
    if (replayStartTimerRef.current !== null) {
      window.clearTimeout(replayStartTimerRef.current)
    }
  }, [])
  const [readyNotice, setReadyNotice] = useState<string | null>(null)
  const previousPrimaryReadyRef = useRef<{ turn: number; participantId: string; ready: boolean } | null>(null)
  const manualUnreadyRef = useRef(false)
  // The server can reset `isReady` while the current turn's attack
  // resolution is still being animated. Keep the player's READY intent
  // visible until the authoritative turn number advances.
  const readyIntentRef = useRef<{ turn: number; participantId: string } | null>(null)
  const [animating, setAnimating] = useState(false)
  const [busy, setBusy] = useState(false)
  // React state is committed asynchronously, so two clicks in the same render can both
  // observe busy=false. Keep an immediate guard for every mutating battle command.
  const busyRef = useRef(false)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (error) void playSe(selectSe('uiError', error))
  }, [error, playSe])
  const [pileOpen, setPileOpen] = useState<PileKind | null>(null)
  const [playHistoryOpen, setPlayHistoryOpen] = useState(false)
  // Why: ターン終了時 attack アニメーションの transient state。
  // attackerId: slide 中の slot、hitId: 赤点滅の被弾 slot、dyingIds: 撃破点滅中。
  const [slotAnim, setSlotAnim] = useState<SlotAnim>({})
  // キャラごとの攻撃回数。攻撃モーション A/B の交互切り替えに使う（戦闘をまたいでも続く）。
  const attackMotionCountRef = useRef(new Map<string, number>())
  // Why: MVP 仕様で step.snapshotAfter は全 step が「最終 state」と同一なため、
  // burst 終了時に setState を呼ぶと一気に最終 HP / dead に飛んでしまう。
  // playSteps 中は setState せず、ここに instanceId → 累積 damage を蓄積し、
  // render 時に actor.currentHp - overlay でプログレッシブに HP を減らす。
  // 全 event 処理後に setState(finalState) + setDamageOverlay({}) で確定。
  const [damageOverlay, setDamageOverlay] = useState<Record<string, number>>({})
  // Why: 攻撃が block で完全相殺されても block 値は減るべき (ユーザ要望)。
  // engine の DealDamage は post-block damage しか出さないため、Client 側で
  // AttackFire.Amount (= totalAttack) と DealDamage.Amount (= damage) の差分を
  // block 消費量として累積する。
  const [blockOverlay, setBlockOverlay] = useState<Record<string, number>>({})
  const [combatFeedback, setCombatFeedback] = useState<CombatFeedbackItem[]>([])
  // 敵が使ったカード名の札と、バフ等の発光エフェクト（キャラ単位・時間で消える）。
  const [actionLabels, setActionLabels] = useState<Record<string, { id: number; text: string }>>({})
  const [slotAuras, setSlotAuras] = useState<Record<string, { id: number; kind: SlotAuraKind }>>({})
  const presentationSequenceRef = useRef(0)
  const showActionLabel = useCallback((actorId: string, text: string, lifetimeMs: number) => {
    const id = ++presentationSequenceRef.current
    setActionLabels(current => ({ ...current, [actorId]: { id, text } }))
    window.setTimeout(() => {
      setActionLabels(current => current[actorId]?.id === id
        ? Object.fromEntries(Object.entries(current).filter(([key]) => key !== actorId))
        : current)
    }, lifetimeMs)
  }, [])
  const showSlotAura = useCallback((actorId: string, kind: SlotAuraKind) => {
    const id = ++presentationSequenceRef.current
    setSlotAuras(current => ({ ...current, [actorId]: { id, kind } }))
    window.setTimeout(() => {
      setSlotAuras(current => current[actorId]?.id === id
        ? Object.fromEntries(Object.entries(current).filter(([key]) => key !== actorId))
        : current)
    }, 760)
  }, [])
  const feedbackSequenceRef = useRef(0)
  const feedbackTimersRef = useRef<Map<number, number>>(new Map())
  const pushCombatFeedback = useCallback((
    targetId: string,
    kind: CombatFeedbackKind,
    text: string,
  ) => {
    const id = ++feedbackSequenceRef.current
    setCombatFeedback(current => appendCombatFeedback(
      current,
      { id, targetId, kind, text },
    ))
    const timer = window.setTimeout(() => {
      feedbackTimersRef.current.delete(id)
      setCombatFeedback(current => current.filter(item => item.id !== id))
    }, combatFeedbackLifetimeMs(kind))
    feedbackTimersRef.current.set(id, timer)
  }, [])
  const clearCombatFeedback = useCallback(() => {
    for (const timer of feedbackTimersRef.current.values()) window.clearTimeout(timer)
    feedbackTimersRef.current.clear()
    setCombatFeedback([])
  }, [])
  useEffect(() => () => {
    for (const timer of feedbackTimersRef.current.values()) window.clearTimeout(timer)
    feedbackTimersRef.current.clear()
  }, [])
  // Why: 戦闘フェーズ banner (戦闘開始 / プレイヤーのターン / 敵のターン / 勝利)。
  // Map screen の ACT START と同じ 2 秒帯フォーマットで画面中央にオーバーレイ。
  // banner 表示中は他のアニメーション/入力を遅延させる (await showBanner)。
  const [phaseBanner, setPhaseBanner] = useState<string | null>(null)
  const [characterSkillBurst, setCharacterSkillBurst] = useState<CharacterSkillBurst | null>(null)
  const skillBurstSequenceRef = useRef(0)
  const skillBurstTimerRef = useRef<number | null>(null)
  const showCharacterSkillBurst = useCallback((
    kind: CharacterSkillBurst['kind'],
    characterId: string,
    characterClass: CharacterSoundClass,
    name: string,
    playbackSpeed = 1,
    motionEnabled = true,
  ): number => {
    if (skillBurstTimerRef.current !== null) window.clearTimeout(skillBurstTimerRef.current)
    const id = ++skillBurstSequenceRef.current
    const durationMs = characterSkillCutinDurationMs(kind, playbackSpeed, motionEnabled)
    setCharacterSkillBurst({
      id,
      kind,
      characterId,
      characterClass,
      name,
      portrait: resolveCharacterPortrait(characterId),
      durationMs,
    })
    skillBurstTimerRef.current = window.setTimeout(() => {
      skillBurstTimerRef.current = null
      setCharacterSkillBurst(current => current?.id === id ? null : current)
    }, durationMs)
    return durationMs
  }, [])
  useEffect(() => () => {
    if (skillBurstTimerRef.current !== null) window.clearTimeout(skillBurstTimerRef.current)
  }, [])
  // resolvedRef は finalize 後の二重呼び出しを抑止するためのラッチ。
  const resolvedRef = useRef(false)
  // Why: hand から消えたカードを exit アニメーション中だけ残しておくバッファ。
  // 表示中はクリック不可 (onClick 無効)、CSS で .is-leaving が捨札方向へ
  // 縮小フェードする。タイマーで自動削除。
  // destDx/destDy は検出時に計測した「カード中心 → 行先パイル中心」の差分。
  // .is-leaving の keyframe が --leave-dx / --leave-dy として読む。
  type LeavingHandEntry = {
    card: BattleCardInstanceDto
    demo: HandCardDemo
    fan: { x: number; y: number; r: number }
    destDx: number
    destDy: number
    destination: 'discard' | 'exhaust'
  }
  const [leavingHand, setLeavingHand] = useState<LeavingHandEntry[]>([])
  type ExhaustingCardEntry = {
    id: string
    demo: HandCardDemo
    startX: number
    startY: number
    startRot: number
  }
  const [exhaustingCards, setExhaustingCards] = useState<ExhaustingCardEntry[]>([])
  const prevHandRef = useRef<{
    initialized: boolean
    list: BattleCardInstanceDto[]
    demos: HandCardDemo[]
    fans: { x: number; y: number; r: number }[]
    /** M6.6: hand から消えたカードが exhaust pile に入ったか判定するための
     *  前回スナップショット (instanceId 集合)。差分検出で除外行きを識別。 */
    exhaustIds: Set<string>
  }>({
    initialized: false,
    list: [],
    demos: [],
    fans: [],
    exhaustIds: new Set(),
  })

  // Why: 多段階 play アニメ + パイル位置計測用の DOM ref。
  //  - handCardRefs: instanceId → カード要素 (rect 計測でゾーン間移動の dx/dy を出す)
  //  - drawPileRef / discardPileRef / exhaustPileRef: 行先パイル中心の計測用
  //  - handWrapRef: パイル位置不明時の fallback (画面下中央)
  const handCardRefs = useRef<Map<string, HTMLDivElement>>(new Map())
  // 能力（双極など）が付く瞬間に光らせる手札のカード。
  const [augmentingCardIds, setAugmentingCardIds] = useState<ReadonlySet<string>>(() => new Set())
  // Keep the last painted center even after React unmounts the hand node. The
  // state diff that identifies a random/chosen exhaust runs after that commit.
  const handCardVisuals = useRef<Map<string, { x: number; y: number; rot: number }>>(new Map())
  const drawPileRef = useRef<HTMLButtonElement | null>(null)
  const discardPileRef = useRef<HTMLButtonElement | null>(null)
  const exhaustPileRef = useRef<HTMLButtonElement | null>(null)
  const handWrapRef = useRef<HTMLDivElement | null>(null)

  // Why: 多段階 play アニメ (centering → holding → leaving) の transient state。
  //  state.hand から消えても LeavingHand とは別ルートで管理し、Stage 1/2/3 を
  //  順に切り替える。leavingHand 検出側からは playingId で除外する。
  // Why: 多段階 play アニメは絶対画面座標で表現する (前バージョンは fan-x +
  //  fan-r 回転 pivot 50%/140% に依存して transform interpolation が歪み、
  //  カード位置によって最終座標がズレるバグが発生していた)。startX/Y は
  //  hand card の visual center、centerX/Y は viewport 中央、destX/Y は
  //  destination pile の visual center。playing-card は position: fixed で
  //  全て screen coords に直接配置 → 数学が単純で fan layout の影響を受けない。
  type PlayingCardState = {
    entry: LeavingHandEntry
    stage: 'idle' | 'centering' | 'holding' | 'leaving'
    startX: number
    startY: number
    startRot: number
    centerX: number
    centerY: number
    destX: number
    destY: number
  }
  const [playingCard, setPlayingCard] = useState<PlayingCardState | null>(null)

  // Why: 山札補充 (discard → draw 全消化) の弧アーチ演出用。
  //  prev.discardPile.length > 0 && current === 0 && draw 増加 → トリガ。
  //  各 sprite は捨札中心 → 山札中心へ 700ms かけて飛ぶ (中間で +Y -80px)。
  //  card には pre.discardPile スナップショットを保持し、各 sprite を
  //  実際の <Card> (face-up) として描画する。
  type ReshuffleEntry = {
    id: number
    card: BattleCardInstanceDto
    fromX: number
    fromY: number
    toX: number
    toY: number
    delay: number
  }
  const [reshuffleEntries, setReshuffleEntries] = useState<ReshuffleEntry[]>([])

  // Why: 1 枚ドローの飛行演出用。山札アイコン中心 → 手札の予定 fan 位置へ
  //  カード sprite を飛ばす。playSteps の stagger ループで 1 枚ずつトリガし、
  //  flight 終了タイミングで実 hand state にコミット (Card は通常通り mount)。
  type DrawAnimEntry = {
    id: number
    card: BattleCardInstanceDto
    fromX: number
    fromY: number
    toX: number
    toY: number
    rot: number
  }
  const [drawAnimEntries, setDrawAnimEntries] = useState<DrawAnimEntry[]>([])

  const { catalog: cardCatalog } = useCardCatalog()
  const { catalog: enemyCatalog } = useEnemyCatalog()
  const { catalog: unitCatalog } = useUnitCatalog()
  const { catalog: characterCatalog } = useCharacterCatalog()

  // Phase 10.5.M2-Choose T7 follow-up (I3): catalog 由来の resolver / map を memoize し、
  //  CardChoiceModal の不要な re-render を抑制する。
  const cardNamesMemo = useMemo<Record<string, string>>(() => {
    if (!cardCatalog) return {}
    const out: Record<string, string> = {}
    for (const [id, def] of Object.entries(cardCatalog)) {
      out[id] = def.displayName ?? def.name ?? id
    }
    return out
  }, [cardCatalog])

  const cardTypeOf = useCallback(
    (id: string): CardType =>
      ((cardCatalog?.[id]?.cardType ?? 'skill').toLowerCase()) as CardType,
    [cardCatalog],
  )
  const cardRarityOf = useCallback(
    (id: string): CardRarity => rarityCodeFromNumber(cardCatalog?.[id]?.rarity ?? 1),
    [cardCatalog],
  )
  // Phase 10.5.M2-Choose T7 follow-up (I1): resolveCardChoice 失敗時のエラーを
  //  CardChoiceModal 内 banner として表示するための state。
  const [chooseError, setChooseError] = useState<string | null>(null)

  // Why: state が更新されるたび親 (MapScreen) に通知。peek 中も TopBar が live
  // battle state (HP / potions) を表示し続けるための仕組み (ユーザ要望)。
  useEffect(() => {
    onBattleStateChange?.(state)
  }, [state, onBattleStateChange])

  // A pending Contingency choice can outlive the response that carried its
  // trigger (for example after reconnect). Rebuild the held centre card from
  // the authoritative pending choice and Exhaust-zone card instead of relying
  // on transient animation history.
  useEffect(() => {
    if (!state || replayView || animating) return
    const pending = findPendingContingencyCard(state, cardCatalog)
    if (!pending) return
    const pendingId = `contingency:${pending.card.instanceId}`
    const appStage = document.querySelector('.app-stage') as HTMLElement | null
    const exhaustCenter = resolveBattleElementCenter(
      exhaustPileRef.current?.getBoundingClientRect(),
      appStage?.getBoundingClientRect(),
      { x: 1216, y: 558 },
    )
    setReenactCard(current => current?.id === pendingId
      ? { ...current, stage: 'holding' }
      : {
          id: pendingId,
          demo: {
            ...toHandCardDemo(pending.card, cardCatalog, state.energy),
            instanceId: pending.card.instanceId,
          },
          stage: 'holding',
          movement: pending.destination === 'exhaust'
            ? 'contingency-exhaust'
            : 'contingency-discard',
          fromX: exhaustCenter.x,
          fromY: exhaustCenter.y,
        })
  }, [animating, cardCatalog, replayView, state])

  useEffect(() => {
    if (!state) return
    const current = findPrimaryParticipant(state)
    if (!current) return
    const previous = previousPrimaryReadyRef.current
    if (
      previous
      && previous.turn === state.turn
      && previous.participantId === current.participantId
      && previous.ready
      && !current.isReady
    ) {
      if (manualUnreadyRef.current) {
        manualUnreadyRef.current = false
      } else if (state.phase === 'PlayerInput') {
        setReadyNotice('新しく行える行動が増えたため、READYが解除されました。')
      }
    }
    previousPrimaryReadyRef.current = {
      turn: state.turn,
      participantId: current.participantId,
      ready: current.isReady,
    }
  }, [state])

  useEffect(() => {
    if (!readyNotice) return
    const timer = window.setTimeout(() => setReadyNotice(null), 3600)
    return () => window.clearTimeout(timer)
  }, [readyNotice])

  // Why: state.hand / exhaustPile が更新された瞬間にゾーン移動を検出する。
  // 捨札行きは leavingHand、除外行きは viewport中央を基準にした専用overlayへ
  // 分ける。後者を hand 内の相対transformで動かすと、選択位置やfan回転、
  // app-stageのscaleによって画面左へずれていた。
  // 注意: hooks は条件付き呼び出し不可なので state===null 早期 return より
  // 前で宣言する。state===null のときは body 冒頭で no-op return する。
  useEffect(() => {
    if (!state) return
    const hand = state.hand
    const prev = prevHandRef.current
    const currentIds = new Set(hand.map(c => c.instanceId))
    const newlyLeaving: LeavingHandEntry[] = []
    const currentExhaustIds = new Set(state.exhaustPile.map(c => c.instanceId))
    const newlyExhaustedIds = new Set<string>()
    if (prev.initialized) {
      currentExhaustIds.forEach(id => {
        if (!prev.exhaustIds.has(id)) newlyExhaustedIds.add(id)
      })
    }
    // Why: ゾーン間移動の終点は捨札パイル中心 (B 要件)。要素 rect が取れる場合は
    // それを使い、取れない場合は手札 wrap 中央を fallback にする。
    const playingId = playingCard?.entry.card.instanceId
    const handRect = handWrapRef.current?.getBoundingClientRect() ?? null
    const fallbackHandCx = handRect ? handRect.left + handRect.width / 2 : window.innerWidth / 2
    const fallbackHandCy = handRect ? handRect.top + handRect.height / 2 : window.innerHeight - 100
    const discardRect = discardPileRef.current?.getBoundingClientRect() ?? null
    const discardCx = discardRect ? discardRect.left + discardRect.width / 2 : 0
    const discardCy = discardRect ? discardRect.top + discardRect.height / 2 : 0
    prev.list.forEach((card, idx) => {
      if (currentIds.has(card.instanceId)) return
      // playingCard が引き取るカードは leavingHand から除外 (二重アニメ防止)。
      if (card.instanceId === playingId) return
      // Exhausted cards are rendered by the fixed overlay below, never by a
      // position inherited from the hand row.
      if (newlyExhaustedIds.has(card.instanceId)) return
      const visual = handCardVisuals.current.get(card.instanceId)
      const cardCx = visual?.x ?? fallbackHandCx
      const cardCy = visual?.y ?? fallbackHandCy
      newlyLeaving.push({
        card,
        demo: prev.demos[idx],
        fan: prev.fans[idx],
        destDx: discardCx - cardCx,
        destDy: discardCy - cardCy,
        destination: 'discard',
      })
    })
    if (newlyLeaving.length > 0) {
      playBattleCue('cardDiscard', newlyLeaving[0].card.instanceId)
      setLeavingHand(prevList => [...prevList, ...newlyLeaving])
      const ids = newlyLeaving.map(e => e.card.instanceId)
      window.setTimeout(() => {
        setLeavingHand(prevList =>
          prevList.filter(e => !ids.includes(e.card.instanceId)),
        )
      }, CARD_LEAVE_MS)
    }
    if (newlyExhaustedIds.size > 0) {
      const appStageEl = document.querySelector('.app-stage') as HTMLElement | null
      const appRect = appStageEl?.getBoundingClientRect()
      const appScaleX = appRect && appRect.width > 0 ? appRect.width / 1280 : 1
      const appScaleY = appRect && appRect.height > 0 ? appRect.height / 720 : 1
      const appLeft = appRect?.left ?? 0
      const appTop = appRect?.top ?? 0
      const createdAt = Date.now()
      const newAnimations: ExhaustingCardEntry[] = []
      state.exhaustPile.forEach((card, order) => {
        if (!newlyExhaustedIds.has(card.instanceId) || card.instanceId === playingId) return
        const prevIndex = prev.list.findIndex(item => item.instanceId === card.instanceId)
        const visual = handCardVisuals.current.get(card.instanceId)
        const screenX = visual?.x ?? fallbackHandCx
        const screenY = visual?.y ?? fallbackHandCy
        newAnimations.push({
          id: `${card.instanceId}:${createdAt}:${order}`,
          demo: prevIndex >= 0
            ? prev.demos[prevIndex]
            : toHandCardDemo(card, cardCatalog, state.energy),
          startX: (screenX - appLeft) / appScaleX,
          startY: (screenY - appTop) / appScaleY,
          startRot: visual?.rot ?? (prevIndex >= 0 ? prev.fans[prevIndex]?.r ?? 0 : 0),
        })
      })
      if (newAnimations.length > 0) {
        playBattleCue('cardExhaust', newAnimations[0].id)
        setExhaustingCards(current => [...current, ...newAnimations])
        const animationIds = new Set(newAnimations.map(item => item.id))
        window.setTimeout(() => {
          setExhaustingCards(current => current.filter(item => !animationIds.has(item.id)))
        }, CARD_LEAVE_MS)
      }
    }
    // 次回比較用に現在の hand / demos / fans / exhaustIds を ref に保存。demos と
    // fans は render 内の handDemos / fan と同じ計算で生成 (cardCatalog 等は dep に
    // 入れず最新値を読む)。
    const newDemos = hand.map((c) =>
      toHandCardDemo(
        c,
        cardCatalog,
        state.energy,
      ),
    )
    const newFans = fanLayout(newDemos.length)
    prevHandRef.current = {
      initialized: true,
      list: hand.slice(),
      demos: newDemos,
      fans: newFans,
      exhaustIds: currentExhaustIds,
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [state?.hand, state?.exhaustPile, cardCatalog, playBattleCue])

  // Why: 山札補充は playSteps 内で「部分ドロー → reshuffle 演出 → 残りドロー」
  //  の 3 段階表示を組み立てるため、setState 後 useEffect 検出ではなく
  //  playSteps から直接 triggerReshuffleAnim を呼ぶ方式に変更。
  const triggerReshuffleAnim = useCallback((cards: BattleCardInstanceDto[]) => {
    if (cards.length === 0) return
    playBattleCue('cardShuffle', cards[0].instanceId)
    const drawEl = drawPileRef.current
    const discEl = discardPileRef.current
    if (!drawEl || !discEl) return
    const dr = drawEl.getBoundingClientRect()
    const er = discEl.getBoundingClientRect()
    // Why: handlePlayCard と同じく .app-stage scale を考慮した local 座標へ変換。
    //  reshuffle sprite は position: fixed で .app-stage 内 containing block に
    //  寄り添うため、screen 座標をそのまま渡すと sprite が画面外に飛んでいた。
    const appStageEl = document.querySelector('.app-stage') as HTMLElement | null
    const appRect = appStageEl?.getBoundingClientRect()
    const appScaleX = appRect ? appRect.width / 1280 : 1
    const appScaleY = appRect ? appRect.height / 720 : 1
    const appLeft = appRect ? appRect.left : 0
    const appTop = appRect ? appRect.top : 0
    const fromScreenX = er.left + er.width / 2
    const fromScreenY = er.top + er.height / 2
    const toScreenX = dr.left + dr.width / 2
    const toScreenY = dr.top + dr.height / 2
    const fromX = (fromScreenX - appLeft) / appScaleX
    const fromY = (fromScreenY - appTop) / appScaleY
    const toX = (toScreenX - appLeft) / appScaleX
    const toY = (toScreenY - appTop) / appScaleY
    const base = Date.now()
    // Why: stagger 60→110ms / sprite 700ms (CSS と一致)。1 枚 1 枚が
    //  視認できる速度に減速 (ユーザ要望)。
    const stagger = 110
    const duration = 700
    const entries: ReshuffleEntry[] = cards.map((card, i) => ({
      id: base + i,
      card,
      fromX,
      fromY,
      toX,
      toY,
      delay: i * stagger,
    }))
    setReshuffleEntries(entries)
    window.setTimeout(() => {
      setReshuffleEntries([])
    }, cards.length * stagger + duration + 50)
  }, [playBattleCue])

  // Why: 1 枚を山札アイコン → 手札の予定 fan 位置へ飛ばす。返り値は flight 時間 (ms)。
  //  - playSteps の stagger ループで「triggerDrawAnim → sleep(flight) → setState (hand に追加)」を 1 サイクル。
  //  - 目標は `.hand-wrap` 中心 + fanLayout(newHandSize)[targetIndex]。
  //  - reshuffle と同じく .app-stage の transform を考慮した local 座標で渡す。
  const DRAW_FLIGHT_MS = 260
  const triggerDrawAnim = useCallback((
    card: BattleCardInstanceDto,
    targetIndex: number,
    newHandSize: number,
  ) => {
    playBattleCue('cardDraw', card.instanceId)
    const drawEl = drawPileRef.current
    const handEl = handWrapRef.current
    if (!drawEl || !handEl) return
    const dr = drawEl.getBoundingClientRect()
    const hr = handEl.getBoundingClientRect()
    const appStageEl = document.querySelector('.app-stage') as HTMLElement | null
    const appRect = appStageEl?.getBoundingClientRect()
    const appScaleX = appRect ? appRect.width / 1280 : 1
    const appScaleY = appRect ? appRect.height / 720 : 1
    const appLeft = appRect ? appRect.left : 0
    const appTop = appRect ? appRect.top : 0
    const fromX = (dr.left + dr.width / 2 - appLeft) / appScaleX
    const fromY = (dr.top + dr.height / 2 - appTop) / appScaleY
    const handCx = (hr.left + hr.width / 2 - appLeft) / appScaleX
    const handCy = (hr.top + hr.height / 2 - appTop) / appScaleY
    const targetFan = fanLayout(newHandSize)[targetIndex] ?? { x: 0, y: 0, r: 0 }
    const toX = handCx + targetFan.x
    const toY = handCy + targetFan.y
    const id = Date.now() + Math.random()
    setDrawAnimEntries(prev => [...prev, {
      id, card, fromX, fromY, toX, toY, rot: targetFan.r,
    }])
    window.setTimeout(() => {
      setDrawAnimEntries(prev => prev.filter(e => e.id !== id))
    }, DRAW_FLIGHT_MS + 30)
  }, [playBattleCue])

  // 戦闘フェーズ banner: text を 2 秒間オーバーレイ表示してから clear。
  // Map の ACT START と同じ 2s フェード仕様 (CSS keyframes 側で 10/88/100% 遷移)。
  const showBanner = useCallback(async (text: string) => {
    setPhaseBanner(text)
    await sleep(2000)
    setPhaseBanner(null)
  }, [])

  // 戦闘終了 → /finalize 呼び出し → 親に通知。
  const handleFinalize = useCallback(async () => {
    if (resolvedRef.current) return
    resolvedRef.current = true
    try {
      const result = await finalizeBattle(accountId)
      onBattleResolved(result)
    } catch (e) {
      resolvedRef.current = false
      setError((e as Error).message)
    }
  }, [accountId, onBattleResolved])

  // BattleActionResponse を再生する。
  // 設計 (ユーザ要望):
  //  - Core event の HitIndex ごとに slide アニメを1回再生する。
  //    All は同じ HitIndex を全対象で共有するため、全被弾 slot を同時 flash。
  //    Single / Random の連撃は HitIndex が異なるため、実ヒット数だけ再生する。
  //  - HP 表示は damageOverlay (instanceId → 累積 damage) で progressive に
  //    減らす。playSteps 中は setState を呼ばず最後にまとめて確定するため、
  //    後続 attacker の最終 state が前 attacker の演出に染み出さない。
  //  - ActorDeath: 撃破点滅 → dyingIds から削除。HP=0 は damageOverlay に
  //    反映済 + state 未確定なので alive filter のため killedIds を別管理する。
  //  - その他 event (BattleStart / TurnStart / GainBlock / Draw / etc.):
  //    短い fallback delay。HP に影響しないので state 未確定でも見た目は同等。
  const playSteps = useCallback(
    async (resp: BattleActionResponseDto, options: PlayStepsOptions = {}) => {
      const presentationOnly = options.presentationOnly === true
      const baselineState = options.baselineState ?? state
      const playbackSpeed = Math.max(0.25, options.speed ?? 1)
      const motionScale = effectiveMotionLevel === 'off'
        ? 0
        : effectiveMotionLevel === 'reduced' ? 0.48 : 1
      const isCancelled = options.isCancelled ?? (() => false)
      const waitForPresentation = async (durationMs: number) => {
        await sleep(Math.round(durationMs * motionScale / playbackSpeed))
        return !isCancelled()
      }
      // スキルカードのモーションが終わる時刻。最後の片付けで途中のコマが消えないよう、ここまで待つ。
      let cardMotionEndsAt = 0
      /**
       * カードの使用・伏策の発動のモーションを再生し、効果を出す位置（release）まで待つ。
       * 残りのコマは並行して流す。中断されたら false。シートが無い・モーション off なら何もしない。
       */
      const playCardMotion = async (casterId: string, motion: CardMotion): Promise<boolean> => {
        const sheet = actorAnimations(casterId)?.[motion]
        if (!sheet || effectiveMotionLevel === 'off') return true
        const motionDurationMs = readableMotionDuration(sheet, 1)
        const releaseMs = Math.round(motionDurationMs * SKILL_CARD_MOTION_RELEASE_RATIO[motion])
        setSlotAnim({ cardMotion: { casterId, motion }, playbackSpeed })
        if (!await waitForPresentation(releaseMs)) return false
        const restMs = Math.round((motionDurationMs - releaseMs) * motionScale / playbackSpeed)
        cardMotionEndsAt = performance.now() + restMs
        window.setTimeout(() => {
          setSlotAnim(current => current.cardMotion?.casterId === casterId
            ? { ...current, cardMotion: undefined }
            : current)
        }, restMs)
        return true
      }
      const finishCardMotion = async () => {
        const restMs = cardMotionEndsAt - performance.now()
        if (restMs > 0) await sleep(restMs)
      }
      const clearTransientPresentation = () => {
        setSlotAnim({})
        setDamageOverlay({})
        setBlockOverlay({})
        setReenactCard(null)
        clearCombatFeedback()
        setAnimating(false)
      }
      setAnimating(true)
      const steps = resp.steps
      // Why: 初回 (state===null) の場合、setState せずに banner を流すと
      // loading 画面 (state===null gate) が後ろに残って banner が見えない。
      // 初回は最初に resp.state を設定して battle UI を出してから演出に入る。
      // 初回 response (battle start) に DealDamage は含まれないため、
      // setState(resp.state) しても progressive HP は問題にならない。
      if (!presentationOnly && state === null) {
        setState(resp.state)
      }
      // Why: playSteps 中は setState を呼ばず、ローカル overlay を更新して
      // setDamageOverlay / setBlockOverlay でレンダーに反映させる。
      // これにより同一 turn 内で attacker A → attacker B と続く時、A の
      // 演出中に B の damage が反映 (= HP が落ちる/敵が消える) する不具合
      // を回避できる。block も同じ仕組みで progressive に減らす。
      const dmg: Record<string, number> = {}
      const blk: Record<string, number> = {}
      const blockStateByActor: Record<string, AnimatedBlockState> = {}
      const baselineActor = (actorInstanceId: string) => baselineState
        ? [...baselineState.allies, ...baselineState.enemies]
            .find(candidate => candidate.instanceId === actorInstanceId)
        : undefined
      const actorAnimations = (actorInstanceId: string | undefined) => {
        if (!actorInstanceId) return undefined
        const actor = baselineActor(actorInstanceId)
        if (!actor) return undefined
        const participant = baselineState
          ? findParticipantForActor(baselineState, actorInstanceId)
          : null
        return toCharacterDemo(
          actor,
          { enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog },
          accountId,
          participant?.characterId ?? snapshot.run.characterId,
        ).animations
      }
      const actorMotionDuration = (
        actorInstanceId: string | undefined,
        motion: CharacterMotion,
        minimumMs: number,
      ) => readableMotionDuration(actorAnimations(actorInstanceId)?.[motion], minimumMs)
      const isSkillCard = (cardId: string | null) =>
        !!cardId && (cardCatalog?.[cardId]?.cardType ?? '').toLowerCase() === 'skill'
      const nextAttackMotion = (actorInstanceId: string | undefined): AttackMotion => {
        if (!actorInstanceId || !actorAnimations(actorInstanceId)?.attackAlt) return 'attack'
        const count = attackMotionCountRef.current.get(actorInstanceId) ?? 0
        attackMotionCountRef.current.set(actorInstanceId, count + 1)
        return count % 2 === 0 ? 'attack' : 'attackAlt'
      }
      const currentBlockState = (actorInstanceId: string) => {
        if (Object.prototype.hasOwnProperty.call(blockStateByActor, actorInstanceId)) {
          return blockStateByActor[actorInstanceId]
        }
        const actor = baselineActor(actorInstanceId)
        const dexterity = actor?.statuses?.dexterity ?? 0
        const addCount = actor?.blockAddCount ?? 0
        const initial: AnimatedBlockState = {
          rawSum: actor?.blockRawSum
            ?? Math.max(0, (actor?.blockDisplay ?? 0) - addCount * dexterity),
          addCount,
          dexterity,
        }
        blockStateByActor[actorInstanceId] = initial
        return initial
      }
      const commitBlockOverlay = (actorInstanceId: string) => {
        const baselineDisplay = baselineActor(actorInstanceId)?.blockDisplay ?? 0
        blk[actorInstanceId] = baselineDisplay
          - displayAnimatedBlock(currentBlockState(actorInstanceId))
      }
      const applyRuntimeStatusChange = (
        actorInstanceId: string,
        statusId: string | undefined,
        amount: number | undefined,
        removed: boolean | undefined,
      ) => {
        if (statusId !== 'dexterity') return
        const current = currentBlockState(actorInstanceId)
        blockStateByActor[actorInstanceId] = {
          ...current,
          dexterity: updateDisplayedDexterity(
            current.dexterity,
            statusId,
            amount,
            removed,
          ),
        }
        commitBlockOverlay(actorInstanceId)
      }
      const pileCenter = (
        element: HTMLElement | null,
        fallback: { x: number; y: number },
      ) => {
        const pileRect = element?.getBoundingClientRect()
        const appRect = document.querySelector('.app-stage')?.getBoundingClientRect()
        return resolveBattleElementCenter(pileRect, appRect, fallback)
      }
      // 敵が行うバフ・デバフ・ブロック・回復は発光を見せる間だけ長めに待つ。
      const supportEffectDelay = (casterInstanceId: string | null | undefined) =>
        casterInstanceId && baselineActor(casterInstanceId)?.side === 'Enemy'
          ? ENEMY_SUPPORT_EFFECT_MS
          : STEP_DELAY_MS
      // 効果の途中のドロー・山札再構築は、そのイベントの位置で演出する（ターン開始時の
      // ドローは従来どおり最後にまとめて演出）。手札・山札・捨札以外は行動前の表示のまま。
      const inlineDrawActorId = !presentationOnly && state
        ? findPrimaryParticipant(state)?.actorInstanceId
          ?? state.allies.find(actor => actor.definitionId === 'hero')?.instanceId
        : undefined
      const inlineDraw = (() => {
        if (presentationOnly || !state) return null
        if (steps.some(candidate => candidate.event.kind === 'TurnStart')) return null
        if (!hasInlineDrawEvent(steps, inlineDrawActorId)) return null
        const previousHandIds = new Set(state.hand.map(card => card.instanceId))
        const drawn = resp.state.hand.filter(card => !previousHandIds.has(card.instanceId))
        const drawnIds = new Set(drawn.map(card => card.instanceId))
        const previousDrawIds = new Set(state.drawPile.map(card => card.instanceId))
        const afterReshuffle = drawn.filter(card => !previousDrawIds.has(card.instanceId))
        const reshuffled = hasShufflePresentationTrigger(steps, inlineDrawActorId)
          && state.discardPile.length > 0
          && afterReshuffle.length + resp.state.drawPile.length > 0
        const reshuffleCards = reshuffled ? [...resp.state.drawPile, ...afterReshuffle] : null
        const survivingHand = resp.state.hand.filter(card => !drawnIds.has(card.instanceId))
        return {
          plan: planInlineDraws({
            steps,
            actorInstanceId: inlineDrawActorId,
            newlyDrawn: drawn,
            survivingHand,
            preDrawPile: state.drawPile,
            preDiscardPile: state.discardPile,
            reshuffleCards,
          }),
          initial: {
            hand: survivingHand,
            drawPile: state.drawPile,
            discardPile: reshuffleCards ?? state.discardPile,
          } satisfies DrawPileState,
        }
      })()
      let inlineDrawStarted = false
      const commitInlinePiles = (piles: DrawPileState) => {
        const base = state
        if (!base) return
        setState({
          ...base,
          hand: piles.hand,
          drawPile: piles.drawPile,
          discardPile: piles.discardPile,
          participants: base.participants?.map(participant =>
            participant.actorInstanceId === inlineDrawActorId
              ? {
                  ...participant,
                  hand: piles.hand,
                  drawPile: piles.drawPile,
                  discardPile: piles.discardPile,
                }
              : participant),
        })
      }
      const runInlineDrawAction = async (action: InlineDrawAction) => {
        if (!inlineDraw) return
        if (!inlineDrawStarted) {
          inlineDrawStarted = true
          commitInlinePiles(inlineDraw.initial)
          await sleep(60)
        }
        if (action.kind === 'reshuffle') {
          await sleep(220)
          triggerReshuffleAnim(action.cards)
          await sleep(action.cards.length * 110 + 700)
          commitInlinePiles(action.stateAfter)
          return
        }
        await new Promise<void>(resolveAll => {
          let remaining = action.cards.length
          action.cards.forEach((card, index) => {
            window.setTimeout(() => {
              const after = action.states[index]
              triggerDrawAnim(card, after.hand.length - 1, after.hand.length)
              window.setTimeout(() => {
                commitInlinePiles(after)
                remaining -= 1
                if (remaining === 0) resolveAll()
              }, DRAW_FLIGHT_MS)
            }, index * 90)
          })
        })
      }

      // Why: 「敵のターン」 banner は 1 turn につき 1 回 (味方攻撃終了 → 初の
      // 敵 caster AttackFire の前) で出す。表示済フラグでガードする。
      let shownEnemyTurnBanner = false

      let i = 0
      while (i < steps.length) {
        const step = steps[i]
        const ev = step.event

        // Live card-zone sounds are owned by the animation that actually moves
        // the card. Replay presentation has no live zone watcher, so its
        // journal events remain audible here.
        const deferredToLiveZoneAnimation = !presentationOnly && (
          ev.kind === 'Draw'
          || ev.kind === 'Shuffle'
          || ev.kind === 'Discard'
          || ev.kind === 'Exhaust'
        )
        if (ev.kind !== 'AttackFire' && !deferredToLiveZoneAnimation) {
          const seed = `${ev.order}:${ev.targetInstanceId ?? ev.cardId ?? ''}`
          const duckPreset = bgmDuckPresetForEvent(ev)
          if (duckPreset) duckBgm(BGM_DUCK_PRESETS[duckPreset])

          if (ev.kind === 'UseCharacterSkill') {
            const participant = baselineState?.participants?.find(candidate =>
              candidate.actorInstanceId === ev.casterInstanceId)
            const characterId = participant?.characterId ?? snapshot.run.characterId
            const characterClass = toCharacterSoundClass(
              characterCatalog?.[characterId]?.characterClass,
            )
            const kind = ev.note?.toLowerCase() === 'ultimate' ? 'ultimate' : 'tactical'
            const skillName = (
              characterCatalog?.[characterId]?.skills?.find(skill => skill.id === kind)?.name
            ) ?? (kind === 'ultimate' ? '奥義' : '戦技')
            const cutinDurationMs = showCharacterSkillBurst(
              kind,
              characterId,
              characterClass,
              skillName,
              playbackSpeed,
              effectiveMotionLevel !== 'off',
            )
            void playLayeredSe(selectCharacterSkillSound(kind, characterClass, seed, {
              pan: -0.28,
            }))
            // The cut-in is an inserted activation beat, not a decorative
            // overlay. Finish its equally timed entrance/exit before the
            // following skill effects begin behind it.
            await sleep(cutinDurationMs)
            if (isCancelled()) {
              clearTransientPresentation()
              return
            }
            // カットインの後、発動したキャラが奥義モーションを再生する。
            // 放つ瞬間（release、8 コマ中 5 コマ目）まで待ってから効果へ進み、残りは効果と並行して流す。
            const casterId = ev.casterInstanceId ?? undefined
            const skillSheet = actorAnimations(casterId)?.skill
            // 静のキャラは奥義の動きもしない（カットインと効果はそのまま）。
            if (casterId && skillSheet && effectiveMotionLevel !== 'off'
              && baselineActor(casterId)?.phase !== 'calm') {
              const skillDurationMs = readableMotionDuration(skillSheet, 1)
              const releaseMs = Math.round(skillDurationMs * SKILL_RELEASE_RATIO)
              setSlotAnim({ skillCasterId: casterId, playbackSpeed })
              if (!await waitForPresentation(releaseMs)) {
                clearTransientPresentation()
                return
              }
              const restMs = Math.round((skillDurationMs - releaseMs) * motionScale / playbackSpeed)
              window.setTimeout(() => {
                setSlotAnim(current => current.skillCasterId === casterId
                  ? { ...current, skillCasterId: undefined }
                  : current)
              }, restMs)
            }
          } else {
            const recipe = battleSoundRecipeForEvent(ev)
            if (recipe) {
              void playLayeredSe(selectBattleSoundRecipe(recipe, seed))
            } else {
              for (const cue of battleAudioCuesForEvent(ev)) playBattleCue(cue, seed)
            }
          }
        }
        const inlineActions = inlineDraw?.plan.actionsByStepIndex.get(i)
        if (inlineActions) {
          for (const action of inlineActions) {
            await runInlineDrawAction(action)
            if (isCancelled()) {
              clearTransientPresentation()
              return
            }
          }
          i++
          continue
        }

        if (ev.kind === 'PlayCard') {
          // Current responses carry the exact reset-before-Finisher value.
          // Legacy responses still receive the required first-tier sound.
          playComboTone(comboToneTier(ev.comboCountAfter) ?? 1)
          // スキルカードを使ったキャラは、防御効果があれば防御モーションで構えてからブロックを得る。
          // それ以外（強化・ドロー等）は発動モーションで力をためてから効果を出す。攻撃するスキルは攻撃モーションに任せる。
          const cardCasterId = ev.casterInstanceId ?? undefined
          // 静のキャラはカードを使っても動かない。そのカードで静が解ける時だけ動く。
          const casterStill = !!cardCasterId
            && baselineActor(cardCasterId)?.phase === 'calm'
            && !playedCardReleasesCalm(steps, i, cardCasterId)
          // 伏策を仕込むカードは、種類に関わらず発動モーション（効果は発動した時に改めて見せる）。
          const armsContingency = !!cardCasterId && playedCardArmsContingency(steps, i, cardCasterId)
          const cardMotion: CardMotion | null = !cardCasterId || casterStill
            ? null
            : armsContingency
              ? 'cast'
              : isSkillCard(ev.cardId)
                ? skillCardMotion(steps, i, cardCasterId)
                : null
          if (cardCasterId && cardMotion && !await playCardMotion(cardCasterId, cardMotion)) {
            clearTransientPresentation()
            return
          }
        }

        // 「戦闘開始」「プレイヤーのターン」 banner: BattleStart / TurnStart
        // event をそのまま trigger にする (engine が emit するタイミング)。
        if (!presentationOnly && ev.kind === 'BattleStart') {
          if (baselineState === null && resp.state.hand.length > 0) {
            playBattleCue('cardDraw', `opening:${resp.state.hand[0].instanceId}`)
          }
          await showBanner('戦闘開始')
          i++
          continue
        }
        if (!presentationOnly && ev.kind === 'TurnStart') {
          await showBanner('プレイヤーのターン')
          i++
          continue
        }

        // 「敵のターン」 banner: 最初の敵 caster の AttackFire 直前に出す。
        // 攻撃 burst を始める前に判定し、敵側ならまず banner を流す。
        if ((ev.kind === 'GainAttack' || ev.kind === 'AttackFire'
            || ev.kind === 'EnemyCardAction')
            && !shownEnemyTurnBanner) {
          const isEnemyCaster = ev.casterInstanceId
            ? step.snapshotAfter.enemies.some(e => e.instanceId === ev.casterInstanceId)
            : false
          if (isEnemyCaster && !presentationOnly) {
            await showBanner('敵のターン')
            shownEnemyTurnBanner = true
          }
        }

        if (ev.kind === 'AttackFire') {
          const hit = readAttackAnimationHit(steps, i)
          if (!hit) {
            i++
            continue
          }
          const casterId = hit.casterId
          const firstTargetActor = hit.targets[0]
            ? baselineActor(hit.targets[0].targetId)
            : undefined
          const attackPan = firstTargetActor
            ? panForBattleSlot(
                firstTargetActor.side === 'Ally' ? 'player' : 'enemy',
                firstTargetActor.slotIndex,
                4,
              )
            : 0
          const contactRecipe = battleContactSoundRecipe(
            hit.targets.reduce((sum, target) => sum + target.blockConsumed, 0),
            hit.targets.reduce((sum, target) => sum + target.damage, 0),
          )
          if (contactRecipe) {
            void playLayeredSe(selectBattleSoundRecipe(
              contactRecipe,
              `${ev.order}:${casterId ?? ''}:${hit.hitIndex ?? 0}`,
              { pan: attackPan },
            ))
          } else {
            playBattleCue('attack', `${ev.order}:${casterId ?? ''}:${hit.hitIndex ?? 0}`)
          }

          // slide 方向は caster side で決定 (現在の state ベースで判定)。
          const side = casterId && baselineState
            ? (baselineState.allies.some(a => a.instanceId === casterId) ? 'Ally' : 'Enemy')
            : undefined

          // Why: damage > 0 の target だけ赤 flash する。block で完全相殺
          // (damage=0) の場合は flash しない (ユーザ仕様: ブロック値だけ減る)。
          const flashTargets = Array.from(new Set(
            hit.targets.filter(target => target.damage > 0).map(target => target.targetId),
          ))
          const blockedTargets = Array.from(new Set(
            hit.targets.filter(target => target.blockConsumed > 0).map(target => target.targetId),
          ))
          // 攻撃モーションは同じキャラの攻撃ごとに A/B を交互に使う（attackAlt が無いキャラは A のみ）。
          const attackMotion = nextAttackMotion(casterId)
          const attackDurationMs = actorMotionDuration(
            casterId,
            attackMotion,
            ATTACK_SLIDE_MS,
          )
          const affectedTargetIds = Array.from(new Set([
            ...flashTargets,
            ...blockedTargets,
          ]))
          const hitDurationMs = affectedTargetIds.reduce(
            (longest, targetId) => Math.max(
              longest,
              actorMotionDuration(targetId, 'hurt', HIT_FLASH_MS),
            ),
            HIT_FLASH_MS,
          )
          // Outer lunge and authored attack sheet share a duration. Contact
          // stays near the lunge apex while retaining the former 180ms
          // minimum, so a longer manifest animation can never be truncated.
          const contactDurationMs = Math.min(
            Math.max(1, attackDurationMs - STEP_DELAY_MS),
            Math.max(ATTACK_CONTACT_MS, Math.round(attackDurationMs * 0.41)),
          )
          const recoveryDurationMs = Math.max(0, attackDurationMs - contactDurationMs)

          // 1論理ヒットにつき1 slide。damage/block もそのヒットの中盤で反映する。
          setSlotAnim({ attackerId: casterId, attackerSide: side, attackMotion, playbackSpeed })
          if (!await waitForPresentation(contactDurationMs)) {
            clearTransientPresentation()
            return
          }
          const hitCues = new Set<BattleAudioCue>()
          for (const change of hit.stateChanges) {
            if (change.hpDelta > 0) hitCues.add('heal')
            if (change.hpDelta < 0) hitCues.add('hurt')
            if (change.gainsBlock && change.blockDelta > 0) hitCues.add('gainBlock')
            if (change.consumesBlock && change.blockDelta < 0) hitCues.add('block')
            const feedbackStatusId = resolveFeedbackStatusId(change.statusId)
            if (feedbackStatusId && !change.statusRemoved) {
              hitCues.add(statusAudioCue(feedbackStatusId, change.statusDelta ?? 0))
            }
          }
          for (const cue of hitCues) {
            playBattleCue(cue, `${ev.order}:${cue}:${hit.hitIndex ?? 0}`)
          }
          for (const target of hit.targets) {
            if (target.blockConsumed > 0) {
              pushCombatFeedback(
                target.targetId,
                'block',
                `BLOCK -${target.blockConsumed}`,
              )
            }
            if (target.damage > 0) {
              pushCombatFeedback(target.targetId, 'damage', `-${target.damage}`)
            } else if (target.blockConsumed > 0) {
              pushCombatFeedback(target.targetId, 'guard', '完全防御')
            }
            if (target.damage > 0) {
              dmg[target.targetId] = (dmg[target.targetId] ?? 0) + target.damage
            }
            blockStateByActor[target.targetId] = consumeAnimatedBlock(
              currentBlockState(target.targetId),
              target.blockConsumed,
            )
            commitBlockOverlay(target.targetId)
          }
          for (const change of hit.stateChanges) {
            if ((change.attackGainAmount ?? 0) > 0) {
              pushCombatFeedback(
                change.targetId,
                'attack',
                attackGainFeedbackText(
                  change.attackGainScope,
                  change.attackGainAmount ?? 0,
                ),
              )
            }
            if (change.hpDelta > 0) {
              pushCombatFeedback(change.targetId, 'heal', `+${change.hpDelta} HP`)
            } else if (change.hpDelta < 0) {
              pushCombatFeedback(change.targetId, 'damage', `${change.hpDelta}`)
            }
            if (change.gainsBlock && change.blockDelta > 0) {
              pushCombatFeedback(
                change.targetId,
                'block',
                blockGainFeedbackText(change.blockDelta),
              )
            }
            if (change.statusId) {
              const feedbackStatusId = resolveFeedbackStatusId(change.statusId)
              const statusKind = statusAudioCue(
                feedbackStatusId,
                change.statusDelta ?? 0,
              ) === 'debuff' ? 'debuff' : 'buff'
              pushCombatFeedback(
                change.targetId,
                statusKind,
                statusFeedbackText(
                  change.statusId,
                  change.statusDelta ?? 0,
                  change.statusRemoved === true,
                ),
              )
            }
            applyRuntimeStatusChange(
              change.targetId,
              resolveFeedbackStatusId(change.statusId) ?? undefined,
              change.statusDelta,
              change.statusRemoved,
            )
            if (change.hpDelta !== 0) {
              dmg[change.targetId] = (dmg[change.targetId] ?? 0) - change.hpDelta
            }
            if (change.gainsBlock) {
              blockStateByActor[change.targetId] = gainAnimatedBlock(
                currentBlockState(change.targetId),
                change.blockDelta,
              )
              commitBlockOverlay(change.targetId)
            } else if (change.consumesBlock) {
              blockStateByActor[change.targetId] = consumeAnimatedBlock(
                currentBlockState(change.targetId),
                Math.max(0, -change.blockDelta),
              )
              commitBlockOverlay(change.targetId)
            }
          }
          setDamageOverlay({ ...dmg })
          setBlockOverlay({ ...blk })
          if (flashTargets.length > 0 || blockedTargets.length > 0) {
            setSlotAnim({
              attackerId: casterId,
              attackerSide: side,
              attackMotion,
              hitIds: flashTargets,
              blockedIds: blockedTargets,
              playbackSpeed,
            })
          }
          if (!await waitForPresentation(recoveryDurationMs)) {
            clearTransientPresentation()
            return
          }
          setSlotAnim(prev => ({ ...prev, attackerId: undefined, attackerSide: undefined }))
          if (flashTargets.length > 0 || blockedTargets.length > 0) {
            if (!await waitForPresentation(Math.max(0, hitDurationMs - recoveryDurationMs))) {
              clearTransientPresentation()
              return
            }
          }
          setSlotAnim({})
          // Commit one neutral frame between consecutive hits. Without this
          // beat React can batch `{}` and the next identical attacker state,
          // leaving the same CSS animation continuously mounted so later hits
          // appear motionless.
          if (!await waitForPresentation(24)) {
            clearTransientPresentation()
            return
          }
          i = hit.nextStepIndex
          continue
        }

        if (ev.kind === 'ReenactCardEffects') {
          if (ev.card) {
            const reenactId = [
              ev.order,
              ev.card.cardInstanceId,
              ev.note ?? '',
            ].join(':')
            setReenactCard({
              id: reenactId,
              demo: toReplayCardDemo(ev.card, cardCatalog),
              stage: 'appearing',
              movement: 'popup',
            })
            if (!await waitForPresentation(16)) {
              clearTransientPresentation()
              return
            }
            setReenactCard(current => current?.id === reenactId
              ? { ...current, stage: 'holding' }
              : current)
            if (!await waitForPresentation(REENACT_APPEAR_MS + REENACT_HOLD_MS)) {
              clearTransientPresentation()
              return
            }
            setReenactCard(current => current?.id === reenactId
              ? { ...current, stage: 'leaving' }
              : current)
            if (!await waitForPresentation(REENACT_LEAVE_MS)) {
              clearTransientPresentation()
              return
            }
            setReenactCard(current => current?.id === reenactId ? null : current)
          } else if (!await waitForPresentation(STEP_DELAY_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        if (ev.kind === 'ContingencyTriggered') {
          if (ev.card) {
            const reenactId = `contingency:${ev.card.cardInstanceId}`
            const exhaustCenter = pileCenter(
              exhaustPileRef.current,
              { x: 1216, y: 558 },
            )
            setReenactCard({
              id: reenactId,
              demo: toReplayCardDemo(ev.card, cardCatalog),
              stage: 'appearing',
              movement: /\[K:exhaust\]/.test(ev.card.description ?? '')
                ? 'contingency-exhaust'
                : 'contingency-discard',
              fromX: exhaustCenter.x,
              fromY: exhaustCenter.y,
              contingencyTrigger: CONTINGENCY_TIMING_LABEL[ev.note ?? ''] ?? '',
              contingencyEffect: contingencyEffectText(ev.card.description),
            })
            if (!await waitForPresentation(16)) {
              clearTransientPresentation()
              return
            }
            setReenactCard(current => current?.id === reenactId
              ? { ...current, stage: 'holding' }
              : current)
            if (!await waitForPresentation(
              REENACT_APPEAR_MS + REENACT_HOLD_MS + CONTINGENCY_TRIGGER_EXTRA_MS)) {
              clearTransientPresentation()
              return
            }
            // 仕込んだキャラが、発動した効果に合わせて動く（攻撃が当たる場合は攻撃の演出に任せる）。
            const triggerCasterId = ev.casterInstanceId
            const triggerCasterStill = !!triggerCasterId
              && baselineActor(triggerCasterId)?.phase === 'calm'
            const triggerMotion = triggerCasterId && !triggerCasterStill
              ? contingencyEffectMotion(steps, i, triggerCasterId)
              : null
            if (triggerCasterId && triggerMotion
                && !await playCardMotion(triggerCasterId, triggerMotion)) {
              clearTransientPresentation()
              return
            }
          } else if (!await waitForPresentation(STEP_DELAY_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        if (ev.kind === 'ContingencyResolved') {
          if (ev.card) {
            const reenactId = `contingency:${ev.card.cardInstanceId}`
            const destination = ev.note === 'exhaust'
              ? 'exhaust'
              : ev.note === 'draw'
                ? 'draw'
                : ev.note === 'hand'
                  ? 'hand'
                  : ev.note === 'discard'
                    ? 'discard'
                    : 'none'
            const exhaustCenter = pileCenter(
              exhaustPileRef.current,
              { x: 1216, y: 558 },
            )
            const destinationCenter = destination === 'exhaust'
              ? exhaustCenter
              : destination === 'draw'
                ? pileCenter(drawPileRef.current, { x: 64, y: 656 })
                : destination === 'hand'
                  ? pileCenter(handWrapRef.current, { x: 640, y: 656 })
                  : destination === 'discard'
                    ? pileCenter(discardPileRef.current, { x: 1216, y: 656 })
                    : { x: 640, y: 360 }
            const movement: ReenactCardState['movement'] = destination === 'exhaust'
              ? 'contingency-exhaust'
              : destination === 'draw'
                ? 'contingency-draw'
                : destination === 'hand'
                  ? 'contingency-hand'
                  : destination === 'discard'
                    ? 'contingency-discard'
                    : 'contingency-none'
            setReenactCard(current => current?.id === reenactId
              ? {
                  ...current,
                  stage: 'holding',
                  movement,
                  toX: destinationCenter.x,
                  toY: destinationCenter.y,
                }
              : {
                  id: reenactId,
                  demo: toReplayCardDemo(ev.card!, cardCatalog),
                  stage: 'holding',
                  movement,
                  fromX: exhaustCenter.x,
                  fromY: exhaustCenter.y,
                  toX: destinationCenter.x,
                  toY: destinationCenter.y,
                })
            if (!await waitForPresentation(16)) {
              clearTransientPresentation()
              return
            }
            setReenactCard(current => current?.id === reenactId
              ? { ...current, stage: 'leaving' }
              : current)
            if (!await waitForPresentation(REENACT_LEAVE_MS)) {
              clearTransientPresentation()
              return
            }
            setReenactCard(current => current?.id === reenactId ? null : current)
          } else if (!await waitForPresentation(STEP_DELAY_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        // State snapshots in the current action DTO are action-final, so show
        // actor resources from the event deltas until that final state commits.
        // Attack-associated deltas are handled as part of the logical hit above.
        // 毒のダメージ（ターン開始時の PoisonTick）: 緑の数字と、体が緑に染まる光。HP の減りも反映する。
        if (ev.kind === 'PoisonTick' && ev.targetInstanceId) {
          const hpLoss = Math.max(0, ev.actualHpLoss ?? ev.amount ?? 0)
          if (hpLoss > 0) {
            pushCombatFeedback(ev.targetInstanceId, 'poison', `-${hpLoss}`)
            showSlotAura(ev.targetInstanceId, 'poison')
          }
          dmg[ev.targetInstanceId] = (dmg[ev.targetInstanceId] ?? 0) + hpLoss
          setDamageOverlay({ ...dmg })
          if (!await waitForPresentation(POISON_TICK_READ_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        if (ev.kind === 'DealDamage' && ev.targetInstanceId) {
          const hpLoss = Math.max(0, ev.actualHpLoss ?? ev.amount ?? 0)
          const blocked = Math.max(0, ev.blockedAmount ?? 0)
          if (blocked > 0) {
            pushCombatFeedback(ev.targetInstanceId, 'block', `BLOCK -${blocked}`)
          }
          if (hpLoss > 0 && ev.damageType === 'Poison') {
            pushCombatFeedback(ev.targetInstanceId, 'poison', `-${hpLoss}`)
            showSlotAura(ev.targetInstanceId, 'poison')
          } else if (hpLoss > 0) {
            pushCombatFeedback(ev.targetInstanceId, 'damage', `-${hpLoss}`)
          } else if (blocked > 0) {
            pushCombatFeedback(ev.targetInstanceId, 'guard', '完全防御')
          }
          dmg[ev.targetInstanceId] = (dmg[ev.targetInstanceId] ?? 0) + hpLoss
          if (ev.damageType === 'Attack') {
            blockStateByActor[ev.targetInstanceId] = consumeAnimatedBlock(
              currentBlockState(ev.targetInstanceId),
              Math.max(0, ev.blockedAmount ?? 0),
            )
            commitBlockOverlay(ev.targetInstanceId)
          }
          setDamageOverlay({ ...dmg })
          setBlockOverlay({ ...blk })
          if (!await waitForPresentation(STEP_DELAY_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        // 敵が予約したカードの実行開始: カード名を頭上に札で見せ、読める間を取る。
        if (ev.kind === 'EnemyCardAction' && ev.casterInstanceId) {
          const cardName = (ev.cardId
            ? cardCatalog?.[ev.cardId]?.displayName ?? cardCatalog?.[ev.cardId]?.name
            : null) ?? ev.cardId ?? '行動'
          const readMs = Math.round(ENEMY_CARD_ACTION_READ_MS * motionScale / playbackSpeed)
          showActionLabel(ev.casterInstanceId, cardName, Math.max(readMs + 900, 1200))
          if (!await waitForPresentation(ENEMY_CARD_ACTION_READ_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        // 攪乱で行動が失敗した: 頭上に「失敗！」を出して、読める間を取る。
        if (ev.kind === 'ActionFailed' && ev.casterInstanceId) {
          pushCombatFeedback(ev.casterInstanceId, 'fail', '失敗！')
          if (!await waitForPresentation(ACTION_FAILED_READ_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        // 位相が変わった瞬間は一時的な光を出す（続く間のオーラは state の phase で描く）。
        // カードに能力（双極など）が付いた: 連続する付与をまとめて、手札のカードを光らせ、
        // 光の途中で付与後の見た目（説明文）に差し替える。
        if (ev.kind === 'CardAugmented') {
          const augmentedIds: string[] = []
          let j = i
          while (j < steps.length && steps[j].event.kind === 'CardAugmented') {
            const id = steps[j].event.targetInstanceId
            if (id && !augmentedIds.includes(id)) augmentedIds.push(id)
            j++
          }
          const inHand = augmentedIds.filter(id => handCardRefs.current.has(id))
          if (inHand.length > 0 && !presentationOnly) {
            if (effectiveMotionLevel !== 'off') {
              setAugmentingCardIds(new Set(inHand))
              window.setTimeout(() => setAugmentingCardIds(new Set()),
                Math.round(CARD_AUGMENT_GLOW_MS * motionScale / playbackSpeed))
              if (!await waitForPresentation(CARD_AUGMENT_GLOW_MS / 2)) {
                clearTransientPresentation()
                return
              }
            }
            const updated = new Map(resp.state.hand
              .filter(card => inHand.includes(card.instanceId))
              .map(card => [card.instanceId, card] as const))
            setState(current => current
              ? { ...current, hand: current.hand.map(card => updated.get(card.instanceId) ?? card) }
              : current)
            if (effectiveMotionLevel !== 'off'
              && !await waitForPresentation(CARD_AUGMENT_GLOW_MS / 2)) {
              clearTransientPresentation()
              return
            }
          }
          i = j
          continue
        }

        if (ev.kind === 'PhaseChanged' && ev.targetInstanceId) {
          showSlotAura(ev.targetInstanceId, 'buff')
          i++
          continue
        }

        if (ev.kind === 'Heal' && ev.targetInstanceId) {
          const amount = Math.max(0, ev.amount ?? 0)
          if (amount > 0) {
            pushCombatFeedback(ev.targetInstanceId, 'heal', `+${amount} HP`)
            showSlotAura(ev.targetInstanceId, 'heal')
          }
          dmg[ev.targetInstanceId] = (dmg[ev.targetInstanceId] ?? 0) - amount
          setDamageOverlay({ ...dmg })
          if (!await waitForPresentation(supportEffectDelay(ev.casterInstanceId))) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        if (ev.kind === 'GainBlock' && ev.targetInstanceId) {
          if ((ev.amount ?? 0) > 0) {
            pushCombatFeedback(
              ev.targetInstanceId,
              'block',
              blockGainFeedbackText(ev.amount ?? 0),
            )
            showSlotAura(ev.targetInstanceId, 'block')
          }
          blockStateByActor[ev.targetInstanceId] = gainAnimatedBlock(
            currentBlockState(ev.targetInstanceId),
            ev.amount ?? 0,
          )
          commitBlockOverlay(ev.targetInstanceId)
          setBlockOverlay({ ...blk })
          if (!await waitForPresentation(supportEffectDelay(ev.casterInstanceId))) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        if (ev.kind === 'GainAttack') {
          const actorInstanceId = ev.casterInstanceId ?? ev.targetInstanceId
          const amount = ev.amount ?? 0
          if (actorInstanceId && amount > 0) {
            pushCombatFeedback(
              actorInstanceId,
              'attack',
              attackGainFeedbackText(ev.note, amount),
            )
          }
          if (!await waitForPresentation(STEP_DELAY_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        if ((ev.kind === 'ApplyStatus' || ev.kind === 'RemoveStatus')
            && ev.targetInstanceId) {
          if (ev.note) {
            const feedbackStatusId = resolveFeedbackStatusId(ev.note)
            const feedbackKind = ev.kind === 'RemoveStatus'
              ? 'buff'
              : statusAudioCue(feedbackStatusId, ev.amount) === 'debuff' ? 'debuff' : 'buff'
            pushCombatFeedback(
              ev.targetInstanceId,
              feedbackKind,
              statusFeedbackText(ev.note, ev.amount ?? 0, ev.kind === 'RemoveStatus'),
            )
            if (ev.kind === 'ApplyStatus') showSlotAura(ev.targetInstanceId, feedbackKind)
          }
          applyRuntimeStatusChange(
            ev.targetInstanceId,
            resolveFeedbackStatusId(ev.note) ?? undefined,
            ev.kind === 'ApplyStatus' ? (ev.amount ?? 0) : undefined,
            ev.kind === 'RemoveStatus',
          )
          setBlockOverlay({ ...blk })
          if (!await waitForPresentation(ev.kind === 'ApplyStatus'
            ? supportEffectDelay(ev.casterInstanceId)
            : STEP_DELAY_MS)) {
            clearTransientPresentation()
            return
          }
          i++
          continue
        }

        if (ev.kind === 'ActorDeath') {
          // Why: ユーザ要望で死亡アニメーションは廃止 (HP=0 で消えるのが
          // 優先される現状のレンダーに対し、後追い演出だと一瞬復活したように
          // 見える)。event は consume するだけで delay も入れない。
          // down シートを持つキャラの倒れる演出は、表示側（HP 0 で down を再生して残す）と
          // 敗北時の待ち（playSteps の最後）で扱う。攻撃のヒット処理がこの event を
          // 先に読み飛ばすことがあるため、ここでは待たない。
          i++
          continue
        }

        // その他 (BattleStart / TurnStart / GainBlock / Draw / etc.)
        // HP 以外のフィールドは playSteps 終了時に state 確定で反映するため、
        // ここでは見た目を変えずに短い delay のみ取る。
        if (!await waitForPresentation(STEP_DELAY_MS)) {
          clearTransientPresentation()
          return
        }
        i++
      }

      // Why: 山札補充 + 複数枚ドローを 1 枚ずつ進行させる。
      //  - reshuffle あり: pre-reshuffle 部分ドロー (1枚ずつ flight) → reshuffle anim →
      //    post-reshuffle 残りドロー (1枚ずつ flight) の 3 段。
      //  - reshuffle なしで複数枚ドロー: 1 枚ずつ flight。
      //  各ドローは triggerDrawAnim で山札アイコン → 予定 fan 位置へカード sprite を
      //  飛ばし、flight 完了で setState して実 hand に追加する。
      //  staging 中は resp.state を baseline にするので HP/energy/status は
      //  最終値で表示される。damageOverlay 等の累積を二重適用しないよう、
      //  staging 開始前にオーバレイをクリアする。
      if (inlineDraw) {
        for (const action of inlineDraw.plan.trailing) await runInlineDrawAction(action)
      }
      if (!presentationOnly && state && !inlineDraw) {
        const drawActorId = findPrimaryParticipant(state)?.actorInstanceId
          ?? state.allies.find(actor => actor.definitionId === 'hero')?.instanceId
        const hasDrawTrigger = hasDrawPresentationTrigger(steps, drawActorId)
        const hasShuffleTrigger = hasShufflePresentationTrigger(steps, drawActorId)
        // ターンをまたぐ時、前ターンの手札は「待機」等で残る札以外は捨札へ行く。
        //  捨札→山札の再構築で同じ札を引き直すことがあるので、残らない札は
        //  「引いたカード」として扱い、着地するまで手札に出さない。
        //  （retainsAtTurnEnd を持たない古いサーバーでは従来どおり）
        const crossesTurnBoundary = steps.some(step => step.event.kind === 'TurnStart')
        const oldHandIds = new Set(state.hand
          .filter(card => !crossesTurnBoundary || card.retainsAtTurnEnd !== false)
          .map(c => c.instanceId))
        const newlyDrawn = resp.state.hand.filter(c => !oldHandIds.has(c.instanceId))
        const preDrawIds = new Set(state.drawPile.map(c => c.instanceId))
        const postReshuffleDrawn = newlyDrawn.filter(c => !preDrawIds.has(c.instanceId))
        const preReshuffleDrawn = newlyDrawn.filter(c => preDrawIds.has(c.instanceId))
        // reshuffle が起きた = pre.discard が消えて draw に補充された
        const reshuffleHappened = hasShuffleTrigger
          &&
          state.discardPile.length > 0
          && resp.state.discardPile.length < state.discardPile.length
          && postReshuffleDrawn.length + resp.state.drawPile.length > 0

        const shouldStage = reshuffleHappened || (hasDrawTrigger && newlyDrawn.length >= 1)
        if (shouldStage) {
          // Why: HP overlay 等は events ループで累積済み。staging 用 setState は
          //  resp.state ベースなので、ここでクリアして二重適用を避ける。
          await finishCardMotion()
          setSlotAnim({})
          setDamageOverlay({})
          setBlockOverlay({})

          // 既存手札 (resp.state.hand から newlyDrawn を除外) = 残存 hand。
          // EndTurn では空、カードプレイ + draw 効果では残った手札。
          const newlyDrawnIds = new Set(newlyDrawn.map(c => c.instanceId))
          const survivingHand = resp.state.hand.filter(c => !newlyDrawnIds.has(c.instanceId))

          // Why: 「ドロー開始前の手札」を先に commit して、前ターンの手札が
          //  ドローと混ざって表示されるのを防ぐ (ユーザ要望: 手札全捨てが
          //  完了してからドロー開始)。EndTurn では survivingHand=[] になる。
          const reshuffleCards = reshuffleHappened
            ? [...resp.state.drawPile, ...postReshuffleDrawn]
            : null
          // Why: 画面の手札・山札・捨札は参加者 DTO（participants[].hand 等）から
          //  表示される。root だけ途中状態にすると最終手札が最初から見えてしまうため、
          //  ドローする参加者の資源も同じ途中状態にそろえる。
          const stagePiles = (piles: {
            hand: BattleCardInstanceDto[]
            drawPile: BattleCardInstanceDto[]
            discardPile?: BattleCardInstanceDto[]
          }): BattleStateDto => {
            const discardPile = piles.discardPile ?? resp.state.discardPile
            return {
              ...resp.state,
              hand: piles.hand,
              drawPile: piles.drawPile,
              discardPile,
              participants: resp.state.participants?.map(participant =>
                participant.actorInstanceId === drawActorId
                  ? { ...participant, hand: piles.hand, drawPile: piles.drawPile, discardPile }
                  : participant),
            }
          }
          setState(stagePiles({
            hand: survivingHand,
            drawPile: state.drawPile,
            discardPile: reshuffleCards ?? resp.state.discardPile,
          }))
          await sleep(80)

          // Why: ドロー演出は parallel に進む (前 sprite が着地する前に次 sprite が
          //  発射される)。1 枚ごとの flight 時間 (DRAW_FLIGHT_MS) は据え置きで、
          //  trigger 間隔 (DRAW_PARALLEL_STAGGER_MS) を短くすることで合計時間を圧縮。
          //  各 commit は flight 完了タイミング (= trigger 時刻 + FLIGHT) で setTimeout
          //  経由に確定。Promise.all で全コミット完了を待つ。
          const DRAW_PARALLEL_STAGGER_MS = 90
          const FLIGHT = DRAW_FLIGHT_MS

          const runStaggered = (
            cards: BattleCardInstanceDto[],
            handAfters: BattleCardInstanceDto[][],
            drawPileAfters: BattleCardInstanceDto[][],
            discardPileAfter: BattleCardInstanceDto[] | null,
          ) => {
            if (cards.length === 0) return Promise.resolve()
            return new Promise<void>((resolveAll) => {
              let remaining = cards.length
              cards.forEach((card, i) => {
                window.setTimeout(() => {
                  triggerDrawAnim(card, handAfters[i].length - 1, handAfters[i].length)
                  window.setTimeout(() => {
                    setState(stagePiles({
                      hand: handAfters[i],
                      drawPile: drawPileAfters[i],
                      ...(discardPileAfter !== null ? { discardPile: discardPileAfter } : {}),
                    }))
                    remaining -= 1
                    if (remaining === 0) resolveAll()
                  }, FLIGHT)
                }, i * DRAW_PARALLEL_STAGGER_MS)
              })
            })
          }

          if (reshuffleHappened && reshuffleCards) {
            // Phase 1: pre-reshuffle 分を parallel stagger で flight + commit。
            //  discardPile は reshuffleCards (リシャッフル対象一式) 固定。
            const phase1HandAfters = preReshuffleDrawn.map((_, i) =>
              [...survivingHand, ...preReshuffleDrawn.slice(0, i + 1)])
            const phase1DrawAfters = preReshuffleDrawn.map((_, i) =>
              state.drawPile.slice(i + 1))
            await runStaggered(preReshuffleDrawn, phase1HandAfters, phase1DrawAfters, reshuffleCards)

            // Phase 2: reshuffle 演出 (sprite が 1 枚ずつ弧で飛ぶ)
            await sleep(220)
            triggerReshuffleAnim(reshuffleCards)
            const reshuffleMs = reshuffleCards.length * 110 + 700
            await sleep(reshuffleMs)

            // Phase 3: post-reshuffle 分を parallel stagger で flight + commit。
            const phase3HandAfters = postReshuffleDrawn.map((_, i) =>
              [...survivingHand, ...preReshuffleDrawn, ...postReshuffleDrawn.slice(0, i + 1)])
            const phase3DrawAfters = postReshuffleDrawn.map((_, i) =>
              reshuffleCards.slice(0, reshuffleCards.length - (i + 1)))
            await runStaggered(postReshuffleDrawn, phase3HandAfters, phase3DrawAfters, null)
          } else {
            // reshuffle なし: parallel stagger で flight + commit。
            const handAfters = newlyDrawn.map((_, i) =>
              [...survivingHand, ...newlyDrawn.slice(0, i + 1)])
            const drawAfters = newlyDrawn.map((_, i) => state.drawPile.slice(i + 1))
            await runStaggered(newlyDrawn, handAfters, drawAfters, null)
          }
        }
      }

      // 全 event 処理完了 → 最終 state に確定 + overlay クリア
      await finishCardMotion()
      setSlotAnim({})
      setDamageOverlay({})
      setBlockOverlay({})
      const pendingContingency = presentationOnly
        ? null
        : findPendingContingencyCard(resp.state, cardCatalog)
      if (pendingContingency) {
        const pendingId = `contingency:${pendingContingency.card.instanceId}`
        const exhaustCenter = pileCenter(
          exhaustPileRef.current,
          { x: 1216, y: 558 },
        )
        setReenactCard(current => current?.id === pendingId
          ? { ...current, stage: 'holding' }
          : {
              id: pendingId,
              demo: {
                ...toHandCardDemo(
                  pendingContingency.card,
                  cardCatalog,
                  resp.state.energy,
                ),
                instanceId: pendingContingency.card.instanceId,
              },
              stage: 'holding',
              movement: pendingContingency.destination === 'exhaust'
                ? 'contingency-exhaust'
                : 'contingency-discard',
              fromX: exhaustCenter.x,
              fromY: exhaustCenter.y,
            })
      } else {
        setReenactCard(null)
      }
      if (isCancelled()) {
        setAnimating(false)
        return
      }
      if (!presentationOnly) setState(resp.state)

      // 決着したら、倒れた側（撃破の点滅・倒れる姿勢）を見せてから先へ進む（ユーザ要望: 約 1 秒）。
      //  敗北時に倒れる姿勢（down シート）を持つキャラは、倒れ切ってから少し止めるまで待つ。
      if (!presentationOnly
        && (resp.state.outcome === 'Victory' || resp.state.outcome === 'Defeat')) {
        const downDurations = resp.state.outcome === 'Defeat'
          ? resp.state.allies
              .filter(ally => ally.currentHp <= 0)
              .map(ally => actorAnimations(ally.instanceId)?.down)
              .filter((sheet): sheet is SpriteSheetDemo => !!sheet)
              .map(sheet => readableMotionDuration(sheet, 1) + DOWN_HOLD_MS)
          : []
        await waitForPresentation(Math.max(OUTCOME_HOLD_MS, ...downDurations))
      }
      // Victory のみ「勝利」banner を出す (ユーザ要望、Defeat は banner なし)。
      if (!presentationOnly && resp.state.outcome === 'Victory') {
        await showBanner('勝利')
      }
      setAnimating(false)

      if (!presentationOnly
        && (resp.state.outcome === 'Victory' || resp.state.outcome === 'Defeat')) {
        await handleFinalize()
      }
    },
    [
      accountId,
      cardCatalog,
      characterCatalog,
      clearCombatFeedback,
      duckBgm,
      effectiveMotionLevel,
      enemyCatalog,
      handleFinalize,
      playBattleCue,
      playComboTone,
      playLayeredSe,
      pushCombatFeedback,
      snapshot.run.characterId,
      showCharacterSkillBurst,
      state,
      showBanner,
      triggerDrawAnim,
      triggerReshuffleAnim,
      unitCatalog,
    ],
  )

  // Keep the replay effect stable while still calling the latest animation
  // pipeline. Live state updates must not restart the action currently shown.
  const playStepsRef = useRef(playSteps)
  useEffect(() => {
    playStepsRef.current = playSteps
  }, [playSteps])

  // 初期化: GET /battle → 既存ならそれを表示、なければ POST /start。
  useEffect(() => {
    let cancelled = false
    async function init() {
      try {
        const existing = await getBattle(accountId)
        if (cancelled) return
        if (existing) {
          setState(existing)
          if (existing.outcome === 'Victory' || existing.outcome === 'Defeat') {
            await handleFinalize()
          }
        } else {
          const resp = await startBattle(accountId)
          if (cancelled) return
          await playSteps(resp)
        }
      } catch (e) {
        if (!cancelled) setError((e as Error).message)
      }
    }
    void init()
    return () => {
      cancelled = true
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accountId])

  const replayParticipantId = replayView?.participant.participantId
  const replayActorId = replayView?.actor.instanceId
  const replayTurn = replayView?.turn
  const replayLatestSequence = replayView?.latestSequence ?? 0
  const replayFeedComplete = replayView?.feedState.toLowerCase() === 'complete'
  const replayCurrentEntry = replayView?.entries[replayView.position]
  const replayCurrentSequence = replayCurrentEntry?.sequence

  // Keep a LIVE/READY feed open as an append-only stream. The participant and
  // actor snapshots above it remain frozen, so new live battle DTOs never leak
  // into a replay already being watched.
  useEffect(() => {
    if (!replayParticipantId || !replayActorId || replayTurn === undefined || replayFeedComplete) return
    let cancelled = false
    const poll = async () => {
      try {
        const incoming = await getBattleReplay(accountId, {
          participantId: replayParticipantId,
          actorInstanceId: replayActorId,
          turn: replayTurn,
          afterSequence: replayLatestSequence,
        })
        if (cancelled) return
        setReplayView(previous => {
          if (!previous
            || previous.participant.participantId !== replayParticipantId
            || previous.turn !== replayTurn) {
            return previous
          }
          const bySequence = new Map(previous.entries.map(entry => [entry.sequence, entry]))
          incoming.entries.forEach(entry => bySequence.set(entry.sequence, entry))
          const mergedEntries = Array.from(bySequence.values())
            .sort((a, b) => a.sequence - b.sequence)
          const wasAtTail = previous.position >= previous.entries.length
          const previousFeedCanReopen = ['live', 'ready'].includes(
            previous.feedState.toLowerCase(),
          )
          const resumeAtAppendedTail = previous.followTail
            && !previous.playing
            && wasAtTail
            && mergedEntries.length > previous.position
            && previousFeedCanReopen
          return {
            ...previous,
            entries: mergedEntries,
            feedState: incoming.feedState,
            latestSequence: Math.max(previous.latestSequence, incoming.latestSequence),
            loading: false,
            playing: previous.playing || resumeAtAppendedTail,
          }
        })
      } catch {
        // A transient poll failure must not close the replay or disturb battle commands.
      }
    }
    const timer = window.setInterval(() => void poll(), 900)
    return () => {
      cancelled = true
      window.clearInterval(timer)
    }
  }, [
    accountId,
    replayActorId,
    replayFeedComplete,
    replayLatestSequence,
    replayParticipantId,
    replayTurn,
  ])

  // Replay the immutable decision journal through the same attack/hit pipeline
  // as a live command. The cursor advances only after the visual transition
  // from StateBefore to StateAfter has completed.
  useEffect(() => {
    if (!replayView?.playing || replayView.loading) return
    const entry = replayView.entries[replayView.position]
    if (!entry) {
      if (replayView.feedState.toLowerCase() !== 'live') {
        setReplayView(previous => previous
          ? { ...previous, playing: false, followTail: true }
          : null)
      }
      return
    }

    const token = ++replayAnimationTokenRef.current
    const isCancelled = () => replayAnimationTokenRef.current !== token
    const waitReplay = async (durationMs: number) => {
      await sleep(Math.round(durationMs / Math.max(0.25, replayView.speed)))
      return !isCancelled()
    }
    const before = replayPresentationAtEntryStart(
      replayView.turn,
      replayView.participant.participantId,
      replayView.actor.instanceId,
      replayView.battle,
      replayView.entries,
      replayView.position,
      replayView.participant,
      replayView.actor,
    ).state
    const after = replayPresentationAtPosition(
      replayView.turn,
      replayView.participant.participantId,
      replayView.actor.instanceId,
      replayView.battle,
      replayView.entries,
      replayView.position + 1,
      replayView.participant,
      replayView.actor,
    ).state
    const response = replayEntryToActionResponse(entry, before, after)

    const run = async () => {
      setReplayActionBaseline(before)
      setReplayActionInProgress(true)
      if (entry.card) {
        const replayDemo = toReplayCardDemo(entry.card, cardCatalog)
        setReplayActionCard({
          demo: replayDemo,
          stage: 'idle',
          ...measureReplayCardPath(replayDemo.instanceId),
        })
        if (!await waitReplay(16)) return
        setReplayActionCard(current => current ? { ...current, stage: 'centering' } : null)
        if (!await waitReplay(200)) return
        setReplayActionCard(current => current ? { ...current, stage: 'holding' } : null)
        playBattleCue('cardPlay', `replay:${entry.sequence}`)
        playComboTone(comboToneTier(entry.comboCountAfter) ?? 1)
      }

      await playStepsRef.current(response, {
        baselineState: before,
        presentationOnly: true,
        speed: replayView.speed,
        isCancelled,
      })
      if (isCancelled()) return

      // Keep a decision card readable briefly even when it only queued intent
      // and consequently produced no immediate battle effect events.
      if (entry.card && response.steps.length === 0 && !await waitReplay(160)) return
      if (entry.card) {
        setReplayActionCard(current => current ? { ...current, stage: 'leaving' } : null)
        if (!await waitReplay(280)) return
      }
      // Keep a readable beat between consecutive actions. The card motion
      // remains unchanged; this only separates one play from the next.
      if (!await waitReplay(220)) return
      if (isCancelled()) return

      setReplayActionCard(null)
      setReenactCard(null)
      setReplayActionBaseline(null)
      setReplayActionInProgress(false)
      setReplayView(previous => {
        if (!previous
          || previous.position !== replayView.position
          || previous.entries[previous.position]?.sequence !== entry.sequence) {
          return previous
        }
        return {
          ...previous,
          position: Math.min(previous.entries.length, previous.position + 1),
        }
      })
    }
    void run()

    return () => {
      if (replayAnimationTokenRef.current === token) {
        replayAnimationTokenRef.current += 1
      }
      setReplayActionCard(null)
      setReenactCard(null)
      setReplayActionBaseline(null)
      setReplayActionInProgress(false)
      setSlotAnim({})
      setDamageOverlay({})
      setBlockOverlay({})
      setAnimating(false)
    }
    // The current sequence is the stable identity for the action being played.
    // Depending on the whole append-only entries array would restart animation
    // whenever a later live-feed entry arrives during the current action.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    replayCurrentSequence,
    replayView?.loading,
    replayView?.playbackEpoch,
    replayView?.playing,
    replayView?.position,
    replayView?.speed,
  ])

  // 再生カードの移動経路を .app-stage ローカル座標 (1280×720) で求める。
  // 手札にあるカードはその位置から、無ければ手札中央付近から出し、捨札へ送る。
  function measureReplayCardPath(cardInstanceId: string | undefined) {
    const appRect = (document.querySelector('.app-stage') as HTMLElement | null)
      ?.getBoundingClientRect()
    const toLocal = (rect: DOMRect) => ({
      x: appRect ? (rect.left + rect.width / 2 - appRect.left) / (appRect.width / 1280) : rect.left + rect.width / 2,
      y: appRect ? (rect.top + rect.height / 2 - appRect.top) / (appRect.height / 720) : rect.top + rect.height / 2,
    })
    const handEl = cardInstanceId ? handCardRefs.current.get(cardInstanceId) : undefined
    const start = handEl ? toLocal(handEl.getBoundingClientRect()) : { x: 640, y: 620 }
    const discardEl = discardPileRef.current
    const dest = discardEl ? toLocal(discardEl.getBoundingClientRect()) : { x: 120, y: 640 }
    return { startX: start.x, startY: start.y, destX: dest.x, destY: dest.y }
  }

  function beginBattleCommand(): boolean {
    if (animating || busyRef.current) return false
    busyRef.current = true
    setBusy(true)
    setError(null)
    return true
  }

  function endBattleCommand() {
    busyRef.current = false
    setBusy(false)
  }

  async function withBusy<T>(fn: () => Promise<T>): Promise<T | null> {
    if (!beginBattleCommand()) return null
    try {
      return await fn()
    } catch (e) {
      setAnimating(false)
      setError((e as Error).message)
      return null
    } finally {
      endBattleCommand()
    }
  }

  function commandContext(
    kind: string,
    participant = state ? findPrimaryParticipant(state) : null,
  ): BattleCommandContext {
    return {
      commandId: createBattleCommandId(kind),
      ...(participant
        ? {
            participantId: participant.participantId,
            actorInstanceId: participant.actorInstanceId,
          }
        : {}),
      ...(state?.actionRevision !== undefined
        ? { expectedRevision: state.actionRevision }
        : {}),
    }
  }

  // Phase 10.5.M6.6: 行先パイルを判定する。
  //  formatter は exhaustSelf action / "exhaust" keyword を共に [K:exhaust]
  //  marker として description に出力するため、それを含むかで判定する。
  //  これで「使ったら喪失」系カードは playing-card overlay の Stage 3 で
  //  data-leaving-mode="exhaust" になり、その場回転縮小消滅アニメに切替わる。
  function destinationFor(card: BattleCardInstanceDto): 'discard' | 'exhaust' {
    return battleCardDestination(card, cardCatalog)
  }

  async function handlePlayCard(handIndex: number) {
    // Why: コスト不足 / 範囲外の hand クリックでサーバー 400 が頻発するのは UX
    // 上ノイズなので、Client 側で事前にプレイ可否を判定して握り潰す。Server は
    // engine ロジックで無効化されるが、わざわざ呼ばないのが望ましい。
    const card = state?.hand[handIndex]
    if (!card) return
    const commandParticipant = state ? findPrimaryParticipant(state) : null
    const playCommand = commandContext('play-card', commandParticipant)
    // Server/Coreが投影した現在支払コストを使用し、Clientではルールを再計算しない。
    const payCost = card.currentCost
    if (payCost === null) return
    if (state && payCost > state.energy) return

    // Lock before the first animation frame. React state is asynchronous, so a
    // ref guard is needed to stop another command during the centering animation.
    if (!beginBattleCommand()) return
    try {

    // 多段階 play アニメ用に DOM 位置を計測する。
    // demo / fan は現フレームで生成された値を ref から拾う (render との 1:1)。
    // Use the currently rendered presentation. The initial hand can mount
    // before the async catalog arrives; a stale pre-catalog snapshot has no
    // description and caused the enlarged card face to appear blank.
    const demo = handDemos[handIndex]
    const fanEntry = fan[handIndex]
    const cardEl = handCardRefs.current.get(card.instanceId)
    if (!cardEl || !demo || !fanEntry) {
      // Fallback: 計測不能 → アニメ無しで従来動作。
      const resp = await playCard(accountId, {
        handIndex,
        cardInstanceId: card.instanceId,
        ...playCommand,
      })
      await playSteps(resp)
      return
    }
    // Why: .app-stage が transform: scale() を持つため position: fixed の
    //  containing block は viewport ではなく .app-stage (1280×720 unscaled
    //  ローカル空間)。getBoundingClientRect は screen 座標を返すので、
    //  そのまま fixed 要素の top/left に当てると座標系が合わずズレる
    //  (ユーザ報告の「右下に拡大表示」「リフレッシュ非表示」の根本原因)。
    //  screen → .app-stage local の変換を介して 1280×720 空間で扱う。
    const appStageEl = document.querySelector('.app-stage') as HTMLElement | null
    const appRect = appStageEl?.getBoundingClientRect()
    const appScaleX = appRect ? appRect.width / 1280 : 1
    const appScaleY = appRect ? appRect.height / 720 : 1
    const appLeft = appRect ? appRect.left : 0
    const appTop = appRect ? appRect.top : 0
    const screenToLocal = (sx: number, sy: number) => ({
      x: (sx - appLeft) / appScaleX,
      y: (sy - appTop) / appScaleY,
    })

    const cardRect = cardEl.getBoundingClientRect()
    const startScreenX = cardRect.left + cardRect.width / 2
    const startScreenY = cardRect.top + cardRect.height / 2
    const startLocal = screenToLocal(startScreenX, startScreenY)
    const startX = startLocal.x
    const startY = startLocal.y

    // .app-stage 内中央 (1280/2, 720/2)
    const centerX = 640
    const centerY = 360

    const dest = destinationFor(card)
    const destEl = dest === 'exhaust' ? exhaustPileRef.current : discardPileRef.current
    let destX = centerX
    let destY = centerY
    if (destEl) {
      const r = destEl.getBoundingClientRect()
      const destLocal = screenToLocal(r.left + r.width / 2, r.top + r.height / 2)
      destX = destLocal.x
      destY = destLocal.y
    }

    // 既存 leavingHand 経路 (turn-end discard 等) との互換のため LeavingHandEntry
    // にも destDx/destDy (相対 delta) を保持する。playing-card 自身の
    // transform は startX/startY (絶対座標) を直接使う。
    const entry: LeavingHandEntry = {
      card,
      demo,
      fan: fanEntry,
      destDx: destX - startX,
      destDy: destY - startY,
      destination: dest,
    }

    // Stage 0: idle (hand card の visual center に絶対座標で配置 + fan-r 回転)。
    // Why: いきなり data-play-stage="centering" で mount すると初期スタイルが
    // centering のスタイルだけになって transition 発火せず瞬間移動。idle で
    // mount → 2 RAF 待って初期フレーム commit → centering に flip で
    // transform interpolation が確実に走る。
    setPlayingCard({
      entry,
      stage: 'idle',
      startX,
      startY,
      startRot: fanEntry.r,
      centerX,
      centerY,
      destX,
      destY,
    })
    await new Promise<void>((resolve) => {
      requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
    })

    const cardMotionScale = effectiveMotionLevel === 'off'
      ? 0
      : effectiveMotionLevel === 'reduced' ? 0.48 : 1

    // Stage 1: hand -> centre. The compositor-only transform is intentionally
    // long enough to follow with the eye; reduced/off settings shorten it.
    setPlayingCard(p => (p ? { ...p, stage: 'centering' } : null))
    await sleep(Math.round(CARD_PLAY_CENTER_MS * cardMotionScale))
    // Stage 2: holding (server 呼び出し + playSteps の演出をホールド)
    setPlayingCard(p => (p ? { ...p, stage: 'holding' } : null))
    const resp = await playCard(accountId, {
      handIndex,
      cardInstanceId: card.instanceId,
      ...playCommand,
    })
    await playSteps(resp)
    // Stage 3: leaving (パイル中心 / 喪失なら回転縮小消滅)
    //   discard/exhaust: centre -> destination pile (CSS と一致)。
    playBattleCue(dest === 'exhaust' ? 'cardExhaust' : 'cardDiscard', card.instanceId)
    setPlayingCard(p => (p ? { ...p, stage: 'leaving' } : null))
    await sleep(Math.round((
      dest === 'exhaust' ? CARD_PLAY_EXHAUST_MS : CARD_PLAY_DISCARD_MS
    ) * cardMotionScale))
    } catch (e) {
      setAnimating(false)
      setError((e as Error).message)
    } finally {
      // Never leave the moving-card overlay or command lock behind on failure.
      setPlayingCard(null)
      endBattleCommand()
    }
  }

  async function handleOpenReplay(
    participant: BattleParticipantDto,
    actor: CombatActorDto,
    subjectName: string,
  ) {
    if (!state) return
    const replayTurn = state.turn
    const participantSnapshot = snapshotParticipant(participant)
    const actorSnapshot = snapshotActor(actor)
    const battleSnapshot = snapshotBattleState(state)
    setReplayView({
      battle: battleSnapshot,
      participant: participantSnapshot,
      actor: actorSnapshot,
      subjectName,
      turn: replayTurn,
      entries: [],
      feedState: participant.isReady ? 'ready' : 'live',
      latestSequence: 0,
      position: 0,
      // Show the participant's initial hand before the first card begins.
      playing: false,
      speed: 1,
      loading: true,
      playbackEpoch: 0,
      followTail: true,
    })
    try {
      const replay = await getBattleReplay(accountId, {
        participantId: participant.participantId,
        actorInstanceId: actor.instanceId,
        turn: replayTurn,
      })
      setReplayView(previous => {
        if (!previous
          || previous.participant.participantId !== participant.participantId
          || previous.turn !== replayTurn) {
          return previous
        }
        return {
          ...previous,
          entries: replay.entries.slice().sort((a, b) => a.sequence - b.sequence),
          feedState: replay.feedState,
          latestSequence: replay.latestSequence,
          loading: false,
        }
      })
      if (replayStartTimerRef.current !== null) {
        window.clearTimeout(replayStartTimerRef.current)
      }
      replayStartTimerRef.current = window.setTimeout(() => {
        replayStartTimerRef.current = null
        setReplayView(previous => previous
          ? { ...previous, playing: true, followTail: true }
          : null)
      }, 1000)
    } catch (e) {
      setReplayView(previous => previous
        ? { ...previous, loading: false, playing: false, followTail: false }
        : null)
      setError((e as Error).message)
    }
  }

  /** 行動再生を閉じて戦闘操作へ戻る（「戻る」ボタンと右上の × で共通）。 */
  function closeReplay() {
    if (replayStartTimerRef.current !== null) {
      window.clearTimeout(replayStartTimerRef.current)
      replayStartTimerRef.current = null
    }
    setReplayView(null)
  }

  async function handleEndTurn() {
    const participant = state ? findPrimaryParticipant(state) : null
    const requestingReady = !!participant && !participant.isReady
    if (participant?.isReady) {
      manualUnreadyRef.current = true
      readyIntentRef.current = null
    } else if (participant && state) {
      readyIntentRef.current = {
        turn: state.turn,
        participantId: participant.participantId,
      }
    }
    const completed = await withBusy(async () => {
      const resp = participant
        ? await setBattleReady(accountId, {
            participantId: participant.participantId,
            isReady: !participant.isReady,
            actorInstanceId: participant.actorInstanceId,
            commandId: createBattleCommandId('ready'),
            ...(state?.actionRevision !== undefined
              ? { expectedRevision: state.actionRevision }
              : {}),
          })
        : await endTurn(accountId, {
            commandId: createBattleCommandId('end-turn'),
            expectedRevision: state?.actionRevision ?? 0,
          })
      await playSteps(resp)
      return resp
    })
    if (!completed) {
      manualUnreadyRef.current = false
      if (requestingReady) readyIntentRef.current = null
    }
  }

  async function handleUsePotion(potionIndex: number) {
    await withBusy(async () => {
      const commandParticipant = state ? findPrimaryParticipant(state) : null
      const resp = await consumeBattlePotion(accountId, {
        potionIndex,
        potionId: state?.potions[potionIndex],
        ...commandContext('use-potion', commandParticipant),
      })
      await playSteps(resp)
      return resp
    })
  }

  async function handleUseTacticalSkill() {
    await withBusy(async () => {
      const resp = await activateTacticalSkill(
        accountId,
        commandContext('use-tactical-skill'),
      )
      await playSteps(resp)
      return resp
    })
  }

  async function handleUseUltimateSkill() {
    await withBusy(async () => {
      const resp = await activateUltimateSkill(
        accountId,
        commandContext('use-ultimate-skill'),
      )
      await playSteps(resp)
      return resp
    })
  }

  /**
   * 側（味方・敵）の枠のクリック。押した位置にいるキャラを対象にする。
   * キャラの領域は隣と重なってよく、重なった所は今の対象でない方を選ぶ。
   * 位置で決まらない時（テスト環境など）は、押した要素が属するキャラ。
   */
  function handleSideTargetClick(side: ActorSide, event: React.MouseEvent<HTMLDivElement>) {
    if (interactionsDisabled) return
    const clicked = event.target as HTMLElement
    // スロット内のボタン（行動の再生など）は、それぞれの操作に任せる。
    if (clicked.closest('button, a, [data-no-target]')) return
    const actors = side === 'Enemy' ? displayedState.enemies : displayedState.allies
    const currentIndex = side === 'Enemy' ? displayedState.targetEnemyIndex : displayedState.targetAllyIndex
    const currentId = currentIndex !== null && currentIndex !== undefined
      ? actors[currentIndex]?.instanceId ?? null
      : null
    const picked = pickTargetAtPoint(
      readTargetRegions(event.currentTarget), event.clientX, event.clientY, currentId)
      ?? clicked.closest<HTMLElement>('.battle__slot[data-targetable="1"]')?.dataset.actorId
      ?? null
    if (!picked) return
    const index = actors.findIndex(actor => actor.instanceId === picked)
    if (index < 0) return
    void handleSetTarget(side, index)
  }

  async function handleSetTarget(side: ActorSide, slotIndex: number) {
    const commandParticipant = state ? findPrimaryParticipant(state) : null
    const newState = await withBusy(() =>
      setBattleTarget(accountId, {
        side,
        slotIndex,
        ...commandContext('set-target', commandParticipant),
      }),
    )
    if (newState) setState(newState)
  }

  // 撃破の点滅: 表示上の HP が 0 になったキャラを、消える前に DEATH_BLINK_MS だけ点滅させる。
  //  （倒れる姿勢を持つキャラは点滅させず、HP 0 のまま倒れた姿で残る。描画側で判定する。）
  const [dyingIds, setDyingIds] = useState<ReadonlySet<string>>(() => new Set())
  const aliveIdsRef = useRef<Set<string> | null>(null)
  const dyingIdsRef = useRef(new Set<string>())
  useEffect(() => {
    if (!state) return
    const alive = new Set<string>()
    const newlyDead: string[] = []
    for (const actor of [...state.allies, ...state.enemies]) {
      const hp = actor.currentHp - (damageOverlay[actor.instanceId] ?? 0)
      if (hp > 0) alive.add(actor.instanceId)
      else if (aliveIdsRef.current?.has(actor.instanceId) && !dyingIdsRef.current.has(actor.instanceId)) {
        newlyDead.push(actor.instanceId)
      }
    }
    aliveIdsRef.current = alive
    if (newlyDead.length === 0) return
    newlyDead.forEach(id => dyingIdsRef.current.add(id))
    setDyingIds(new Set(dyingIdsRef.current))
    window.setTimeout(() => {
      newlyDead.forEach(id => dyingIdsRef.current.delete(id))
      setDyingIds(new Set(dyingIdsRef.current))
    }, DEATH_BLINK_MS)
  }, [state, damageOverlay])

  // ------------- Render -------------

  if (state === null) {
    return (
      <div className="battle">
        <div className="battle__pattern" />
        <div className="battle__loading">戦闘準備中…</div>
        {error ? <div className="battle__error">{error}</div> : null}
      </div>
    )
  }

  const primaryParticipant = findPrimaryParticipant(state)
  const replayPresentation = replayView
    ? replayActionBaseline
      ? replayPresentationAtPosition(
          replayView.turn,
          replayView.participant.participantId,
          replayView.actor.instanceId,
          replayActionBaseline,
          [],
          0,
          replayView.participant,
          replayView.actor,
        )
      : replayPresentationAtPosition(
          replayView.turn,
          replayView.participant.participantId,
          replayView.actor.instanceId,
          replayView.battle,
          replayView.entries,
          replayView.position,
          replayView.participant,
          replayView.actor,
        )
    : null
  const displayedParticipant = replayPresentation?.participant ?? primaryParticipant
  const displayBaseState = replayPresentation?.state ?? state
  const projectedDisplayedState = projectParticipantForDisplay(displayBaseState, displayedParticipant)
  const replayPlayingInstanceId = replayActionCard?.demo.instanceId
  const displayedState = replayPlayingInstanceId
    ? {
        ...projectedDisplayedState,
        hand: projectedDisplayedState.hand.filter(card => card.instanceId !== replayPlayingInstanceId),
      }
    : projectedDisplayedState
  const primaryIsReady = primaryParticipant?.isReady ?? false
  const readyIntent = readyIntentRef.current
  const viewingReplay = replayView !== null
  const armedContingencyCount = displayedState.exhaustPile.filter(
    card => card.isContingencyArmed,
  ).length

  // データソース変換
  const heroActorRaw = displayedState.allies.find(
    (a) => a.definitionId === 'hero',
  ) as CombatActorDto | undefined
  // damageOverlay 反映: TopBar/HUD の HP 表示も progressive に減らす。
  const heroActor = heroActorRaw
    ? {
        ...heroActorRaw,
        currentHp: replayPresentation && !replayActionInProgress
          ? heroActorRaw.currentHp
          : Math.min(
              heroActorRaw.maxHp,
              Math.max(
                0,
              heroActorRaw.currentHp - (damageOverlay[heroActorRaw.instanceId] ?? 0),
              ),
            ),
      }
    : undefined
  const heroHp = heroActor
    ? {
        cur: heroActor.currentHp,
        max: heroActor.maxHp,
        lv: hpLevel(heroActor.currentHp, heroActor.maxHp),
      }
    : { cur: 0, max: 0, lv: '0' as const }
  // Why: HP / relics / potions は TopBar に集約済 (battle__hud 撤去済)。
  // 旧 demo 用の hpPct / relicDemos / potionDemos は不要になったため削除。

  // Why: BattleEngine は死亡した actor を state.Allies / Enemies から削除せず、
  // CurrentHp=0 のまま残す (.Where(a => a.IsAlive) で都度フィルタ)。Client 側でも
  // 表示前にフィルタして「死んだのに残り続ける」現象を回避する。
  // damageOverlay は playSteps 中の累積ダメージで、表示用 HP に減算する。
  // blockOverlay は同じく block 消費量で blockDisplay に減算する。
  // 死亡アニメーションは廃止 (ユーザ要望) のため HP=0 で即フィルタ → 自然消滅。
  const applyOverlay = (a: CombatActorDto): CombatActorDto => {
    const dmg = damageOverlay[a.instanceId] ?? 0
    const blk = blockOverlay[a.instanceId] ?? 0
    if (dmg === 0 && blk === 0) return a
    return {
      ...a,
      currentHp: Math.min(a.maxHp, Math.max(0, a.currentHp - dmg)),
      blockDisplay: Math.max(0, a.blockDisplay - blk),
    }
  }
  const applyCurrentOverlay = replayPresentation && !replayActionInProgress
    ? (actor: CombatActorDto) => actor
    : applyOverlay
  const allAllies = displayedState.allies.map(applyCurrentOverlay)
  const allEnemies = displayedState.enemies.map(applyCurrentOverlay)
  // down シートを持つキャラ（新規格のキャラ）は HP 0 でも残し、倒れた姿勢を見せる。
  const showsDownPose = (actor: CombatActorDto) => !!toCharacterDemo(
    actor,
    { enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog },
    accountId,
    findParticipantForActor(displayedState, actor.instanceId)?.characterId
      ?? snapshot.run.characterId,
  ).animations?.down
  const isDyingBlink = (actor: CombatActorDto) =>
    actor.currentHp <= 0 && dyingIds.has(actor.instanceId) && !showsDownPose(actor)
  const aliveAllies = allAllies.filter((a) => a.currentHp > 0 || showsDownPose(a) || isDyingBlink(a))
  const aliveEnemies = allEnemies.filter((e) => e.currentHp > 0 || showsDownPose(e) || isDyingBlink(e))

  // 4 スロット zero-pad で side ごとに描画。
  const playerActorsBySlot = placeActorsBySlot(aliveAllies, 4)
  const enemyActorsBySlot = placeActorsBySlot(aliveEnemies, 4)
  const playerSlots = padSlots(
    playerActorsBySlot
      .map(actor => actor
        ? toCharacterDemo(
            actor,
            { enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog },
            accountId,
            findParticipantForActor(displayedState, actor.instanceId)?.characterId
              ?? snapshot.run.characterId,
          )
        : null),
    4,
  )
  const enemySlots = padSlots(
    enemyActorsBySlot
      .map(actor => actor
        ? toCharacterDemo(
            actor,
            { enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog },
            accountId,
          )
        : null),
    4,
  )

  // Why: 片側に occupied slot が 1 つしか無い場合、選択肢が無いので
  // ターゲティング三角形 + sprite glow は描かない (ユーザ要望)。
  const playerOccupiedCount = playerSlots.filter(c => c.occupied).length
  const enemyOccupiedCount = enemySlots.filter(c => c.occupied).length
  const showTransientBattleAnimation = !viewingReplay || replayActionInProgress

  const handDemos = displayedState.hand.map((c) =>
    toHandCardDemo(
      c,
      cardCatalog,
      displayedState.energy,
    ),
  )
  const fan = fanLayout(handDemos.length)

  // Phase 10.5.M2-Choose T7: pendingCardPlay 中は modal 以外の全操作 (手札クリック /
  //  ターゲット切替 / end-turn / potion) を disable し、Server 側 RejectIfPending と
  //  二重防御する。
  const interactionsDisabled = animating
    || busy
    || state.pendingCardPlay !== null
    || primaryIsReady
    || viewingReplay
  const readyToggleDisabled = animating
    || busy
    || state.pendingCardPlay !== null
    || viewingReplay
    || state.phase !== 'PlayerInput'
  const displayedSkillOwnerId = displayedParticipant?.characterId
    ?? snapshot.run.characterId
  const characterSkills = characterCatalog?.[displayedSkillOwnerId]?.skills
    ?? unitCatalog?.[displayedSkillOwnerId]?.skills
    ?? []
  const tacticalSkill = characterSkills.find((skill) => skill.id === 'tactical')
  const ultimateSkill = characterSkills.find((skill) => skill.id === 'ultimate')
  const comboSkillAvailability = getComboSkillAvailability({
    comboGauge: displayedState.comboGauge,
    energy: displayedState.energy,
    phase: displayedState.phase,
    animating,
    busy,
    hasPendingChoice: displayedState.pendingCardPlay !== null,
    handCount: displayedState.hand.length,
    tacticalUsedThisTurn: displayedState.usedCharacterSkillIdsThisTurn.includes('tactical'),
    tacticalUsesThisTurn: displayedState.characterSkillUsesThisTurn?.tactical ??
      (displayedState.usedCharacterSkillIdsThisTurn.includes('tactical') ? 1 : 0),
    tacticalUsesPerTurn: tacticalSkill?.usesPerTurn ?? (tacticalSkill?.oncePerTurn ? 1 : 0),
    ultimateUsesThisTurn: displayedState.characterSkillUsesThisTurn?.ultimate ?? 0,
    ultimateUsesPerTurn: ultimateSkill?.usesPerTurn ?? (ultimateSkill?.oncePerTurn ? 1 : 0),
    tacticalGaugeCost: tacticalSkill?.comboGaugeCost,
    ultimateGaugeCost: ultimateSkill?.comboGaugeCost,
    skillsRestricted: displayedState.characterSkillsRestricted ?? false,
    tacticalEnergyCost: tacticalSkill?.energyCost,
    ultimateEnergyCost: ultimateSkill?.energyCost,
    ultimateRequiresHand: false,
  })
  const sharedSkillDisabledReason = viewingReplay
    ? 'リプレイ表示中'
    : busy || animating
      ? '効果処理中'
      : displayedState.pendingCardPlay !== null
        ? 'カード選択中'
        : displayedState.phase !== 'PlayerInput'
          ? '計画フェーズ外'
          : displayedState.characterSkillsRestricted
            ? '戦技・奥義が封印中'
            : undefined
  const skillDisabledReason = (
    kind: ComboSkillKind,
    disabled: boolean,
  ): string | undefined => {
    if (!disabled) return undefined
    if (sharedSkillDisabledReason) return sharedSkillDisabledReason
    const skill = kind === 'tactical' ? tacticalSkill : ultimateSkill
    const uses = kind === 'tactical'
      ? displayedState.characterSkillUsesThisTurn?.tactical ??
        (displayedState.usedCharacterSkillIdsThisTurn.includes('tactical') ? 1 : 0)
      : displayedState.characterSkillUsesThisTurn?.ultimate ?? 0
    const limit = skill?.usesPerTurn ?? (skill?.oncePerTurn ? 1 : 0)
    if (limit > 0 && uses >= limit) return 'このターンの使用回数に到達'
    const gaugeCost = skill?.comboGaugeCost
      ?? (kind === 'tactical' ? TACTICAL_SKILL_COST : ULTIMATE_SKILL_COST)
    if (displayedState.comboGauge < gaugeCost) {
      return `コンボゲージ不足 ${displayedState.comboGauge}/${gaugeCost}`
    }
    if (displayedState.energy < (skill?.energyCost ?? 0)) {
      return `エナジー不足 ${displayedState.energy}/${skill?.energyCost ?? 0}`
    }
    return '現在使用できません'
  }
  const tacticalDisabledReason = skillDisabledReason(
    'tactical',
    comboSkillAvailability.tacticalDisabled || viewingReplay,
  )
  const ultimateDisabledReason = skillDisabledReason(
    'ultimate',
    comboSkillAvailability.ultimateDisabled || viewingReplay,
  )
  const phaseLabel: Record<BattleStateDto['phase'], string> = {
    PlayerInput: 'PLANNING',
    PlayerAttacking: 'ALLY ACTION',
    EnemyAttacking: 'ENEMY ACTION',
    Resolved: 'RESULT',
  }
  const readyParticipants = displayedState.participants?.filter(participant =>
    participant.isReady
      || (readyIntent?.participantId === participant.participantId
        && readyIntent.turn === displayedState.turn)).length ?? 0
  const participantCount = displayedState.participants?.length ?? 1

  return (
    <div className="battle-screen">
      {/* Why: ユーザ要望「上のバーは map 画面のものをそのまま」。
          HP は live battle override (heroHp.cur)、potion 操作は battle 内で
          discard 不可なので onDiscardPotion は no-op。
          TopBar は .battle の外側に置くことで stage の intent chip 等と
          視覚的に重ならないようにする。 */}
      <TopBar
        currentHp={heroHp.cur}
        maxHp={heroHp.max}
        instantHp={replayActionInProgress}
        gold={snapshot.run.gold}
        potions={displayedState.potions}
        potionUsesThisTurn={displayedState.potionUsesThisTurn}
        deck={snapshot.run.deck}
        relics={displayedState.ownedRelicIds}
        onDiscardPotion={() => { /* battle 中は discard 不可 */ }}
        onUsePotion={
          interactionsDisabled
            ? () => { /* lockdown: pendingCardPlay / busy / animating 中は無視 */ }
            : (i) => void handleUsePotion(i)
        }
        onOpenMenu={onOpenMenu ?? (() => {})}
        menuActive={menuOpen ?? false}
        onTogglePeek={onTogglePeek}
        peekActive={false}
        peekDisabled={!onTogglePeek}
        playSeconds={snapshot.run.playSeconds}
      />
      <div
        className={`battle${((slotAnim.hitIds?.length ?? 0) + (slotAnim.blockedIds?.length ?? 0)) > 0 ? ' is-impacting' : ''}`}
        data-act={snapshot.run.currentAct}
        // 主人公ごとの戦闘操作UI（コンボ・戦技・奥義・READY）の画像。CSS が var(--hero-ui-*) で使う。
        style={heroUiStyle(snapshot.run.characterId)}
        // 敵の行動再生中はプレイヤーUIを敵用の装飾に切り替える。
        data-ui-owner={replayView?.actor.side === 'Enemy' ? 'enemy' : undefined}
      >
        <div className="battle__pattern" />
        <div className="battle__content" data-presentation="shake">
        {/* Stage */}
        <div className="battle__stage">
          <div className="battle-state-ribbon" role="status" aria-live="polite">
            <span className="battle-state-ribbon__turn">TURN {displayedState.turn}</span>
            <span className={`battle-state-ribbon__phase is-${displayedState.phase.toLowerCase()}`}>
              {phaseLabel[displayedState.phase] ?? displayedState.phase}
            </span>
            <span className="battle-state-ribbon__ready">
              READY {readyParticipants}/{participantCount}
            </span>
          </div>
          {replayView?.actor.side === 'Enemy' ? (
            <div className="battle-enemy-view-badge" role="status">
              敵の行動 ― {replayView.subjectName}
            </div>
          ) : null}
          <div className="battle__chars">
            <div
              className="battle__side battle__side--player"
              onClick={event => handleSideTargetClick('Ally', event)}
            >
              {playerSlots.map((c, i) => {
                // Visual placement uses stable SlotIndex; command targeting
                // below resolves the actor back to its DTO array index.
                const actor = playerActorsBySlot[i] ?? undefined
                const actorParticipant = actor
                  ? findParticipantForActor(displayedState, actor.instanceId)
                  : null
                const actorArrayIndex = actor
                  ? displayedState.allies.findIndex(candidate =>
                      candidate.instanceId === actor.instanceId)
                  : -1
                const isTargeted =
                  actorArrayIndex >= 0 &&
                  displayedState.targetAllyIndex === actorArrayIndex
                // Why: hero クリックは「targetAllyIndex を hero (slot 0) に
                // 戻す」操作として有効化する。召喚 ally を選択した後に hero へ
                // 戻せない不具合への対応。targetEnemyIndex も同時にクリアして
                // ターゲット状態をリセットする (現状 SetTarget は片方しか動かさ
                // ないのでそのまま敵スロット 0 に戻すか、味方 0 だけ戻すかは
                // Engine 仕様に従う。ここでは Ally,0 を呼ぶことで味方ターゲットを
                // hero に戻す挙動を提供)。
                const onClick =
                  c.occupied && actorArrayIndex >= 0 && !interactionsDisabled
                    ? () => void handleSetTarget('Ally', actorArrayIndex)
                    : undefined
                // Why: ally が攻撃側の時は敵側 (右) に slide。
                const attackingDir =
                  showTransientBattleAnimation && actor && slotAnim.attackerId === actor.instanceId
                    ? ('toward-enemy' as const)
                    : undefined
                const isHit = !!(showTransientBattleAnimation
                  && actor
                  && (slotAnim.hitIds ?? []).includes(actor.instanceId))
                const isBlocked = !!(showTransientBattleAnimation
                  && actor
                  && (slotAnim.blockedIds ?? []).includes(actor.instanceId))
                return (
                  <Slot
                    key={`p-${i}`}
                    char={c}
                    isTargeted={isTargeted}
                    showTargetVisual={playerOccupiedCount > 1}
                    attackingDir={attackingDir}
                    attackMotion={attackingDir ? slotAnim.attackMotion : undefined}
                    isSkill={!!(showTransientBattleAnimation && actor
                      && slotAnim.skillCasterId === actor.instanceId)}
                    cardMotion={showTransientBattleAnimation && actor
                      && slotAnim.cardMotion?.casterId === actor.instanceId
                      ? slotAnim.cardMotion.motion
                      : null}
                    isHit={isHit}
                    isBlocked={isBlocked}
                    isDying={!!actor && isDyingBlink(actor)}
                    motionPlaybackSpeed={slotAnim.playbackSpeed}
                    onClick={onClick}
                    onViewActions={
                      !viewingReplay && !animating && !busy && state.pendingCardPlay === null
                        && actor && actorParticipant
                        && actorParticipant.participantId !== primaryParticipant?.participantId
                        ? () => void handleOpenReplay(actorParticipant, actor, c.name)
                        : undefined
                    }
                    replayFeedState={actorParticipant?.isReady ? 'ready' : 'live'}
                    feedbackItems={actor
                      ? combatFeedback.filter(item => item.targetId === actor.instanceId)
                      : []}
                    actionLabel={actor ? actionLabels[actor.instanceId] ?? null : null}
                    aura={showTransientBattleAnimation && actor ? slotAuras[actor.instanceId] ?? null : null}
                    participantReady={actorParticipant
                      ? actorParticipant.isReady
                        || (readyIntent?.participantId === actorParticipant.participantId
                          && readyIntent.turn === displayedState.turn)
                        || displayedState.phase === 'PlayerAttacking'
                        || displayedState.phase === 'EnemyAttacking'
                      : undefined}
                  />
                )
              })}
            </div>
            <div
              className="battle__side battle__side--enemy"
              onClick={event => handleSideTargetClick('Enemy', event)}
            >
              {enemySlots.map((c, i) => {
                const actor = enemyActorsBySlot[i] ?? undefined
                const actorArrayIndex = actor
                  ? displayedState.enemies.findIndex(candidate =>
                      candidate.instanceId === actor.instanceId)
                  : -1
                const isTargeted =
                  actorArrayIndex >= 0 &&
                  displayedState.targetEnemyIndex === actorArrayIndex
                const onClick =
                  c.occupied && actorArrayIndex >= 0 && !interactionsDisabled
                    ? () => void handleSetTarget('Enemy', actorArrayIndex)
                    : undefined
                // Why: enemy が攻撃側の時は味方側 (左) に slide。
                const attackingDir =
                  showTransientBattleAnimation && actor && slotAnim.attackerId === actor.instanceId
                    ? ('toward-ally' as const)
                    : undefined
                const isHit = !!(showTransientBattleAnimation
                  && actor
                  && (slotAnim.hitIds ?? []).includes(actor.instanceId))
                const isBlocked = !!(showTransientBattleAnimation
                  && actor
                  && (slotAnim.blockedIds ?? []).includes(actor.instanceId))
                return (
                  <Slot
                    key={`e-${i}`}
                    char={c}
                    isTargeted={isTargeted}
                    showTargetVisual={enemyOccupiedCount > 1}
                    attackingDir={attackingDir}
                    attackMotion={attackingDir ? slotAnim.attackMotion : undefined}
                    isSkill={!!(showTransientBattleAnimation && actor
                      && slotAnim.skillCasterId === actor.instanceId)}
                    cardMotion={showTransientBattleAnimation && actor
                      && slotAnim.cardMotion?.casterId === actor.instanceId
                      ? slotAnim.cardMotion.motion
                      : null}
                    isHit={isHit}
                    isBlocked={isBlocked}
                    isDying={!!actor && isDyingBlink(actor)}
                    motionPlaybackSpeed={slotAnim.playbackSpeed}
                    onClick={onClick}
                    onViewActions={(() => {
                      // カード制御の敵も、このターンに計画したカード使用を再生できる。
                      const enemyParticipant = actor
                        ? state.enemyParticipants?.find(candidate =>
                            candidate.actorInstanceId === actor.instanceId)
                        : undefined
                      return !viewingReplay && !animating && !busy && state.pendingCardPlay === null
                        && actor && enemyParticipant
                        ? () => void handleOpenReplay(
                            enemyReplayParticipant(enemyParticipant), actor, c.name)
                        : undefined
                    })()}
                    replayFeedState="ready"
                    feedbackItems={actor
                      ? combatFeedback.filter(item => item.targetId === actor.instanceId)
                      : []}
                    actionLabel={actor ? actionLabels[actor.instanceId] ?? null : null}
                    aura={showTransientBattleAnimation && actor ? slotAuras[actor.instanceId] ?? null : null}
                  />
                )
              })}
            </div>
          </div>

          {/* Energy orb */}
          <EnergyOrb cur={displayedState.energy} recoveryPerTurn={displayedState.energyPerTurn} />

          {/* Piles (clickable to open modal) */}
          {replayView ? (
            // 行動再生を閉じる（「戻る」と同じ）。捨札・除外と同じ右端の列、画面右上に置く。
            <button
              type="button"
              className="battle-replay-close"
              onClick={closeReplay}
              aria-label="行動再生を閉じる"
              title="閉じる"
            >
              ×
            </button>
          ) : null}
          <button
            ref={drawPileRef}
            type="button"
            className="pile pile--draw"
            onClick={() => setPileOpen('draw')}
            aria-label={`山札 (${displayedState.drawPile.length}枚) を表示`}
          >
            <img className="pile__sym pile__sym--img" src={battleIcon('pile_draw')} alt="" draggable={false} />
            <span className="pile__lbl">山札</span>
            <span className="pile__num">{displayedState.drawPile.length}</span>
          </button>
          <button
            ref={exhaustPileRef}
            type="button"
            className={[
              'pile pile--exhaust',
              armedContingencyCount > 0 ? 'has-armed-contingency' : '',
            ].filter(Boolean).join(' ')}
            onClick={() => setPileOpen('exhaust')}
            aria-label={armedContingencyCount > 0
              ? `除外 (${displayedState.exhaustPile.length}枚、伏策待機中${armedContingencyCount}枚) を表示`
              : `除外 (${displayedState.exhaustPile.length}枚) を表示`}
          >
            <img className="pile__sym pile__sym--img" src={battleIcon('pile_exhaust')} alt="" draggable={false} />
            <span className="pile__lbl">除外</span>
            <span className="pile__num">{displayedState.exhaustPile.length}</span>
            {armedContingencyCount > 0 ? (
              <span className="pile__armed">伏策 {armedContingencyCount}</span>
            ) : null}
          </button>
          <button
            ref={discardPileRef}
            type="button"
            className="pile pile--discard"
            onClick={() => setPileOpen('discard')}
            aria-label={`捨札 (${displayedState.discardPile.length}枚) を表示`}
          >
            <img className="pile__sym pile__sym--img" src={battleIcon('pile_discard')} alt="" draggable={false} />
            <span className="pile__lbl">捨札</span>
            <span className="pile__num">{displayedState.discardPile.length}</span>
          </button>

          {readyNotice ? (
            <div className="battle-ready-notice" role="status" aria-live="polite">
              {readyNotice}
            </div>
          ) : null}

          {replayView ? (
            <BattleReplayControls
              subjectName={replayView.subjectName}
              feedState={replayView.feedState}
              entries={replayView.entries}
              position={replayView.position}
              playing={replayView.playing}
              loading={replayView.loading}
              speed={replayView.speed}
              onBack={closeReplay}
              onRestart={() => {
                if (replayStartTimerRef.current !== null) {
                  window.clearTimeout(replayStartTimerRef.current)
                }
                setReplayView(previous => previous
                  ? {
                      ...previous,
                      position: 0,
                      playing: false,
                      followTail: true,
                      playbackEpoch: previous.playbackEpoch + 1,
                    }
                  : null)
                replayStartTimerRef.current = window.setTimeout(() => {
                  replayStartTimerRef.current = null
                  setReplayView(previous => previous
                    ? { ...previous, playing: true, followTail: true }
                    : null)
                }, 1000)
              }}
              onTogglePlaying={() => setReplayView(previous => previous
                ? {
                    ...previous,
                    playing: !previous.playing,
                    followTail: previous.playing ? false : true,
                  }
                : null)}
              onSkip={() => setReplayView(previous => previous
                ? {
                    ...previous,
                    position: previous.entries.length,
                    playing: false,
                    followTail: false,
                  }
                  : null)}
              onStepBack={() => setReplayView(previous => previous
                ? {
                    ...previous,
                    position: Math.max(0, previous.position - 1),
                    playing: false,
                    followTail: false,
                  }
                : null)}
              onStepForward={() => setReplayView(previous => previous
                ? {
                    ...previous,
                    position: Math.min(previous.entries.length, previous.position + 1),
                    playing: false,
                    followTail: false,
                  }
                : null)}
              onSeek={position => setReplayView(previous => previous
                ? {
                    ...previous,
                    position: Math.max(0, Math.min(previous.entries.length, position)),
                    playing: false,
                    followTail: false,
                  }
                : null)}
              onSpeedChange={speed => setReplayView(previous => previous
                ? { ...previous, speed }
                : null)}
              renderCard={entry => entry.card
                ? <BattleCardVisual card={toReplayCardDemo(entry.card, cardCatalog)} detailsDisabled />
                : null}
            />
          ) : null}

          {/* Tactical / COMBO + gauge / Ultimate controls + Ready toggle */}
          <BattleComboControls
            count={displayedState.comboCount}
            maxComboThisTurn={displayedState.maxComboThisTurn}
            lastPlayedOrigCost={displayedState.lastPlayedOrigCost}
            nextCardComboFreePass={displayedState.nextCardComboFreePass}
            comboCarryoverPending={displayedState.comboCarryoverPending ?? false}
            comboGauge={displayedState.comboGauge}
            tacticalDisabled={comboSkillAvailability.tacticalDisabled || viewingReplay}
            ultimateDisabled={comboSkillAvailability.ultimateDisabled || viewingReplay}
            onUseTactical={() => void handleUseTacticalSkill()}
            onUseUltimate={() => void handleUseUltimateSkill()}
            tacticalSkill={tacticalSkill}
            ultimateSkill={ultimateSkill}
            tacticalUnconfigured={viewingReplay && !tacticalSkill}
            ultimateUnconfigured={viewingReplay && !ultimateSkill}
            tacticalHidden={viewingReplay && !tacticalSkill}
            ultimateHidden={viewingReplay && !ultimateSkill}
            tacticalDisabledReason={tacticalDisabledReason}
            ultimateDisabledReason={ultimateDisabledReason}
            tacticalUsesThisTurn={displayedState.characterSkillUsesThisTurn?.tactical ??
              (displayedState.usedCharacterSkillIdsThisTurn.includes('tactical') ? 1 : 0)}
            ultimateUsesThisTurn={displayedState.characterSkillUsesThisTurn?.ultimate ??
              (displayedState.usedCharacterSkillIdsThisTurn.includes('ultimate') ? 1 : 0)}
            finisherRecord={displayedState.finisherRecord}
            playHistoryCount={displayedState.playHistoryThisTurn?.length ?? 0}
            onOpenFinisherRecord={() => setPlayHistoryOpen(true)}
          />
          <button
            type="button"
            className={`end-turn${primaryIsReady ? ' is-ready' : ''}`}
            onClick={() => void handleEndTurn()}
            disabled={readyToggleDisabled}
            aria-pressed={primaryIsReady}
          >
            <span className="end-turn__label">
              {primaryIsReady ? 'READY 解除' : 'READY'}
            </span>
          </button>

          {/* Hand (fanned).
              Why: key を instanceId にすることで React が DOM を 1:1 で追跡し、
              新規カードのみ entry CSS animation が走り、再 mount 不要なカードは
              アニメ再生されない (急に再描画されない)。 */}
          <div className="hand-wrap" ref={handWrapRef}>
            <div className="hand">
              {handDemos.map((c, i) => {
                const inst = displayedState.hand[i]
                const id = inst?.instanceId ?? `idx-${i}`
                const isPlaying =
                  !viewingReplay &&
                  !!inst &&
                  !!playingCard &&
                  playingCard.entry.card.instanceId === inst.instanceId
                return (
                  <HandCard
                    key={id}
                    card={c}
                    augmenting={!!inst && augmentingCardIds.has(inst.instanceId)}
                    fan={fan[i]}
                    onClick={
                      interactionsDisabled || !c.playable || c.cardUsageLimitReached
                        ? undefined
                        : () => void handlePlayCard(i)
                    }
                    interactionLocked={interactionsDisabled}
                    onCardElement={(el) => {
                      if (!inst) return
                      if (el) {
                        handCardRefs.current.set(inst.instanceId, el)
                        const rect = el.getBoundingClientRect()
                        handCardVisuals.current.set(inst.instanceId, {
                          x: rect.left + rect.width / 2,
                          y: rect.top + rect.height / 2,
                          rot: fan[i]?.r ?? 0,
                        })
                      } else {
                        // Keep handCardVisuals: the removal diff runs after this
                        // DOM node has unmounted and still needs its last center.
                        handCardRefs.current.delete(inst.instanceId)
                      }
                    }}
                    hidden={isPlaying}
                  />
                )
              })}
              {/* leaving cards: 直前の hand には居たが今 hand にいないカード。
                  exit アニメ完了後に leavingHand から削除される。 */}
              {!viewingReplay && leavingHand.map((lh) => (
                <HandCard
                  key={`leaving-${lh.card.instanceId}`}
                  card={lh.demo}
                  fan={lh.fan}
                  leaving
                  leaveDx={lh.destDx}
                  leaveDy={lh.destDy}
                  leavingMode={lh.destination}
                />
              ))}
            </div>
          </div>
        </div>

        {error ? <div className="battle__error">{error}</div> : null}
        </div>
      </div>
      {pileOpen ? (
        <PileModal
          kind={pileOpen}
          cards={
            pileOpen === 'draw' ? displayedState.drawPile
              : pileOpen === 'discard' ? displayedState.discardPile
              : displayedState.exhaustPile
          }
          onClose={() => setPileOpen(null)}
        />
      ) : null}
      {playHistoryOpen ? (
        // 表示中の参加者（再生中なら再生対象）の現在のプレイ履歴を開く。
        <PlayHistoryModal
          history={displayedState.playHistoryThisTurn ?? []}
          record={displayedState.finisherRecord ?? null}
          onClose={() => setPlayHistoryOpen(false)}
          plannedIntent={displayedState.finisherRecord?.plannedAfterimageIntent ? (
            <div className="intents finisher-record__intents">
              <IntentChipRow
                intents={toIntentDemos(displayedState.finisherRecord.plannedAfterimageIntent)}
              />
            </div>
          ) : null}
        />
      ) : null}
      {/* Phase 10.5.M2-Choose T7: pendingCardPlay 中の choose modal。
          T7 では pile=hand のみ実装、draw / discard は T8 で List モード追加。 */}
      {state.pendingCardPlay && (
        <CardChoiceModal
          pending={state.pendingCardPlay}
          hand={state.hand}
          drawPile={state.drawPile}
          discardPile={state.discardPile}
          exhaustPile={state.exhaustPile}
          cardNames={cardNamesMemo}
          cardTypeOf={cardTypeOf}
          cardRarityOf={cardRarityOf}
          errorMessage={chooseError}
          onConfirm={async (selectedInstanceIds) => {
            // Why (I1): withBusy は throw を catch して null を返すが、
            //  念のため try/catch も入れて両ルートをカバー。失敗時は banner を出す。
            setChooseError(null)
            try {
              const completed = await withBusy(async () => {
                const commandParticipant = findPrimaryParticipant(state)
                const resp = await resolveCardChoice(accountId, {
                  selectedInstanceIds,
                  pendingCardInstanceId:
                    commandParticipant?.pendingCardPlay?.cardInstanceId,
                  ...commandContext('resolve-card-choice', commandParticipant),
                })
                await playSteps(resp)
                return true
              })
              if (!completed) {
                console.error('resolveCardChoice failed: withBusy returned null')
                setChooseError('カード選択の確定に失敗しました。再度お試しください。')
                return
              }
            } catch (err) {
              console.error('resolveCardChoice failed:', err)
              setChooseError(
                'カード選択の確定に失敗しました: ' +
                  String(err instanceof Error ? err.message : err),
              )
            }
          }}
        />
      )}
      {/* Why: 戦闘フェーズ banner。Map の ACT START と同じ 2 秒帯を画面中央に
          オーバーレイ。表示中は他の input/anim が await でブロックされている。
          phaseBanner の key に text を使うことで、連続表示時にアニメーションが
          再起動するようにする (同じ要素を残すと再アニメが走らない)。 */}
      {phaseBanner !== null && (
        <div
          key={phaseBanner}
          className="battle-screen__phase-banner"
          role="status"
          aria-live="polite"
        >
          <span className="battle-screen__phase-banner-text">{phaseBanner}</span>
        </div>
      )}
      {characterSkillBurst ? (
        <div
          key={characterSkillBurst.id}
          className={[
            'character-skill-burst',
            `character-skill-burst--${characterSkillBurst.kind}`,
            `character-skill-burst--${characterSkillBurst.characterClass.toLowerCase()}`,
          ].join(' ')}
          data-character-id={characterSkillBurst.characterId}
          data-skill-kind={characterSkillBurst.kind}
          style={{
            '--skill-burst-duration': `${characterSkillBurst.durationMs}ms`,
          } as CSSProperties}
          role="status"
          aria-live="polite"
        >
          <span className="character-skill-burst__portrait-wrap" aria-hidden="true">
            <img
              className="character-skill-burst__portrait"
              src={characterSkillBurst.portrait}
              alt=""
              draggable={false}
              onError={(event) => {
                if (!event.currentTarget.src.endsWith(DEFAULT_CHARACTER_PORTRAIT)) {
                  event.currentTarget.src = DEFAULT_CHARACTER_PORTRAIT
                }
              }}
            />
          </span>
          <span className="character-skill-burst__copy">
            <span className="character-skill-burst__kind">
              {characterSkillBurst.kind === 'ultimate' ? '奥義' : '戦技'}
            </span>
            <span className="character-skill-burst__name">{characterSkillBurst.name}</span>
          </span>
          <span className="character-skill-burst__sigil" aria-hidden="true">
            {characterSkillBurst.characterClass === 'Guardian' ? '◆'
              : characterSkillBurst.characterClass === 'Tactician' ? '⌖'
                : characterSkillBurst.characterClass === 'Enhancer' ? '♪' : '✦'}
          </span>
        </div>
      ) : null}
      {/* Cards moved to exhaust by random/all/choose effects share one fixed,
          viewport-centred animation. Their source position no longer depends
          on the selected hand slot or the hand container's transform. */}
      {exhaustingCards.map((entry) => (
        <div
          key={entry.id}
          className="effect-exhaust-card"
          data-animation-kind="effect-exhaust"
          style={{
            '--exhaust-start-x': `${entry.startX}px`,
            '--exhaust-start-y': `${entry.startY}px`,
            '--exhaust-start-rot': `${entry.startRot}deg`,
            '--exhaust-center-x': '640px',
            '--exhaust-center-y': '360px',
          } as CSSProperties}
        >
          <BattleCardVisual card={entry.demo} detailsDisabled ariaHidden />
        </div>
      ))}
      {/* Why: 多段階 play アニメ用の overlay。.battle-screen 直下に置くことで
          祖先要素の transform/will-change で containing block が作られて
          position: fixed が viewport 基準にならないリスクを排除する。
          state.hand から消えても LeavingHand とは別ルートで表示し続け、
          centering → holding (server + playSteps) → leaving の 3 段で
          top/left + transform を切り替えるアニメーション。 */}
      {!viewingReplay && playingCard && (
        <div
          className="playing-card"
          data-play-stage={playingCard.stage}
          data-leaving-mode={playingCard.entry.destination}
          style={{
            '--start-x': `${playingCard.startX}px`,
            '--start-y': `${playingCard.startY}px`,
            '--start-rot': `${playingCard.startRot}deg`,
            '--center-x': `${playingCard.centerX}px`,
            '--center-y': `${playingCard.centerY}px`,
            '--dest-x': `${playingCard.destX}px`,
            '--dest-y': `${playingCard.destY}px`,
          } as CSSProperties}
        >
          <BattleCardVisual card={playingCard.entry.demo} detailsDisabled ariaHidden />
        </div>
      )}
      {viewingReplay && replayActionCard && (
        <div
          className="playing-card replay-playing-card"
          data-replay-action="card"
          data-play-stage={replayActionCard.stage}
          data-leaving-mode="discard"
          style={{
            // Why: translate の % はカード自身の大きさ基準になり左上に出てしまうため px で指定する。
            '--start-x': `${replayActionCard.startX}px`,
            '--start-y': `${replayActionCard.startY}px`,
            '--start-rot': '0deg',
            '--center-x': '640px',
            '--center-y': '360px',
            '--dest-x': `${replayActionCard.destX}px`,
            '--dest-y': `${replayActionCard.destY}px`,
          } as CSSProperties}
        >
          <BattleCardVisual card={replayActionCard.demo} detailsDisabled ariaHidden />
        </div>
      )}
      {reenactCard && (
        <div
          key={reenactCard.id}
          className="reenact-card"
          data-reenact-card={reenactCard.demo.instanceId}
          data-reenact-stage={reenactCard.stage}
          data-reenact-movement={reenactCard.movement ?? 'popup'}
          style={reenactCard.fromX === undefined ? undefined : {
            '--reenact-from-x': `${reenactCard.fromX}px`,
            '--reenact-from-y': `${reenactCard.fromY}px`,
            '--reenact-to-x': `${reenactCard.toX}px`,
            '--reenact-to-y': `${reenactCard.toY}px`,
          } as CSSProperties}
          aria-hidden="true"
        >
          <BattleCardVisual card={reenactCard.demo} detailsDisabled ariaHidden />
          {reenactCard.contingencyTrigger !== undefined ? (
            <>
              <span className="reenact-card__burst" />
              <span className="reenact-card__stamp">
                <b>伏策発動</b>
                {reenactCard.contingencyTrigger ? <small>{reenactCard.contingencyTrigger}</small> : null}
                {reenactCard.contingencyEffect ? (
                  <span className="reenact-card__stamp-effect">
                    <CardDesc text={reenactCard.contingencyEffect} compress={false} />
                  </span>
                ) : null}
              </span>
            </>
          ) : null}
        </div>
      )}
      {/* Why: 山札補充 (discard → draw) の弧アーチ演出。fixed overlay で
          捨札中心 → 山札中心へ 110ms stagger で順次飛ばす。各 sprite は
          700ms で中間 +Y -80px の弧を描く。
          sprite は実際の <Card> (face-up) を表示し、hand と同じ視覚で
          「どのカードが reshuffle されたか」を見せる。 */}
      {reshuffleEntries.length > 0 && (
        <div className="reshuffle-overlay">
          {reshuffleEntries.map((e) => {
            const demo = toHandCardDemo(
              e.card,
              cardCatalog,
              state?.energy ?? 0,
            )
            return (
              <div
                key={e.id}
                className="reshuffle-sprite"
                style={{
                  '--rs-from-x': `${e.fromX}px`,
                  '--rs-from-y': `${e.fromY}px`,
                  '--rs-to-x': `${e.toX}px`,
                  '--rs-to-y': `${e.toY}px`,
                  '--rs-delay': `${e.delay}ms`,
                } as CSSProperties}
              >
                <BattleCardVisual card={demo} detailsDisabled ariaHidden />
              </div>
            )
          })}
        </div>
      )}
      {/* Why: 1 枚ドロー演出。山札アイコン中心 → 手札の予定 fan 位置へ
          DRAW_FLIGHT_MS で飛ぶ。reshuffle と同じ fixed overlay 構造。 */}
      {drawAnimEntries.length > 0 && (
        <div className="draw-overlay">
          {drawAnimEntries.map((e) => {
            const demo = toHandCardDemo(
              e.card,
              cardCatalog,
              state?.energy ?? 0,
            )
            return (
              <div
                key={e.id}
                className="draw-sprite"
                style={{
                  '--draw-from-x': `${e.fromX}px`,
                  '--draw-from-y': `${e.fromY}px`,
                  '--draw-to-x': `${e.toX}px`,
                  '--draw-to-y': `${e.toY}px`,
                  '--draw-rot': `${e.rot}deg`,
                } as CSSProperties}
              >
                <BattleCardVisual card={demo} detailsDisabled ariaHidden />
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

