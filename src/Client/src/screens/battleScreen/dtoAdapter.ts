// src/Client/src/screens/battleScreen/dtoAdapter.ts
//
// Phase 10.3-MVP Task 14:
// API DTO (BattleStateDto / CombatActorDto / BattleCardInstanceDto) と
// 各種カタログ (CardCatalog / RelicCatalog / EnemyCatalog / UnitCatalog) を、
// BattleScreen が JSX で扱う既存の中間型 (RelicDemo / CharacterDemo /
// HandCardDemo / BuffDemo) に変換するアダプタ群。
//
// 中間型の定義は BattleScreen.tsx から re-export している。

import type {
  BattleCardInstanceDto,
  CombatActorDto,
  IntentDto,
} from '../../api/types'
import type {
  CardCatalog,
  CharacterCatalog,
  EnemyCatalog,
  RelicCatalog,
  UnitCatalog,
} from '../../api/catalog'
import type { CardRarity, CardType } from '../../components/Card'
import { STATUS_DEFS } from '../../components/CardDesc'
import { rarityCodeFromName, rarityCodeFromNumber } from '../../components/rarity'
import type {
  BuffDemo,
  CharacterDemo,
  HandCardDemo,
  HpLv,
  IntentDemo,
  RelicDemo,
} from '../BattleScreen'
import { resolveActorVisual } from './actorVisualManifest'
import { intentIcon, statusIcon } from '../../presentation/assets'

// ---------------- HP level (sprite tier) ----------------

export function hpLevel(cur: number, max: number): HpLv {
  if (max <= 0) return '0'
  const pct = cur / max
  if (pct <= 0) return '0'
  if (pct < 0.34) return '1'
  if (pct < 0.67) return '2'
  return '3'
}

// ---------------- Rarity helpers ----------------

function cardRarityFromNumber(n: number): CardRarity {
  return rarityCodeFromNumber(n)
}

function relicRarityFromString(rarity: string | undefined): CardRarity {
  return rarityCodeFromName(rarity) ?? 'c'
}

function cardTypeFromString(s: string | undefined): CardType {
  const lower = (s ?? 'attack').toLowerCase()
  if (lower === 'attack' || lower === 'skill' || lower === 'power' ||
      lower === 'curse' || lower === 'status' || lower === 'unit') {
    return lower
  }
  return 'attack'
}

// ---------------- Sprite/icon fallbacks ----------------

const HERO_DEFINITION_ID = 'hero'

// BattleState.actor.definitionId for a playable character is still the
// sentinel 'hero'.  Its actual catalog identity therefore comes from the
// owning participant (and falls back to the legacy solo default).
const HERO_CHARACTER_ID = 'default'

const HERO_FALLBACK = {
  imageId: '☗',
  description: 'プレイヤー本体。HP 0 で敗北。',
}

// 既知 relic に対する簡易アイコン（フォント記号）。
// MVP 用フォールバック。Catalog DTO に icon 列がないため、
// id ベースで symbol を当てる。
const RELIC_ICON_FALLBACK: Record<string, string> = {
  burning_blood: '♆',
  iron_anchor: '⚓',
  twilight_seal: '❖',
  ancient_tower: '♜',
  void_eye: '✧',
  flame_crown: '♛',
  whetstone: '✦',
  bronze_charm: '✪',
}

function relicIcon(relicId: string): string {
  return RELIC_ICON_FALLBACK[relicId] ?? '◇'
}

// ---------------- toRelicDemo ----------------

export function toRelicDemo(relicId: string, catalog: RelicCatalog | null): RelicDemo {
  const def = catalog?.[relicId]
  return {
    icon: relicIcon(relicId),
    rarity: relicRarityFromString(def?.rarity),
    name: def?.name ?? relicId,
    desc: def?.description ?? '',
  }
}

// ---------------- toCharacterDemo ----------------

type CharacterCatalogs = {
  enemies: EnemyCatalog | null
  units: UnitCatalog | null
  characters: CharacterCatalog | null
}

