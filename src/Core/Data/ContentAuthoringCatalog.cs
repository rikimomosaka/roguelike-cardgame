using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RoguelikeCardGame.Core.Data;

/// <summary>
/// Stable vocabulary exposed to browser authoring tools. Keeping these values in Core prevents
/// the legacy editors, the unified workbench, and executable rules from maintaining separate
/// lists. Adding a brand-new action still requires an engine implementation; this catalog only
/// describes values that the current engine understands.
/// </summary>
public static class ContentAuthoringCatalog
{
    /// <summary>Authoring UI display names keyed as "category:value".</summary>
    public static IReadOnlyDictionary<string, string> DisplayNames { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rarities:0"] = "基本", ["rarities:1"] = "コモン", ["rarities:2"] = "レア",
            ["rarities:3"] = "エピック", ["rarities:4"] = "レジェンダリー", ["rarities:5"] = "特殊",
            ["acts:1"] = "第1層", ["acts:2"] = "第2層", ["acts:3"] = "第3層",
            ["cardTypes:Attack"] = "攻撃", ["cardTypes:Skill"] = "スキル",
            ["cardTypes:Power"] = "パワー", ["cardTypes:Curse"] = "呪い",
            ["cardTypes:Status"] = "状態異常", ["cardTypes:Unit"] = "召喚",
            ["characterClasses:Striker"] = "ストライカー",
            ["characterClasses:Guardian"] = "ガーディアン",
            ["characterClasses:Tactician"] = "タクティシャン",
            ["characterClasses:Enhancer"] = "エンハンサー",
            ["cardPoolClasses:Universal"] = "ユニバーサル",
            ["cardPoolClasses:Striker"] = "ストライカー",
            ["cardPoolClasses:Guardian"] = "ガーディアン",
            ["cardPoolClasses:Tactician"] = "タクティシャン",
            ["cardPoolClasses:Enhancer"] = "エンハンサー",
            ["cardPoolKinds:Basic"] = "基本カード",
            ["cardPoolKinds:Reward"] = "報酬カード",
            ["scopes:self"] = "自身", ["scopes:single"] = "単体",
            ["scopes:random"] = "ランダム", ["scopes:all"] = "全体",
            ["sides:enemy"] = "敵", ["sides:ally"] = "味方",
            ["selects:random"] = "ランダム", ["selects:choose"] = "選択", ["selects:all"] = "全て",
            ["piles:hand"] = "手札", ["piles:draw"] = "山札", ["piles:discard"] = "捨札", ["piles:exhaust"] = "除外",
            ["tiers:Weak"] = "弱敵", ["tiers:Strong"] = "強敵", ["tiers:Elite"] = "中ボス", ["tiers:Boss"] = "ボス",
            ["moveKinds:Attack"] = "攻撃", ["moveKinds:Defend"] = "防御",
            ["moveKinds:Buff"] = "バフ", ["moveKinds:Debuff"] = "デバフ", ["moveKinds:Multi"] = "複合",
            ["tileKinds:Start"] = "開始地点", ["tileKinds:Enemy"] = "敵", ["tileKinds:Elite"] = "中ボス",
            ["tileKinds:Rest"] = "休息所", ["tileKinds:Merchant"] = "商人", ["tileKinds:Treasure"] = "宝箱",
            ["tileKinds:Unknown"] = "不明", ["tileKinds:Boss"] = "ボス", ["tileKinds:Event"] = "イベント",
            ["eventRarities:common"] = "一般", ["eventRarities:uncommon"] = "珍しい", ["eventRarities:rare"] = "稀",
            ["costDurations:untilPlayed"] = "使用するまで", ["costDurations:untilTurnEnd"] = "ターン終了まで",
            ["cardAbilities:finisher"] = "終撃", ["cardAbilities:reprise"] = "再演",
            ["cardAbilities:contingency"] = "伏策",
            ["contingencyTriggers:TurnStart"] = "ターン開始時",
            ["contingencyTriggers:BeforeEnemyAttack"] = "相手のアタック時",
            ["contingencyTriggers:ComboReached"] = "コンボ達成時",
            ["characterTraitTriggers:OnTurnStart"] = "ターン開始時",
            ["characterTraitTriggers:OnComboReached"] = "最大コンボ達成時",
            ["characterTraitTriggers:OnContingencyTriggered"] = "伏策発動後",
            ["characterTraitTriggers:OnResonanceConsumed"] = "共鳴発動後",

            ["actions:attack"] = "攻撃", ["actions:block"] = "ブロック", ["actions:buff"] = "バフ",
            ["actions:debuff"] = "デバフ", ["actions:heal"] = "回復", ["actions:draw"] = "ドロー",
            ["actions:discard"] = "捨てる", ["actions:gainEnergy"] = "エナジー獲得",
            ["actions:gainComboGauge"] = "コンボゲージ獲得",
            ["actions:gainMaxEnergy"] = "ターン開始エナジー増加", ["actions:exhaustCard"] = "カード除外",
            ["actions:upgrade"] = "カード強化", ["actions:summon"] = "召喚", ["actions:selfDamage"] = "自傷",
            ["actions:addCard"] = "カード追加", ["actions:recoverFromDiscard"] = "捨札回収",
            ["actions:recoverFromExhaust"] = "除外回収",
            ["actions:modifyCardCost"] = "カードコスト変更", ["actions:gainMaxHp"] = "最大HP増加",
            ["actions:gainGold"] = "ゴールド獲得", ["actions:healHp"] = "HP回復",
            ["actions:energyPerTurnBonus"] = "ターン開始エナジー補正",
            ["actions:cardsDrawnPerTurnBonus"] = "ターンドロー枚数補正",
            ["actions:goldRewardMultiplier"] = "ゴールド報酬倍率", ["actions:shopPriceMultiplier"] = "商店価格倍率",
            ["actions:rewardCardChoicesBonus"] = "報酬カード候補数補正",
            ["actions:rewardRerollAvailable"] = "報酬リロール有効化",
            ["actions:unknownTileWeightDelta"] = "不明マス重み補正", ["actions:restHealBonus"] = "休息回復量補正",

            ["triggers:OnPickup"] = "取得時", ["triggers:OnBattleStart"] = "戦闘開始時",
            ["triggers:OnBattleEnd"] = "戦闘終了時", ["triggers:OnTurnStart"] = "ターン開始時",
            ["triggers:OnTurnEnd"] = "ターン終了時", ["triggers:OnPlayCard"] = "カード使用時",
            ["triggers:OnEnemyDeath"] = "敵撃破時", ["triggers:OnDamageReceived"] = "被ダメージ時",
            ["triggers:OnHit"] = "攻撃命中時", ["triggers:OnCombo"] = "コンボ時",
            ["triggers:OnMapTileResolved"] = "マス解決時", ["triggers:OnCardDiscarded"] = "カード破棄時",
            ["triggers:OnCardExhausted"] = "カード除外時", ["triggers:OnEnterShop"] = "商店入場時",
            ["triggers:OnEnterRestSite"] = "休息所入場時", ["triggers:OnRest"] = "休息時",
            ["triggers:OnRewardGenerated"] = "報酬生成時", ["triggers:OnCardAddedToDeck"] = "デッキ追加時",
            ["triggers:Passive"] = "常時",

            ["amountSources:handCount"] = "手札枚数", ["amountSources:drawPileCount"] = "山札枚数",
            ["amountSources:discardPileCount"] = "捨札枚数", ["amountSources:exhaustPileCount"] = "除外枚数",
            ["amountSources:selfHp"] = "自身の現在HP", ["amountSources:selfHpLost"] = "自身の減少HP",
            ["amountSources:selfBlock"] = "自身のブロック", ["amountSources:comboCount"] = "現在コンボ",
            ["amountSources:maxComboThisTurn"] = "このターンの最大コンボ",
            ["amountSources:energy"] = "現在エナジー", ["amountSources:powerCardCount"] = "パワーカード枚数",
            ["amountSources:currentCost"] = "対象カードの現在コスト",
            ["amountSources:selfAttack"] = "自身の攻撃力", ["amountSources:selfDefense"] = "自身の守備力",

            ["eventEffectTypes:gainGold"] = "ゴールド獲得", ["eventEffectTypes:payGold"] = "ゴールド支払い",
            ["eventEffectTypes:heal"] = "HP回復", ["eventEffectTypes:takeDamage"] = "ダメージを受ける",
            ["eventEffectTypes:gainMaxHp"] = "最大HP増加", ["eventEffectTypes:loseMaxHp"] = "最大HP減少",
            ["eventEffectTypes:gainRelicRandom"] = "ランダムレリック獲得",
            ["eventEffectTypes:grantCardReward"] = "カード報酬獲得",
            ["eventEffectTypes:gainCard"] = "指定カード獲得",
            ["eventConditionTypes:minGold"] = "必要ゴールド", ["eventConditionTypes:minHp"] = "必要HP",

            ["statuses:strength"] = "筋力", ["statuses:dexterity"] = "敏捷", ["statuses:omnistrike"] = "拡散",
            ["statuses:vulnerable"] = "脆弱", ["statuses:weak"] = "脱力", ["statuses:poison"] = "毒",
            ["statuses:barrier"] = "バリア", ["statuses:lifesteal"] = "吸血", ["statuses:reflect"] = "反射",
            ["statuses:handCostReduction"] = "節制", ["statuses:handCostIncrease"] = "浪費",
            ["statuses:vigor"] = "元気", ["statuses:pursuit"] = "追撃", ["statuses:regeneration"] = "再生",
            ["statuses:fortify"] = "堅牢", ["statuses:resistance"] = "耐性", ["statuses:revive"] = "復活",
            ["statuses:growth"] = "成長", ["statuses:insight"] = "洞察", ["statuses:haste"] = "加速",
            ["statuses:continuity"] = "継続", ["statuses:mastery"] = "熟練",
            ["statuses:afterimage"] = "残光", ["statuses:steadfast"] = "堅持",
            ["statuses:cover"] = "護衛", ["statuses:defensiveStance"] = "守勢",
            ["statuses:comboCarryover"] = "連環", ["statuses:afterglow"] = "余韻",
            ["statuses:resonance"] = "共鳴",
            ["statuses:virulentPoison"] = "猛毒", ["statuses:stagnation"] = "停滞", ["statuses:burn"] = "火傷",
            ["statuses:scorch"] = "灼痕",
            ["statuses:confusion"] = "混乱", ["statuses:curse"] = "呪縛", ["statuses:bind"] = "束縛",
            ["statuses:silence"] = "沈黙", ["statuses:seal"] = "封印", ["statuses:exhaustion"] = "消耗",
            ["statuses:distress"] = "動揺", ["statuses:disruption"] = "攪乱",
        });

    public static IReadOnlyList<string> EffectActions { get; } = Array.AsReadOnly(new[]
    {
        "attack", "block", "buff", "debuff", "heal", "draw", "discard",
        "gainEnergy", "gainComboGauge", "gainMaxEnergy", "exhaustCard", "upgrade", "summon",
        "selfDamage", "addCard", "recoverFromDiscard", "recoverFromExhaust", "modifyCardCost",
        "gainMaxHp", "gainGold", "healHp", "energyPerTurnBonus",
        "cardsDrawnPerTurnBonus", "goldRewardMultiplier", "shopPriceMultiplier",
        "rewardCardChoicesBonus", "rewardRerollAvailable", "unknownTileWeightDelta",
        "restHealBonus",
    });

    public static IReadOnlyList<string> Triggers { get; } = Array.AsReadOnly(new[]
    {
        "OnPickup", "OnBattleStart", "OnBattleEnd", "OnTurnStart", "OnTurnEnd",
        "OnPlayCard", "OnEnemyDeath", "OnDamageReceived", "OnHit", "OnCombo",
        "OnMapTileResolved", "OnCardDiscarded", "OnCardExhausted", "OnEnterShop",
        "OnEnterRestSite", "OnRest", "OnRewardGenerated", "OnCardAddedToDeck", "Passive",
    });

    public static IReadOnlyList<string> AmountSources { get; } = Array.AsReadOnly(new[]
    {
        "handCount", "drawPileCount", "discardPileCount", "exhaustPileCount",
        "selfHp", "selfHpLost", "selfBlock", "comboCount", "maxComboThisTurn", "energy", "powerCardCount",
        "selfAttack", "selfDefense",
        // modifyCardCost only: resolve once per selected card so "set cost to zero"
        // remains correct even when the selected cards currently have different costs.
        "currentCost",
    });

    public static IReadOnlyList<string> EventEffectTypes { get; } = Array.AsReadOnly(new[]
    {
        "gainGold", "payGold", "heal", "takeDamage", "gainMaxHp", "loseMaxHp",
        "gainRelicRandom", "grantCardReward", "gainCard",
    });

    public static IReadOnlyList<string> EventConditionTypes { get; } = Array.AsReadOnly(new[]
    {
        "minGold", "minHp",
    });

    public static IReadOnlyList<string> CharacterClasses { get; } =
        Array.AsReadOnly(Enum.GetNames<CharacterClass>());

    public static IReadOnlyList<string> CardPoolClasses { get; } =
        Array.AsReadOnly(Enum.GetNames<CardPoolClass>());

    public static IReadOnlyList<string> CardPoolKinds { get; } =
        Array.AsReadOnly(Enum.GetNames<CardPoolKind>());

    public static IReadOnlyList<string> CardAbilityTypes { get; } =
        Array.AsReadOnly(new[] { "finisher", "reprise", "contingency" });

    public static IReadOnlyList<string> ContingencyTriggerTimings { get; } =
        Array.AsReadOnly(Enum.GetNames<RoguelikeCardGame.Core.Cards.ContingencyTriggerTiming>());

    public static IReadOnlyList<string> CharacterTraitTriggers { get; } =
        Array.AsReadOnly(Enum.GetNames<CharacterTraitTrigger>());
}
