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
import type {
  BuffDemo,
  CharacterDemo,
  HandCardDemo,
  HpLv,
  IntentDemo,
  RelicDemo,
} from '../BattleScreen'

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
  switch (n) {
    case 0: return 'c'
    case 1: return 'r'
    case 2: return 'e'
    case 3: return 'l'
    default: return 'c'
  }
}

function relicRarityFromString(rarity: string | undefined): CardRarity {
  const r = (rarity ?? '').toLowerCase()
  if (r === 'rare' || r === 'r') return 'r'
  if (r === 'epic' || r === 'e') return 'e'
  if (r === 'legendary' || r === 'l') return 'l'
  return 'c'
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

// Why: BattleState.actor.definitionId for the hero is the sentinel 'hero',
// but the playable-character catalog is keyed by CharacterDefinition.Id
// (currently only 'default'). This hardcoded mapping unblocks heightTier
// data flow for the single-character era. TODO: thread RunSnapshotDto.run.
// characterId through when a second playable character lands.
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
): CharacterDemo {
  const isHero = actor.definitionId === HERO_DEFINITION_ID
  const enemyDef = !isHero && actor.side === 'Enemy'
    ? catalogs.enemies?.[actor.definitionId]
    : undefined
  const unitDef = !isHero && actor.side === 'Ally'
    ? catalogs.units?.[actor.definitionId]
    : undefined
  const characterDef = isHero
    ? catalogs.characters?.[HERO_CHARACTER_ID]
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

  // Why: 立ち絵 PNG は hero のみ既存配置 (player_stand.png)。enemy / summon は
  // 実アセットが配置されたら image を流し込むため、現状 undefined。
  // heightTier はカタログ (catalog 取得失敗時 5) から引く。
  const image = isHero ? '/characters/player_stand.png' : undefined
  const heightTier = isHero
    ? characterDef?.heightTier ?? 5
    : enemyDef?.heightTier ?? unitDef?.heightTier ?? 5

  return {
    occupied: true,
    name,
    desc,
    sprite,
    spriteKind,
    image,
    heightTier,
    hpCur: actor.currentHp,
    hpMax: actor.maxHp,
    hpLv: hpLevel(actor.currentHp, actor.maxHp),
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
    (intent.attackSingle ?? 0) > 0 ||
    (intent.attackRandom ?? 0) > 0 ||
    (intent.attackAll ?? 0) > 0
  // Why: tooltip は title (name) と body (desc) を別フィールドで描画する仕様。
  // body 側の文頭に行動名を含めると重複表示になるので、body は本文だけを書く。
  // 「通常攻撃」は「単体攻撃」と表記を変更 (ユーザ要望)。
  if (hasAttack) {
    const parts: string[] = []
    const damages: string[] = []
    if (intent.attackSingle && intent.attackSingle > 0) {
      parts.push('単体攻撃'); damages.push(String(intent.attackSingle))
    }
    if (intent.attackRandom && intent.attackRandom > 0) {
      parts.push('ランダム攻撃'); damages.push(String(intent.attackRandom))
    }
    if (intent.attackAll && intent.attackAll > 0) {
      parts.push('全体攻撃'); damages.push(String(intent.attackAll))
    }
    // Why: 攻撃 chip の icon は最も影響が大きい scope を選ぶ:
    // attackAll > attackSingle > attackRandom (ランダムは専用アイコン未配置のため
    // text fallback)。
    let attackIcon = '⚔'
    if (intent.attackAll && intent.attackAll > 0) {
      attackIcon = '/icons/ui/attack_all.png'
    } else if (intent.attackSingle && intent.attackSingle > 0) {
      attackIcon = '/icons/ui/attack.png'
    }
    list.push({
      kind: 'attack',
      icon: attackIcon,
      name: parts.join('/'),
      desc: `ターン終了時に${damages.join('/')}ダメージの攻撃。`,
      attack: {
        single: intent.attackSingle ?? undefined,
        random: intent.attackRandom ?? undefined,
        all: intent.attackAll ?? undefined,
        hits: intent.attackHits ?? undefined,
      },
    })
  }

  if (intent.block && intent.block > 0) {
    list.push({
      kind: 'defend',
      icon: '/icons/ui/block.png',
      num: intent.block,
      name: '防御',
      // Why: 「ターン終了時に」だと現在ターンの攻撃を防げると誤認するため、
      // 「次のターンに」に変更 (ユーザ要望)。
      desc: `次のターンに${intent.block}ブロックを得る。`,
    })
  }
  if (intent.hasBuff) {
    list.push({ kind: 'buff', icon: '✦', name: '強化', desc: '次のターンに自身を強化する。' })
  }
  if (intent.hasDebuff) {
    list.push({ kind: 'buff', icon: '☄', name: '弱体化', desc: '次のターンにこちらを弱体化する。' })
  }
  if (intent.hasHeal) {
    list.push({ kind: 'heal', icon: '✚', name: '回復', desc: '次のターンに自身を回復する。' })
  }

  return list
}

// ---------------- toBuffs ----------------

export function toBuffs(actor: CombatActorDto): BuffDemo[] {
  const buffs: BuffDemo[] = []
  if (actor.blockDisplay > 0) {
    buffs.push({
      kind: 'block',
      icon: '/icons/ui/block.png',
      num: actor.blockDisplay,
      name: 'ブロック',
      desc: `ダメージを${actor.blockDisplay}軽減する。`,
    })
  }
  for (const [statusId, amount] of Object.entries(actor.statuses)) {
    if (amount <= 0) continue
    const meta = statusMeta(statusId, amount)
    buffs.push({
      kind: meta.kind,
      icon: meta.icon,
      num: amount,
      name: meta.name,
      desc: meta.desc,
    })
  }
  return buffs
}

function statusMeta(id: string, amount: number): {
  kind: BuffDemo['kind']
  icon: string
  name: string
  desc: string
} {
  switch (id) {
    case 'strength':
      return { kind: 'buff', icon: '✦', name: '筋力',
               desc: `与えるダメージが${amount}増加する。` }
    case 'dexterity':
      return { kind: 'buff', icon: '◇', name: '敏捷',
               desc: `ブロック値が${amount}増加する。` }
    case 'vulnerable':
      return { kind: 'debuff', icon: '☠', name: '脆弱',
               desc: `受けるダメージが 1.5 倍。${amount}ターン残存。` }
    case 'weak':
      return { kind: 'debuff', icon: '☄', name: '脱力',
               desc: `与えるダメージが 0.75 倍。${amount}ターン残存。` }
    case 'poison':
      return { kind: 'debuff', icon: '/icons/ui/poison.png', name: '毒',
               desc: `ターン開始時に${amount}ダメージ。スタック数が 1 減る。` }
    case 'omnistrike':
      return { kind: 'buff', icon: '/icons/ui/attack_all.png', name: '全体攻撃',
               desc: `攻撃が全敵に当たる。${amount}ターン残存。` }
    default:
      return { kind: 'buff', icon: '?', name: id, desc: '' }
  }
}

// ---------------- toHandCardDemo ----------------

/**
 * card のオリジナルコスト (CostOverride 無視) を返す。
 * 強化済みなら upgradedCost を優先。combo 判定で参照される値。
 */
function origCostOf(
  card: BattleCardInstanceDto,
  catalog: CardCatalog | null,
): number | null {
  const def = catalog?.[card.cardDefinitionId]
  if (!def) return null
  if (card.isUpgraded && def.upgradedCost !== null && def.upgradedCost !== undefined) {
    return def.upgradedCost
  }
  return def.cost ?? null
}

export function toHandCardDemo(
  card: BattleCardInstanceDto,
  catalog: CardCatalog | null,
  energy: number,
  /** Server engine の state.lastPlayedOrigCost (combo 軽減判定用)。 */
  lastPlayedOrigCost: number | null = null,
): HandCardDemo {
  const def = catalog?.[card.cardDefinitionId]
  const orig = origCostOf(card, catalog)
  const baseCost = card.costOverride ?? orig
  // Why: BattleEngine.PlayCard と同じ combo 軽減ロジック (orig === lastPlayedOrigCost + 1)
  // を再現することで、見た目のコストを実際の支払いコストに合わせる。Server 側は
  // CostOverride とは独立に支払い時に -1 する (BattleEngine.PlayCard.cs:50-51 参照)。
  const willCombo =
    orig !== null && lastPlayedOrigCost !== null && orig === lastPlayedOrigCost + 1
  const reducedCost =
    baseCost !== null && baseCost !== undefined
      ? Math.max(0, baseCost - (willCombo ? 1 : 0))
      : null
  const playable =
    reducedCost !== null && reducedCost !== undefined && reducedCost <= energy
  return {
    name: def?.displayName ?? def?.name ?? card.cardDefinitionId,
    cost: reducedCost ?? 'X',
    // Why: 軽減発生時のみ表示用に元コストを露出。Card が "{orig}→{cost}" を描画する。
    costOrig: willCombo && baseCost !== null && baseCost !== undefined ? baseCost : null,
    type: cardTypeFromString(def?.cardType),
    rarity: def ? cardRarityFromNumber(def.rarity) : 'c',
    playable,
    desc: card.isUpgraded
      ? def?.upgradedDescription ?? def?.description ?? ''
      : def?.description ?? '',
  }
}