export function toCharacterDemo(
  actor: CombatActorDto,
  catalogs: CharacterCatalogs,
  accountId: string,
  characterId?: string,
): CharacterDemo {
  const isHero = actor.definitionId === HERO_DEFINITION_ID
  const enemyDef = !isHero && actor.side === 'Enemy'
    ? catalogs.enemies?.[actor.definitionId]
    : undefined
  const unitDef = !isHero && actor.side === 'Ally'
    ? catalogs.units?.[actor.definitionId]
    : undefined
  const characterDef = isHero
    ? catalogs.characters?.[characterId ?? HERO_CHARACTER_ID]
      ?? catalogs.characters?.[HERO_CHARACTER_ID]
    : undefined

  const name = isHero
    ? accountId
    : enemyDef?.name ?? unitDef?.name ?? actor.definitionId
  const sprite = isHero
    ? HERO_FALLBACK.imageId
    : enemyDef?.imageId ?? unitDef?.imageId ?? '?'
  const desc = isHero ? HERO_FALLBACK.description : ''

  const spriteKind: CharacterDemo['spriteKind'] = isHero
    ? 'hero'
    : actor.side === 'Ally'
      ? 'ally'
      : 'enemy'

  // Why: 攻撃 (single/random/all) を 1 個の chip に統合、block / buff /
  // debuff / heal は別 chip として `&` 区切りで表示する仕様。
  // hero/enemy/summon すべて Server 計算済 IntentDto を使う統一経路。
  const intents = actor.intent ? toIntentDemos(actor.intent) : undefined

  const visual = resolveActorVisual({
    isHero,
    characterId,
    imageId: isHero ? undefined : sprite,
    side: actor.side,
  })
  const image = visual?.image
  const animations: CharacterDemo['animations'] = visual?.animations
  const heightTier = isHero
    ? characterDef?.heightTier ?? 5
    : enemyDef?.heightTier ?? unitDef?.heightTier ?? 5

  return {
    occupied: true,
    instanceId: actor.instanceId,
    name,
    desc,
    sprite,
    spriteKind,
    image,
    animations,
    heightTier,
    stage: visual?.stage,
    // 主人公は自分専用の戦闘UI（頭上の行動予告の下地など）を使う。
    uiHeroId: isHero ? characterId ?? HERO_CHARACTER_ID : undefined,
    traitName: characterDef?.traitName ?? undefined,
    traitDescription: characterDef?.traitEffects
      ?? characterDef?.traitDescription
      ?? undefined,
    hpCur: actor.currentHp,
    hpMax: actor.maxHp,
    hpLv: hpLevel(actor.currentHp, actor.maxHp),
    blockValue: actor.blockDisplay,
    intent: undefined,
    intents,
    buffs: toBuffs(actor),
  }
}

// ---------------- toIntentDemos ----------------

/**
 * IntentDto を chip リストに変換する。
 * - 攻撃 (single/random/all) はまとめて 1 個の chip (attack)、内訳は attack
 *   フィールドで保持して色分け表示。
 * - block/buff/debuff/heal はそれぞれ独立 chip として後続。
 */
