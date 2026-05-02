using System.Collections.Generic;
using System.Linq;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// CardEffect 配列から日本語 description を生成する純関数 (v2)。
/// JSON の description override が空のときに利用される。
///
/// 出力には marker syntax を含む:
///   [N:5]            数字 (Client で黄色表示)
///   [N:7|up]         数字 (context 適用後 base より上振れ → 赤、10.5.C)
///   [N:3|down]       数字 (context 適用後 base より下振れ → 青、10.5.C)
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
    /// CardDefinition と upgraded フラグから表示テキストを生成する (context 無し)。
    /// 既存呼出互換のため <see cref="CardActorContext.Empty"/> を渡す薄いラッパ。
    /// </summary>
    public static string Format(CardDefinition def, bool upgraded)
        => Format(def, upgraded, CardActorContext.Empty);

    /// <summary>
    /// CardDefinition + upgraded + actor context から表示テキストを生成する。
    /// override (Description/UpgradedDescription) が非空ならそれを返し、
    /// 空ならその時点の effects 配列 + キーワード行から自動生成する。
    /// context が <see cref="CardActorContext.Empty"/> でない場合、attack / block の
    /// amount は context により調整され、base と異なれば |up / |down マーカーを emit する。
    /// </summary>
    public static string Format(CardDefinition def, bool upgraded, CardActorContext context)
    {
        string? manual = upgraded ? def.UpgradedDescription : def.Description;
        if (!string.IsNullOrWhiteSpace(manual)) return manual!;

        var keywords = def.EffectiveKeywords(upgraded);
        // Phase 10.5.M3: 複数キーワードは "/" 区切りで 1 行に並べる
        //   (Client 側 CardDesc が `/` を白文字で描画する)。
        var keywordLine = keywords is null || keywords.Count == 0
            ? null
            : string.Join("/", keywords.Select(k => $"[K:{k}]"));

        var effects = upgraded && def.UpgradedEffects is not null
            ? def.UpgradedEffects
            : def.Effects;
        var effectText = FormatEffects(effects, context);

        var lines = new List<string>();
        if (keywordLine is not null) lines.Add(keywordLine);
        if (effectText.Length > 0) lines.AddRange(effectText.Split('\n'));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// effects 配列単体から description 文字列を組み立てる (context 無し)。
    /// 既存呼出互換のため <see cref="CardActorContext.Empty"/> を渡す薄いラッパ。
    /// </summary>
    public static string FormatEffects(IReadOnlyList<CardEffect> effects)
        => FormatEffects(effects, CardActorContext.Empty);

    /// <summary>
    /// effects 配列単体 + context から description 文字列を組み立てる。
    /// </summary>
    public static string FormatEffects(IReadOnlyList<CardEffect> effects, CardActorContext context)
    {
        if (effects.Count == 0) return string.Empty;

        // 連続する完全同一 spec を 1 個にまとめて " × [N:N] 回" 表記。
        var grouped = GroupConsecutive(effects);
        var sentences = grouped.Select(g => DescribeGroup(g.Effect, g.Count, context));
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

    private static string DescribeGroup(CardEffect e, int count, CardActorContext context)
    {
        var head = DescribeOne(e, context);
        var triggerPrefix = !string.IsNullOrEmpty(e.Trigger) ? $"[T:{e.Trigger}]の度に" : "";
        if (count <= 1) return triggerPrefix + head + "。";
        // M4: 「× N 回」→「× N」(回 を除去)
        return triggerPrefix + head + " × [N:" + count + "]。";
    }

    private static string DescribeOne(CardEffect e, CardActorContext context) => e.Action switch
    {
        "attack" => DescribeAttack(e, context),
        "block" => DescribeBlock(e, context),
        "draw" => $"{AmountToken(e, context)} 枚引く",
        "drawCards" => $"{AmountToken(e, context)} 枚引く",
        "discard" => DescribeDiscard(e, context),
        "buff" => DescribeStatusChange(e, context, isDebuff: false),
        "debuff" => DescribeStatusChange(e, context, isDebuff: true),
        "heal" => DescribeHeal(e, context),
        "summon" => $"{e.UnitId ?? "ユニット"} を召喚",
        "exhaustCard" => DescribeExhaustOrUpgrade(e, context, isUpgrade: false),
        // Phase 10.5.M3: exhaustSelf も keyword "exhaust" 化。後方互換のため
        // formatter は marker を emit して、二段ポップで定義を出す。
        "exhaustSelf" => "[K:exhaust]",
        // retainSelf は Phase 10.5.M2 で「待機」キーワード化された。後方互換のため
        // formatter は文言を残すが、新規カードではキーワードを使うことを推奨。
        "retainSelf" => "[K:wait]",
        "gainEnergy" => $"エナジー +{AmountToken(e, context)}",
        "gainMaxEnergy" => $"エナジー上限+{AmountToken(e, context)}",
        "upgrade" => DescribeExhaustOrUpgrade(e, context, isUpgrade: true),
        "selfDamage" => $"{TargetPrefix(e.Scope, e.Side)}{AmountToken(e, context)} ダメージ",
        "addCard" => DescribeAddCard(e, context),
        "recoverFromDiscard" => DescribeRecoverFromDiscard(e, context),
        _ => $"(未対応 action: {e.Action})",
    };

    /// <summary>
    /// (Scope, Side) の組から日本語の対象表現プレフィックスを返す統一ヘルパ。
    /// 例: (Self, _) → "自身に "、(Single, Enemy) → "敵単体に "、(All, Ally) → "味方全体に "。
    /// 全 action (attack/block/buff/debuff/heal 等) で同じ法則を適用する。
    /// 末尾に半角スペースを含める (続く amount/status/etc との分離用)。
    /// </summary>
    private static string TargetPrefix(EffectScope scope, EffectSide? side) => (scope, side) switch
    {
        (EffectScope.Self, _) => "自身に ",
        (EffectScope.Single, EffectSide.Enemy) => "敵単体に ",
        (EffectScope.Single, EffectSide.Ally) => "味方単体に ",
        // Random は「ランダムな (敵|味方) に」(M4: 単体 を除去)
        (EffectScope.Random, EffectSide.Enemy) => "ランダムな敵に ",
        (EffectScope.Random, EffectSide.Ally) => "ランダムな味方に ",
        (EffectScope.All, EffectSide.Enemy) => "敵全体に ",
        (EffectScope.All, EffectSide.Ally) => "味方全体に ",
        // side が null のフォールバック
        (EffectScope.Single, _) => "対象に ",
        (EffectScope.Random, _) => "ランダムな対象に ",
        (EffectScope.All, _) => "全体に ",
        _ => "",
    };

    private static string DescribeAttack(CardEffect e, CardActorContext context)
        => $"{TargetPrefix(e.Scope, e.Side)}{AmountToken(e, context)} アタック";

    private static string DescribeBlock(CardEffect e, CardActorContext context)
        => $"{TargetPrefix(e.Scope, e.Side)}{AmountToken(e, context)} ブロック";

    /// <summary>
    /// exhaustCard / upgrade を Select (random/choose/all) + Pile (hand/draw/discard) の
    /// 組合せで翻訳する。共通フォーマット:
    ///   Select=all → "{zone}を全て {verb}"
    ///   Select=random → "{zone}を {amount} 枚ランダムに {verb}"
    ///   Select=choose / null → "{zone}を {amount} 枚選んで {verb}"
    /// </summary>
    private static string DescribeExhaustOrUpgrade(CardEffect e, CardActorContext context, bool isUpgrade)
    {
        var zone = ZoneJp(e.Pile);
        var verb = isUpgrade ? "強化" : "除外";
        return e.Select switch
        {
            "all" => $"{zone}を全て{verb}",
            "random" => $"{zone}を {AmountToken(e, context)} 枚ランダムに{verb}",
            _ => $"{zone}を {AmountToken(e, context)} 枚選んで{verb}",
        };
    }

    private static string DescribeHeal(CardEffect e, CardActorContext context)
        => $"{TargetPrefix(e.Scope, e.Side)}HP を {AmountToken(e, context)} 回復";

    private static string DescribeStatusChange(CardEffect e, CardActorContext context, bool isDebuff)
    {
        // Phase 10.5.M2: status 名を [S:id] marker として emit。Client 側で JP 名と
        //  ホバー定義 popup へ展開する (旧: bare JP 文字列、scan 困難だった)。
        var statusToken = string.IsNullOrEmpty(e.Name) ? "ステータス" : $"[S:{e.Name}]";
        return $"{TargetPrefix(e.Scope, e.Side)}{statusToken} {AmountToken(e, context)} を付与";
    }

    private static string DescribeDiscard(CardEffect e, CardActorContext context)
    {
        if (string.IsNullOrEmpty(e.Select))
        {
            // 旧仕様 (Select なし) はランダム扱いの簡略文言
            return $"手札 {AmountToken(e, context)} 枚を捨てる";
        }
        return e.Select switch
        {
            "choose" => $"手札を選んで {AmountToken(e, context)} 枚捨てる",
            "random" => $"手札からランダムに {AmountToken(e, context)} 枚捨てる",
            "all" => "手札を全て捨てる",
            _ => $"手札 {AmountToken(e, context)} 枚を捨てる",
        };
    }

    private static string DescribeAddCard(CardEffect e, CardActorContext context)
    {
        var cardRef = !string.IsNullOrEmpty(e.CardRefId) ? $"[C:{e.CardRefId}]" : "[C:?]";
        var zone = ZoneJp(e.Pile);
        return $"{cardRef} を{zone}に {AmountToken(e, context)} 枚加える";
    }

    private static string DescribeRecoverFromDiscard(CardEffect e, CardActorContext context)
    {
        var selectJp = SelectJp(e.Select);
        var dest = ZoneJp(e.Pile);
        // exhaust の場合は「除外する」、それ以外は「<dest>に戻す」
        if (e.Pile == "exhaust")
        {
            return $"捨札から{selectJp} {AmountToken(e, context)} 枚、除外する";
        }
        return $"捨札から{selectJp} {AmountToken(e, context)} 枚、{dest}に戻す";
    }

    /// <summary>
    /// effect の amount を context で調整した上で marker token を返す。
    /// AmountSource (Variable X) があればそちらを優先 (10.5.D で engine 評価予定)。
    /// 調整後 == base なら無修飾 [N:base]、上振れ |up、下振れ |down を付ける。
    /// </summary>
    private static string AmountToken(CardEffect e, CardActorContext context)
    {
        if (!string.IsNullOrEmpty(e.AmountSource))
        {
            var label = AmountSourceJp(e.AmountSource);
            // 1 個目変数は X (将来 Y/Z 採番予定。本フェーズでは X 固定)
            return $"[V:X|{label}]";
        }
        int adjusted = AdjustAmount(e, context);
        if (adjusted == e.Amount) return $"[N:{e.Amount}]";
        var modifier = adjusted > e.Amount ? "up" : "down";
        return $"[N:{adjusted}|{modifier}]";
    }

    /// <summary>
    /// attack: strength 加算 → weak>0 なら 0.75 倍 (floor)。
    /// block: dexterity を単純加算。
    /// その他 action は当面比較対象外で base を返す。
    /// engine 側 (BattleEngine / EffectApplier) の計算とずれる場合は formatter 内で完結。
    /// </summary>
    private static int AdjustAmount(CardEffect e, CardActorContext ctx)
    {
        if (e.Action == "attack")
        {
            int withStr = e.Amount + ctx.Strength;
            return ctx.Weak > 0 ? (int)(withStr * 0.75) : withStr;
        }
        if (e.Action == "block")
        {
            return e.Amount + ctx.Dexterity;
        }
        return e.Amount;
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
        null or "" => "手札",  // 旧 effect (Pile 未指定) は手札を想定
        _ => pile!,
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
