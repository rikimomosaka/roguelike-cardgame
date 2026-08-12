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
import { rarityCodeFromName, rarityCodeFromNumber } from '../../components/rarity'
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
  const animations: CharacterDemo['animations'] = isHero
    ? {
        idle: {
          src: '/characters/player_idle.png?v=2',
          frameWidth: 128,
          frameHeight: 256,
          frameCount: 8,
          durationMs: 1200,
          loop: true,
        },
        attack: {
          src: '/characters/player_attack.png?v=1',
          frameWidth: 192,
          frameHeight: 256,
          frameCount: 4,
          durationMs: 240,
          loop: false,
        },
        hurt: {
          src: '/characters/player_hurt.png?v=1',
          frameWidth: 192,
          frameHeight: 256,
          frameCount: 4,
          durationMs: 300,
          loop: false,
        },
      }
    : undefined
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
    animations,
    heightTier,
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
      attackIcon = '/icons/ui/attack_all.png'
    } else if (typeof intent.attackSingle === 'number') {
      attackIcon = '/icons/ui/attack.png'
    } else if (typeof intent.attackRandom === 'number') {
      attackIcon = '/icons/ui/attack_random.png'
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
      icon: '/icons/ui/block.png',
      num: intent.block,
      name: '防御',
      // Why: 「ターン終了時に」だと現在ターンの攻撃を防げると誤認するため、
      // 「次のターンに」に変更 (ユーザ要望)。
      desc: `次のターンに${intent.block}ブロックを得る。`,
    })
  }
  if (intent.hasBuff) {
    list.push({ kind: 'buff', icon: '/icons/ui/intent_buff.png',
                name: '強化', desc: '次のターンに自身を強化する。' })
  }
  if (intent.hasDebuff) {
    list.push({ kind: 'buff', icon: '/icons/ui/intent_debuff.png',
                name: '弱体化', desc: '次のターンにこちらを弱体化する。' })
  }
  if (intent.hasHeal) {
    list.push({ kind: 'heal', icon: '/icons/ui/intent_heal.png',
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
  for (const [statusId, amount] of Object.entries(actor.statuses)) {
    if (amount === 0) continue
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
      // Phase 10.6.B follow-up: signed なので負値で「減少」表示に切替
      return {
        kind: amount < 0 ? 'debuff' : 'buff',
        icon: '/icons/ui/strength.png', name: '筋力',
        desc: amount < 0
          ? `与えるダメージが${-amount}減少する。`
          : `与えるダメージが${amount}増加する。`,
      }
    case 'dexterity':
      return {
        kind: amount < 0 ? 'debuff' : 'buff',
        icon: '/icons/ui/dexterity.png', name: '敏捷',
        desc: amount < 0
          ? `ブロック値が${-amount}減少する。`
          : `ブロック値が${amount}増加する。`,
      }
    case 'vulnerable':
      return { kind: 'debuff', icon: '/icons/ui/vulnerable.png', name: '脆弱',
               desc: `受けるダメージが 1.5 倍。${amount}ターン残存。` }
    case 'weak':
      return { kind: 'debuff', icon: '/icons/ui/weak.png', name: '脱力',
               desc: `与えるダメージが 0.75 倍。${amount}ターン残存。` }
    case 'poison':
      return { kind: 'debuff', icon: '/icons/ui/poison.png', name: '毒',
               desc: `ターン開始時に${amount}ダメージ。スタック数が 1 減る。` }
    case 'omnistrike':
      return { kind: 'buff', icon: '/icons/ui/attack_all.png', name: '拡散',
               desc: `全ての攻撃を全体攻撃に変更する。${amount}ターン残存。` }
    case 'barrier':
      return { kind: 'buff', icon: '/icons/ui/barrier.png', name: 'バリア',
               desc: `正の攻撃を${amount}ヒット無効化する。ブロックは消費しない。` }
    case 'lifesteal':
      return { kind: 'buff', icon: '/icons/ui/lifesteal.png', name: '吸血',
               desc: `攻撃で実際に減らしたHPの${amount}%を回復する。` }
    case 'reflect':
      return { kind: 'buff', icon: '/icons/ui/reflect.png', name: '反射',
               desc: `攻撃を受けるたび、攻撃者へ${amount}の固定ダメージを返す。` }
    case 'handCostReduction':
      return { kind: 'buff', icon: '/icons/ui/intent_buff.png', name: '手札軽減',
               desc: `手札にあるカードの現在コストを${amount}減らす。手札を離れると適用されない。` }
    case 'handCostIncrease':
      return { kind: 'debuff', icon: '/icons/ui/intent_debuff.png', name: '手札増加',
               desc: `手札にあるカードの現在コストを${amount}増やす。手札を離れると適用されない。` }
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
  // Core has already applied card modifiers, hand-wide statuses and combo
  // reduction. Never duplicate those game rules in the client.
  const currentCost = card.currentCost
  const originalCost = card.originalCost
  const playable = currentCost !== null && currentCost <= energy
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
    // 変更理由に関係なく上段=現在値、下段=コンボ参照用の元コスト。
    costOrig: currentCost !== originalCost ? originalCost : null,
    type: cardTypeFromString(def?.cardType),
    rarity: def ? cardRarityFromNumber(def.rarity) : 'c',
    playable,
    desc: card.isUpgraded
      ? upgradedDescription ?? description
      : description,
  }
}