export function toIntentDemos(intent: IntentDto): IntentDemo[] {
  const list: IntentDemo[] = []

  const hasAttack =
    (intent.attackHits ?? 0) > 0 ||
    typeof intent.attackSingle === 'number' ||
    typeof intent.attackRandom === 'number' ||
    typeof intent.attackAll === 'number'
  // Why: tooltip は title (name) と body (desc) を別フィールドで描画する仕様。
  // body 側の文頭に行動名を含めると重複表示になるので、body は本文だけを書く。
  // 「通常攻撃」は「単体攻撃」と表記を変更 (ユーザ要望)。
  if (hasAttack) {
    const parts: string[] = []
    const scopes: { label: string; amount: number; hits: number }[] = []
    if (typeof intent.attackSingle === 'number') {
      parts.push('単体攻撃')
      scopes.push({
        label: '単体攻撃',
        amount: intent.attackSingle,
        hits: intent.attackSingleHits ?? intent.attackHits ?? 1,
      })
    }
    if (typeof intent.attackRandom === 'number') {
      parts.push('ランダム攻撃')
      scopes.push({
        label: 'ランダム攻撃',
        amount: intent.attackRandom,
        hits: intent.attackRandomHits ?? intent.attackHits ?? 1,
      })
    }
    if (typeof intent.attackAll === 'number') {
      parts.push('全体攻撃')
      scopes.push({
        label: '全体攻撃',
        amount: intent.attackAll,
        hits: intent.attackAllHits ?? intent.attackHits ?? 1,
      })
    }
    // 攻撃 chip の icon は最も影響が大きい scope を優先:
    // attackAll > attackSingle > attackRandom
    let attackIcon = '⚔'
    if (typeof intent.attackAll === 'number') {
      attackIcon = intentIcon('attack_all')
    } else if (typeof intent.attackSingle === 'number') {
      attackIcon = intentIcon('attack')
    } else if (typeof intent.attackRandom === 'number') {
      attackIcon = intentIcon('attack_random')
    }
    const total = [intent.attackSingle, intent.attackRandom, intent.attackAll]
      .reduce<number>((sum, amount) => sum + (amount ?? 0), 0)
    const desc = `ターン終了時に${scopes
      .map(scope => `${scope.label}：${scope.hits} HIT、合計${scope.amount}ダメージ`)
      .join('／')}。`
    list.push({
      kind: 'attack',
      icon: attackIcon,
      name: parts.join('/'),
      desc,
      attack: {
        single: intent.attackSingle ?? undefined,
        random: intent.attackRandom ?? undefined,
        all: intent.attackAll ?? undefined,
        hits: intent.attackHits ?? undefined,
        singleHits: intent.attackSingleHits ?? undefined,
        randomHits: intent.attackRandomHits ?? undefined,
        allHits: intent.attackAllHits ?? undefined,
        total,
      },
    })
  }

  if (intent.block && intent.block > 0) {
    list.push({
      kind: 'defend',
      icon: intentIcon('block'),
      num: intent.block,
      name: '防御',
      // Why: 「ターン終了時に」だと現在ターンの攻撃を防げると誤認するため、
      // 「次のターンに」に変更 (ユーザ要望)。
      desc: `次のターンに${intent.block}ブロックを得る。`,
    })
  }
  if (intent.hasBuff) {
    list.push({ kind: 'buff', icon: intentIcon('buff'),
                name: '強化', desc: '次のターンに自身を強化する。' })
  }
  if (intent.hasDebuff) {
    list.push({ kind: 'buff', icon: intentIcon('debuff'),
                name: '弱体化', desc: '次のターンにこちらを弱体化する。' })
  }
  if (intent.hasHeal) {
    list.push({ kind: 'heal', icon: intentIcon('heal'),
                name: '回復', desc: '次のターンに自身を回復する。' })
  }

  return list
}

// ---------------- toBuffs ----------------

export function toBuffs(actor: CombatActorDto): BuffDemo[] {
  const buffs: BuffDemo[] = []
  // Why: ブロック値 (block) は HP バー右端に専用 UI で表示するため
  // (ユーザ要望)、buffs から分離。CharacterDemo.blockValue 経由で別出し。
  // Phase 10.6.B follow-up: strength / dexterity は signed なので負値も表示する。
  // weak / vulnerable / poison 等は engine 側で 0 以下なら remove されているため、
  // ここに到達した時点で amount > 0 (= 表示すべき)。
  const statusIds = [
    ...(actor.statusOrder ?? []),
    ...Object.keys(actor.statuses),
  ].filter((id, index, all) => all.indexOf(id) === index)
  for (const statusId of statusIds) {
    const amount = actor.statuses[statusId] ?? 0
    if (amount === 0) continue
    const meta = statusMeta(statusId, amount)
    buffs.push({
      kind: meta.kind,
      icon: meta.icon,
      num: amount,
      name: STATUS_DEFS[statusId]?.name ?? meta.name,
      desc: formatStatusDescription(statusId, amount, meta.desc),
    })
  }
  return buffs
}

function formatStatusDescription(id: string, amount: number, fallback: string): string {
  if (id === 'strength' && amount < 0)
    return `アタックで与えるダメージが[N:${-amount}]減少する。`
  if (id === 'dexterity' && amount < 0)
    return `得るブロックが[N:${-amount}]減少する。`
  const template = STATUS_DEFS[id]?.desc
  return template ? template.replaceAll('[N:X]', `[N:${Math.abs(amount)}]`) : fallback
}

