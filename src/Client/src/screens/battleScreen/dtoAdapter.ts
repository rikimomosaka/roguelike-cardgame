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

const HERO_FALLBACK = {
  name: '主人公',
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
}

export function toCharacterDemo(
  actor: CombatActorDto,
  catalogs: CharacterCatalogs,
): CharacterDemo {
  const isHero = actor.definitionId === HERO_DEFINITION_ID
  const enemyDef = !isHero && actor.side === 'Enemy'
    ? catalogs.enemies?.[actor.definitionId]
    : undefined
  const unitDef = !isHero && actor.side === 'Ally'
    ? catalogs.units?.[actor.definitionId]
    : undefined

  const name = isHero
    ? HERO_FALLBACK.name
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

  return {
    occupied: true,
    name,
    desc,
    sprite,
    spriteKind,
    hpCur: actor.currentHp,
    hpMax: actor.maxHp,
    hpLv: hpLevel(actor.currentHp, actor.maxHp),
    intent: actor.intent ? toIntentDemo(actor.intent) : undefined,
    buffs: toBuffs(actor),
  }
}

// ---------------- toIntentDemo ----------------

/**
 * Server 側の IntentDto (kind="attack"|"defend"|...) を IntentDemo に変換。
 * BattleScreen の頭上チップで「次の予定行動」を表示する用途。
 */
export function toIntentDemo(intent: IntentDto): IntentDemo {
  switch (intent.kind) {
    case 'attack': {
      const hits = intent.hits && intent.hits > 1 ? `×${intent.hits}` : ''
      return {
        kind: 'attack',
        icon: '⚔',
        num: intent.amount ?? undefined,
        name: '攻撃',
        desc: `次のターンに ${intent.amount ?? '?'} ダメージを与える${hits}。`,
      }
    }
    case 'defend':
      return {
        kind: 'defend',
        icon: '◆',
        num: intent.amount ?? undefined,
        name: '防御',
        desc: `次のターンに ${intent.amount ?? '?'} のブロックを得る。`,
      }
    case 'multi': {
      const hits = intent.hits && intent.hits > 1 ? `×${intent.hits}` : ''
      return {
        kind: 'attack',
        icon: '⚡',
        num: intent.amount ?? undefined,
        name: '攻撃 + 補助',
        desc: `次のターンに ${intent.amount ?? '?'} ダメージ${hits} と補助行動。`,
      }
    }
    case 'buff':
      return { kind: 'buff', icon: '✦', name: '強化', desc: '次のターンに自身を強化する。' }
    case 'debuff':
      return { kind: 'buff', icon: '☄', name: '弱体化', desc: '次のターンにこちらを弱体化する。' }
    case 'heal':
      return { kind: 'heal', icon: '✚', name: '回復', desc: '次のターンに自身を回復する。' }
    case 'unknown':
    default:
      return { kind: 'unknown', icon: '?', name: '不明', desc: '次の行動は不明。' }
  }
}

// ---------------- toBuffs ----------------

export function toBuffs(actor: CombatActorDto): BuffDemo[] {
  const buffs: BuffDemo[] = []
  if (actor.blockDisplay > 0) {
    buffs.push({
      kind: 'block',
      icon: '◆',
      num: actor.blockDisplay,
      name: 'ブロック',
      desc: 'このターン、ダメージを軽減する。',
    })
  }
  for (const [statusId, amount] of Object.entries(actor.statuses)) {
    if (amount <= 0) continue
    const meta = statusMeta(statusId)
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

function statusMeta(id: string): {
  kind: BuffDemo['kind']
  icon: string
  name: string
  desc: string
} {
  switch (id) {
    case 'strength':
      return { kind: 'buff', icon: '✦', name: '力', desc: '与えるダメージが +N 増加する。' }
    case 'dexterity':
      return { kind: 'buff', icon: '◇', name: '敏捷', desc: 'ブロック値が +N 増加する。' }
    case 'vulnerable':
      return { kind: 'debuff', icon: '☠', name: '脆弱', desc: '受けるダメージが 1.5 倍。N ターン残存。' }
    case 'weak':
      return { kind: 'debuff', icon: '☄', name: '脱力', desc: '与えるダメージが 0.75 倍。N ターン残存。' }
    case 'poison':
      return { kind: 'debuff', icon: '☠', name: '毒', desc: 'ターン開始時に N ダメージ。スタック数が 1 減る。' }
    case 'omnistrike':
      return { kind: 'buff', icon: '✷', name: '全体攻撃', desc: '攻撃が全敵に当たる。N ターン残存。' }
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
    type: cardTypeFromString(def?.cardType),
    rarity: def ? cardRarityFromNumber(def.rarity) : 'c',
    playable,
    desc: card.isUpgraded
      ? def?.upgradedDescription ?? def?.description ?? ''
      : def?.description ?? '',
  }
}
