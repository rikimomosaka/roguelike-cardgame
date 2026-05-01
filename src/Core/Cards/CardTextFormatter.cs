using System.Collections.Generic;
using System.Linq;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// CardEffect 配列から日本語 description を生成する純関数 (v2)。
/// JSON の description override が空のときに利用される。
///
/// 出力には marker syntax を含む:
///   [N:5]            数字 (Client で黄色表示)
///   [K:wild]         キーワード ID (Client で表示名 / 色変え)
///   [T:OnTurnStart]  power カードの発火タイミング
///   [V:X|手札の数]    Variable X (AmountSource を持つ場合)
///   [C:strike]       カード参照 (addCard 等)
///
/// 関連 spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §1-3 / §2.
/// </summary>
public static class CardTextFormatter
{
    /// <summary>
    /// CardDefinition と upgraded フラグから表示テキストを生成する。
    /// override (Description/UpgradedDescription) が非空ならそれを返し、
    /// 空ならその時点の effects 配列 + キーワード行から自動生成する。
    /// </summary>
    public static string Format(CardDefinition def, bool upgraded)
    {
        string? manual = upgraded ? def.UpgradedDescription : def.Description;
        if (!string.IsNullOrWhiteSpace(manual)) return manual!;

        var keywords = def.EffectiveKeywords(upgraded);
        var keywordLines = keywords is null
            ? Enumerable.Empty<string>()
            : keywords.Select(k => $"[K:{k}]");

        var effects = upgraded && def.UpgradedEffects is not null
            ? def.UpgradedEffects
            : def.Effects;
        var effectText = FormatEffects(effects);

        var allLines = keywordLines.Concat(effectText.Length == 0
            ? Enumerable.Empty<string>()
            : effectText.Split('\n'));
        return string.Join("\n", allLines);
    }

    /// <summary>
    /// effects 配列単体から description 文字列を組み立てる。テスト・dev tool プレビュー用。
    /// </summary>
    public static string FormatEffects(IReadOnlyList<CardEffect> effects)
    {
        if (effects.Count == 0) return string.Empty;

        // 連続する完全同一 spec を 1 個にまとめて " × [N:N] 回" 表記。
        var grouped = GroupConsecutive(effects);
        var sentences = grouped.Select(g => DescribeGroup(g.Effect, g.Count));
        return string.Join("\n", sentences);
    }

    private record EffectGroup(CardEffect Effect, int Count);

    private static IEnumerable<EffectGroup> GroupConsecutive(IReadOnlyList<CardEffect> effects)
    {
        var result = new List<EffectGroup>();
        foreach (var e in effects)
        {
            if (result.Count > 0 && IsSameSpec(result[^1].Effect, e))
            {
                result[^1] = new EffectGroup(result[^1].Effect, result[^1].Count + 1);
            }
            else
            {
                result.Add(new EffectGroup(e, 1));
            }
        }
        return result;
    }

    private static bool IsSameSpec(CardEffect a, CardEffect b)
        => a.Action == b.Action
        && a.Scope == b.Scope
        && a.Side == b.Side
        && a.Amount == b.Amount
        && a.Name == b.Name
        && a.UnitId == b.UnitId
        && a.CardRefId == b.CardRefId
        && a.Select == b.Select
        && a.AmountSource == b.AmountSource
        && a.Trigger == b.Trigger
        && a.Pile == b.Pile;

    private static string DescribeGroup(CardEffect e, int count)
    {
        var head = DescribeOne(e);
        var triggerPrefix = !string.IsNullOrEmpty(e.Trigger) ? $"[T:{e.Trigger}]の度に" : "";
        if (count <= 1) return triggerPrefix + head + "。";
        return triggerPrefix + head + " × [N:" + count + "] 回。";
    }

    private static string DescribeOne(CardEffect e) => e.Action switch
    {
        "attack" => DescribeAttack(e),
        "block" => DescribeBlock(e),
        "draw" => $"カードを {AmountToken(e)} 枚引く",
        "discard" => DescribeDiscard(e),
        "buff" => DescribeStatusChange(e, isDebuff: false),
        "debuff" => DescribeStatusChange(e, isDebuff: true),
        "heal" => DescribeHeal(e),
        "summon" => $"{e.UnitId ?? "ユニット"} を召喚",
        "exhaustCard" => $"手札 {AmountToken(e)} 枚を除外",
        "exhaustSelf" => "このカードを除外",
        "retainSelf" => "このカードを次ターンに持ち越す",
        "gainEnergy" => $"エナジー +{AmountToken(e)}",
        "gainMaxEnergy" => $"エナジー上限を+{AmountToken(e)}する",
        "upgrade" => $"カード {AmountToken(e)} 枚を強化",
        "selfDamage" => $"自身のHPを-{AmountToken(e)}",
        "addCard" => DescribeAddCard(e),
        "recoverFromDiscard" => DescribeRecoverFromDiscard(e),
        _ => $"(未対応 action: {e.Action})",
    };

