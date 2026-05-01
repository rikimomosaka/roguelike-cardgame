using System.Collections.Generic;
using System.Linq;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// CardEffect 配列から日本語 description を生成する純関数。
/// JSON の description override が空のときに利用される。
/// 関連 spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §2.
/// </summary>
public static class CardTextFormatter
{
    /// <summary>
    /// CardDefinition と upgraded フラグから表示テキストを生成する。
    /// override (Description/UpgradedDescription) が非空ならそれを返し、
    /// 空ならその時点の effects 配列から自動生成する。
    /// </summary>
    public static string Format(CardDefinition def, bool upgraded)
    {
        string? manual = upgraded ? def.UpgradedDescription : def.Description;
        if (!string.IsNullOrWhiteSpace(manual)) return manual!;

        var effects = upgraded && def.UpgradedEffects is not null
            ? def.UpgradedEffects
            : def.Effects;
        return FormatEffects(effects);
    }

    /// <summary>
    /// effects 配列単体から description 文字列を組み立てる。テスト・dev tool プレビュー用。
    /// </summary>
    public static string FormatEffects(IReadOnlyList<CardEffect> effects)
    {
        if (effects.Count == 0) return string.Empty;

        // 連続する同一 (action, scope, side, amount, name, unitId) を 1 個にまとめて " × N 回" 表記。
        var grouped = GroupConsecutive(effects);
        var sentences = grouped.Select(g => DescribeGroup(g.Effect, g.Count));
        return string.Join("\n", sentences);
    }

    private record EffectGroup(CardEffect Effect, int Count);

    private static IEnumerable<EffectGroup> GroupConsecutive(IReadOnlyList<CardEffect> effects)
    {
        // 連続して同 spec が並ぶ部分のみ畳む（discontiguous は別グループ）
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
        && a.UnitId == b.UnitId;

    private static string DescribeGroup(CardEffect e, int count)
    {
        var head = DescribeOne(e);
        if (count <= 1) return head + "。";
        return head + " × " + count + " 回。";
    }

    private static string DescribeOne(CardEffect e) => e.Action switch
    {
        "attack" => DescribeAttack(e),
        "block" => DescribeBlock(e),
        "draw" => $"カードを {e.Amount} 枚引く",
        "discard" => $"手札 {e.Amount} 枚を捨てる",
        "buff" => DescribeStatusChange(e, isDebuff: false),
        "debuff" => DescribeStatusChange(e, isDebuff: true),
        "heal" => DescribeHeal(e),
        "summon" => $"{e.UnitId ?? "ユニット"} を召喚",
        "exhaustCard" => $"手札 {e.Amount} 枚を除外",
        "exhaustSelf" => "このカードを除外",
        "retainSelf" => "このカードを次ターンに持ち越す",
        "gainEnergy" => $"エナジー +{e.Amount}",
        "upgrade" => $"カード {e.Amount} 枚を強化",
        _ => $"(未対応 action: {e.Action})",
    };

    private static string DescribeAttack(CardEffect e) => e.Scope switch
    {
        EffectScope.Single => $"敵 1 体に {e.Amount} ダメージ",
        EffectScope.Random => $"敵ランダム 1 体に {e.Amount} ダメージ",
        EffectScope.All => $"敵全体に {e.Amount} ダメージ",
        _ => $"敵に {e.Amount} ダメージ",
    };

    private static string DescribeBlock(CardEffect e) => (e.Scope, e.Side) switch
    {
        (EffectScope.Self, _) => $"ブロック {e.Amount} を得る",
        (EffectScope.Single, EffectSide.Ally) => $"味方 1 体にブロック {e.Amount}",
        (EffectScope.All, EffectSide.Ally) => $"味方全体にブロック {e.Amount}",
        _ => $"ブロック {e.Amount}",
    };

    private static string DescribeHeal(CardEffect e) => (e.Scope, e.Side) switch
    {
        (EffectScope.Self, _) => $"HP を {e.Amount} 回復",
        (EffectScope.Single, EffectSide.Ally) => $"味方 1 体の HP を {e.Amount} 回復",
        (EffectScope.All, EffectSide.Ally) => $"味方全体の HP を {e.Amount} 回復",
        _ => $"HP を {e.Amount} 回復",
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
        return $"{target}に {jpName} {e.Amount}";
    }

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
