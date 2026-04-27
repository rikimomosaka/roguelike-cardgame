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
      lower === 'curse' || lower === 'status') {
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
    intent: undefined, // MVP: intent 表示は最小、Phase 10.4 で詳細化
    buffs: toBuffs(actor),
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

export function toHandCardDemo(
  card: BattleCardInstanceDto,
  catalog: CardCatalog | null,
  energy: number,
): HandCardDemo {
  const def = catalog?.[card.cardDefinitionId]
  const baseCost = def?.cost ?? null
  const cost = card.costOverride ?? baseCost
  const playable =
    cost !== null && cost !== undefined && cost <= energy
  return {
    name: def?.displayName ?? def?.name ?? card.cardDefinitionId,
    cost: cost ?? 'X',
    type: cardTypeFromString(def?.cardType),
    rarity: def ? cardRarityFromNumber(def.rarity) : 'c',
    playable,
    desc: card.isUpgraded
      ? def?.upgradedDescription ?? def?.description ?? ''
      : def?.description ?? '',
  }
}