    private static string DescribeAttack(CardEffect e) => e.Scope switch
    {
        EffectScope.Single => $"敵 1 体に {AmountToken(e)} ダメージ",
        EffectScope.Random => $"敵ランダム 1 体に {AmountToken(e)} ダメージ",
        EffectScope.All => $"敵全体に {AmountToken(e)} ダメージ",
        _ => $"敵に {AmountToken(e)} ダメージ",
    };

    private static string DescribeBlock(CardEffect e) => (e.Scope, e.Side) switch
    {
        (EffectScope.Self, _) => $"ブロック {AmountToken(e)} を得る",
        (EffectScope.Single, EffectSide.Ally) => $"味方 1 体にブロック {AmountToken(e)}",
        (EffectScope.All, EffectSide.Ally) => $"味方全体にブロック {AmountToken(e)}",
        _ => $"ブロック {AmountToken(e)}",
    };

    private static string DescribeHeal(CardEffect e) => (e.Scope, e.Side) switch
    {
        (EffectScope.Self, _) => $"HP を {AmountToken(e)} 回復",
        (EffectScope.Single, EffectSide.Ally) => $"味方 1 体の HP を {AmountToken(e)} 回復",
        (EffectScope.All, EffectSide.Ally) => $"味方全体の HP を {AmountToken(e)} 回復",
        _ => $"HP を {AmountToken(e)} 回復",
    };

    private static string DescribeStatusChange(CardEffect e, bool isDebuff)
    {
        var jpName = JpStatusName(e.Name);
        var target = (e.Scope, e.Side) switch
        {
            (EffectScope.Self, _) => "自身",
            (EffectScope.Single, EffectSide.Enemy) => "敵 1 体",
            (EffectScope.Single, EffectSide.Ally) => "味方 1 体",
            (EffectScope.All, EffectSide.Enemy) => "敵全体",
            (EffectScope.All, EffectSide.Ally) => "味方全体",
            (EffectScope.Random, EffectSide.Enemy) => "敵ランダム 1 体",
            (EffectScope.Random, EffectSide.Ally) => "味方ランダム 1 体",
            _ => "対象",
        };
        return $"{target}に {jpName} {AmountToken(e)} を付与";
    }

    private static string DescribeDiscard(CardEffect e)
    {
        if (string.IsNullOrEmpty(e.Select))
        {
            // 旧仕様 (Select なし) はランダム扱いの簡略文言
            return $"手札 {AmountToken(e)} 枚を捨てる";
        }
        return e.Select switch
        {
            "choose" => $"手札を選んで {AmountToken(e)} 枚捨てる",
            "random" => $"手札からランダムに {AmountToken(e)} 枚捨てる",
            "all" => "手札を全て捨てる",
            _ => $"手札 {AmountToken(e)} 枚を捨てる",
        };
    }

    private static string DescribeAddCard(CardEffect e)
    {
        var cardRef = !string.IsNullOrEmpty(e.CardRefId) ? $"[C:{e.CardRefId}]" : "[C:?]";
        var zone = ZoneJp(e.Pile);
        return $"{cardRef} を{zone}に {AmountToken(e)} 枚加える";
    }

    private static string DescribeRecoverFromDiscard(CardEffect e)
    {
        var selectJp = SelectJp(e.Select);
        var dest = ZoneJp(e.Pile);
        // exhaust の場合は「除外する」、それ以外は「<dest>に戻す」
        if (e.Pile == "exhaust")
        {
            return $"捨札から{selectJp} {AmountToken(e)} 枚、除外する";
        }
        return $"捨札から{selectJp} {AmountToken(e)} 枚、{dest}に戻す";
    }

    private static string AmountToken(CardEffect e)
    {
        if (string.IsNullOrEmpty(e.AmountSource)) return $"[N:{e.Amount}]";
        var label = AmountSourceJp(e.AmountSource);
        // 1 個目変数は X (将来 Y/Z 採番予定。本フェーズでは X 固定)
        return $"[V:X|{label}]";
    }

    private static string AmountSourceJp(string src) => src switch
    {
        "handCount" => "手札の数",
        "drawPileCount" => "山札の数",
        "discardPileCount" => "捨札の数",
        "exhaustPileCount" => "除外の数",
        "selfHp" => "自身のHP",
        "comboCount" => "現在のコンボ",
        _ => src,
    };

    private static string ZoneJp(string? pile) => pile switch
    {
        "hand" => "手札",
        "draw" => "山札",
        "discard" => "捨札",
        "exhaust" => "除外",
        null or "" => "手札",
        _ => pile,
    };

    private static string SelectJp(string? select) => select switch
    {
        "random" => "ランダムに",
        "choose" => "選んで",
        "all" => "全て",
        null or "" => "",
        _ => select,
    };

    private static string JpStatusName(string? id) => id switch
    {
        "weak" => "脱力",
        "vulnerable" => "脆弱",
        "strength" => "筋力",
        "dexterity" => "敏捷",
        "poison" => "毒",
        "omnistrike" => "全体攻撃",
        null or "" => "ステータス",
        _ => id,
    };
}