function statusMeta(id: string, amount: number): {
  kind: BuffDemo['kind']
  icon: string
  name: string
  desc: string
} {
  switch (id) {
    case 'strength':
      // Phase 10.6.B follow-up: signed なので負値で「減少」表示に切替
      return {
        kind: amount < 0 ? 'debuff' : 'buff',
        icon: statusIcon('strength'), name: '筋力',
        desc: amount < 0
          ? `アタックで与えるダメージが${-amount}減少する。`
          : `アタックで与えるダメージが${amount}増加する。`,
      }
    case 'dexterity':
      return {
        kind: amount < 0 ? 'debuff' : 'buff',
        icon: statusIcon('dexterity'), name: '敏捷',
        desc: amount < 0
          ? `得るブロックが${-amount}減少する。`
          : `得るブロックが${amount}増加する。`,
      }
    case 'vulnerable':
      return { kind: 'debuff', icon: statusIcon('vulnerable'), name: '脆弱',
               desc: `アタックで受けるダメージが1.5倍になる。ターン終了時に${amount}が1減少する。` }
    case 'weak':
      return { kind: 'debuff', icon: statusIcon('weak'), name: '脱力',
               desc: `アタックで与えるダメージが0.75倍になる。ターン終了時に${amount}が1減少する。` }
    case 'poison':
      return { kind: 'debuff', icon: statusIcon('poison'), name: '毒',
               desc: `ターン開始時に${amount}ダメージを受け、${amount}が1減少する。` }
    case 'omnistrike':
      return { kind: 'buff', icon: statusIcon('omnistrike'), name: '拡散',
               desc: `全ての攻撃を全体攻撃に変更する。ターン終了時に${amount}が1減少する。` }
    case 'barrier':
      return { kind: 'buff', icon: statusIcon('barrier'), name: 'バリア',
               desc: `アタックによってHPが1以上減る場合、そのダメージを無効化し、${amount}が1減少する。` }
    case 'lifesteal':
      return { kind: 'buff', icon: statusIcon('lifesteal'), name: '吸血',
               desc: `アタックによって減らしたHPの${amount}%を回復する。` }
    case 'reflect':
      return { kind: 'buff', icon: statusIcon('reflect'), name: '反射',
               desc: `アタックを受けた時、攻撃元にアタックの回数分だけ${amount}ダメージを返す(この際ブロックを無視する)。` }
    case 'handCostReduction':
      return { kind: 'buff', icon: statusIcon('hand_cost_reduction'), name: '節制',
               desc: `手札のカードの使用コストを[N:${amount}]軽減する。カードの使用時に[S:handCostReduction]が1減少する。` }
    case 'handCostIncrease':
      return { kind: 'debuff', icon: statusIcon('hand_cost_increase'), name: '浪費',
               desc: `手札のカードの使用コストを[N:${amount}]増加する。カードの使用時に[S:handCostIncrease]が1減少する。` }
    case 'vigor':
      return { kind: 'buff', icon: statusIcon('vigor'), name: '元気',
               desc: `アタックで与えるダメージが1.5倍になる。ターン終了時に${amount}が1減少する。` }
    case 'pursuit':
      return { kind: 'buff', icon: statusIcon('pursuit'), name: '追撃',
               desc: `攻撃直前に、「敵全体に${amount}アタック。」を得て、${amount}が1減少する。` }
    case 'regeneration':
      return { kind: 'buff', icon: statusIcon('regeneration'), name: '再生',
               desc: `ターン開始時にHPを${amount}回復し、${amount}が1減少する。` }
    case 'fortify':
      return { kind: 'buff', icon: statusIcon('fortify'), name: '堅牢',
               desc: `ブロックを最大${amount}まで次のターンに持ち越す。` }
    case 'resistance':
      return { kind: 'buff', icon: statusIcon('resistance'), name: '耐性',
               desc: `デバフを無効化し、${amount}が1減少する。` }
    case 'revive':
      return { kind: 'buff', icon: statusIcon('revive'), name: '復活',
               desc: `死亡時に1度だけHPを${amount}回復し、${amount}が0になる。` }
    case 'growth':
      return { kind: 'buff', icon: statusIcon('growth'), name: '成長',
               desc: `ターン開始時に筋力${amount}を得る。` }
    case 'insight':
      return { kind: 'buff', icon: statusIcon('insight'), name: '洞察',
               desc: `ターン開始時に${amount}枚追加で引き、${amount}が0になる。` }
    case 'haste':
      return { kind: 'buff', icon: statusIcon('haste'), name: '加速',
               desc: 'ターン開始時のエナジー回復量を+[N:X]し、[S:haste]が消滅する。' }
    case 'continuity':
      return { kind: 'buff', icon: statusIcon('continuity'), name: '継続',
               desc: `コンボ条件を満たさないカードを使った時、コンボを継続し、${amount}が1減少する。` }
    case 'mastery':
      return { kind: 'buff', icon: statusIcon('mastery'), name: '熟練',
               desc: `ターン開始時にコンボゲージを${amount}得て、${amount}が1減少する。` }
    case 'virulentPoison':
      return { kind: 'debuff', icon: statusIcon('virulent_poison'), name: '猛毒',
               desc: `毒によって受けるダメージが${amount}倍になる。` }
    case 'stagnation':
      return { kind: 'debuff', icon: statusIcon('stagnation'), name: '停滞',
               desc: `コンボゲージが増えなくなる。ターン終了時に${amount}が1減少する。` }
    case 'burn':
      return { kind: 'debuff', icon: statusIcon('burn'), name: '火傷',
               desc: `ターン開始時に現在HPの${amount}%のダメージを受け、${amount}が0になる。` }
    case 'scorch':
      return { kind: 'debuff', icon: statusIcon('scorch'), name: '灼痕',
               desc: `火傷が消滅する時、代わりに灼痕が1減少する。` }
    case 'confusion':
      return { kind: 'debuff', icon: statusIcon('confusion'), name: '混乱',
               desc: `カードが手札へ入った時、その使用コストを0～3のランダムな数値に変更する。ターン終了時に${amount}が1減少する。` }
    case 'curse':
      return { kind: 'debuff', icon: statusIcon('curse'), name: '呪縛',
               desc: `カードを使用した時に${amount}が1減少する。${amount}が0になったターンはカードを使用できなくなる。` }
    case 'bind':
      return { kind: 'debuff', icon: statusIcon('bind'), name: '束縛',
               desc: `アタックカードを使用できない。ターン終了時に${amount}が1減少する。` }
    case 'silence':
      return { kind: 'debuff', icon: statusIcon('silence'), name: '沈黙',
               desc: `アタック以外のカードを使用できない。ターン終了時に${amount}が1減少する。` }
    case 'seal':
      return { kind: 'debuff', icon: statusIcon('seal'), name: '封印',
               desc: `戦技・奥義を使用できない。ターン終了時に${amount}が1減少する。` }
    case 'exhaustion':
      return { kind: 'debuff', icon: statusIcon('exhaustion'), name: '消耗',
               desc: `ターン開始時にエナジーを${amount}失い、${amount}が1減少する。` }
    case 'distress':
      return { kind: 'debuff', icon: statusIcon('distress'), name: '動揺',
               desc: `カードを使用することでコンボが途切れた時、エナジーとコンボゲージを0にする。ターン終了時に${amount}が1減少する。` }
    case 'afterimage':
      return { kind: 'buff', icon: statusIcon('afterimage'), name: '残光',
               desc: STATUS_DEFS.afterimage.desc }
    case 'steadfast':
      return { kind: 'buff', icon: statusIcon('steadfast'), name: '堅持',
               desc: STATUS_DEFS.steadfast.desc }
    case 'cover':
      return { kind: 'buff', icon: statusIcon('cover'), name: '護衛',
               desc: STATUS_DEFS.cover.desc }
    case 'defensiveStance':
      return { kind: 'buff', icon: statusIcon('defensive_stance'), name: '守勢',
               desc: STATUS_DEFS.defensiveStance.desc }
    case 'comboCarryover':
      return { kind: 'buff', icon: statusIcon('combo_carryover'), name: '連環',
               desc: STATUS_DEFS.comboCarryover.desc }
    case 'afterglow':
      return { kind: 'buff', icon: statusIcon('afterglow'), name: '余韻',
               desc: STATUS_DEFS.afterglow.desc }
    case 'resonance':
      return { kind: 'buff', icon: statusIcon('resonance'), name: '共鳴',
               desc: STATUS_DEFS.resonance.desc }
    case 'disruption':
      return { kind: 'debuff', icon: statusIcon('disruption'), name: '攪乱',
               desc: STATUS_DEFS.disruption.desc }
    default:
      return { kind: 'buff', icon: statusIcon('status_unknown'), name: id, desc: '' }
  }
}

// ---------------- toHandCardDemo ----------------

export function toHandCardDemo(
  card: BattleCardInstanceDto,
  catalog: CardCatalog | null,
  energy: number,
): HandCardDemo {
  const def = catalog?.[card.cardDefinitionId]
  // Core has already applied card modifiers, hand-wide statuses and combo
  // reduction. Never duplicate those game rules in the client.
  const currentCost = card.currentCost
  const originalCost = card.originalCost
  const cardUsageLimitReached = card.cardUsageLimitReached ?? false
  const playable = currentCost !== null
    && currentCost <= energy
    && !cardUsageLimitReached
    && !(card.cardPlayRestricted ?? false)
  const unavailableReason = playable
    ? undefined
    : card.cardPlayRestrictionReason
      ?? (cardUsageLimitReached
        ? 'このターンの使用回数に到達'
        : currentCost === null
          ? '現在使用できません'
          : currentCost > energy
            ? `エナジー不足 ${energy}/${currentCost}`
            : '現在使用できません')
  // Why: 10.5.C で Server が hero context を反映した adjustedDescription を populate
  //   するようになった。null (戦闘外 catalog 等) のときは catalog の static description
  //   にフォールバックする。adjustedDescription/adjustedUpgradedDescription は marker
  //   `[N:N|up]` `[N:N|down]` を含み得るので、CardDesc で赤/青に色分けされる。
  const description = card.adjustedDescription ?? def?.description ?? ''
  const upgradedDescription =
    card.adjustedUpgradedDescription ?? def?.upgradedDescription ?? null
  return {
    name: def?.displayName ?? def?.name ?? card.cardDefinitionId,
    // Why: PileModal と同じく Card 側に upgraded を渡して "+" を専用 span で
    //  描画する (CSS class card__plus による色分け維持)。ユーザ要望: 手札にも +。
    upgraded: card.isUpgraded,
    // Why: 右クリック長押しトグル用に通常 / 強化両方のテキストを保持。
    //  Card 側の useTooltipTarget が effectiveUpgraded に応じて切替える。
    description,
    upgradedDescription,
    cost: currentCost ?? 'X',
    costOriginal: originalCost,
    costChanges: card.costChanges ?? [],
    isContingencyArmed: card.isContingencyArmed ?? false,
    contingencyTriggerText: card.contingencyTriggerText ?? null,
    comboReductionEligible: card.comboReductionEligible ?? false,
    comboContinues: card.comboContinues ?? card.comboReductionEligible ?? false,
    comboRingHighlighted: card.comboRingHighlighted ?? card.comboContinues ?? false,
    wildDisabled: card.wildDisabled ?? false,
    cardUsageLimitReached,
    costUnavailable: cardUsageLimitReached || (card.cardPlayRestricted ?? false),
    // 変更理由に関係なく上段=現在値、下段=コンボ参照用の元コスト。
    costOrig: currentCost !== originalCost ? originalCost : null,
    type: cardTypeFromString(def?.cardType),
    rarity: def ? cardRarityFromNumber(def.rarity) : 'c',
    // Generated-card DTOs carry their source affiliation. Older snapshots or
    // unknown definitions still need the neutral frame instead of an implicit
    // type-coloured fallback.
    poolClass: card.poolClass ?? def?.poolClass ?? 'Universal',
    playable,
    unavailableReason,
    desc: card.isUpgraded
      ? upgradedDescription ?? description
      : description,
  }
}
