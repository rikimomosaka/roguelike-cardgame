using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Statuses;

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
        var keywords = def.EffectiveKeywords(upgraded);
        string mainBody;
        string conditionalBody = string.Empty;
        if (!string.IsNullOrWhiteSpace(manual))
        {
            mainBody = manual!;
        }
        else
        {
            var effects = upgraded && def.UpgradedEffects is not null
                ? def.UpgradedEffects
                : def.Effects;
            // カード面では、通常効果の後に乗勢等の条件付き効果をまとめる。
            // FormatEffects 単体は解決順を表す共通 API なので、表示用の整列はここだけで行う。
            mainBody = FormatEffects(
                effects.Where(effect => effect.MaxComboMin is null).ToArray(),
                context);
            conditionalBody = FormatEffects(
                effects.Where(effect => effect.MaxComboMin is not null).ToArray(),
                context);
        }

        var body = FormatCardAbilities(
            mainBody,
            conditionalBody,
            keywords,
            def.EffectiveAbilities(upgraded));

        var limit = def.EffectiveUsesPerTurn(upgraded);
        var usageLine = limit > 0
            ? $"ターン中{limit}度だけ使用可能(0/{limit})。"
            : null;
        return usageLine is null
            ? body
            : string.IsNullOrEmpty(body) ? usageLine : $"{usageLine}\n{body}";
    }

    /// <summary>
    /// Formats a card and replaces its card-level per-turn usage counter with
    /// the authoritative count for this card instance.
    /// </summary>
    public static string FormatWithUsageCount(
        CardDefinition def,
        bool upgraded,
        CardActorContext context,
        int usageCount)
    {
        var text = Format(def, upgraded, context);
        var limit = def.EffectiveUsesPerTurn(upgraded);
        if (limit <= 0) return text;

        return text.Replace(
            $"(0/{limit})",
            $"({Math.Clamp(usageCount, 0, limit)}/{limit})",
            System.StringComparison.Ordinal);
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

    /// <summary>
    /// Shared card/skill text projection. Keyword markers are emitted before
    /// effect sentences so the Client can render their definitions below the
    /// dashed separator, exactly like a card tooltip.
    /// </summary>
    public static string FormatEffectsWithKeywords(
        IReadOnlyList<CardEffect> effects,
        IReadOnlyList<string>? keywords,
        CardActorContext context)
    {
        var lines = new List<string>();
        if (keywords is { Count: > 0 })
            lines.Add(string.Join("/", keywords.Select(k => $"[K:{k}]")));
        var effectText = FormatEffects(effects, context);
        if (effectText.Length > 0)
            lines.AddRange(effectText.Split('\n'));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Shared entity-level usage header followed by the same keyword/effect
    /// text used for cards, potions, and character skills.
    /// </summary>
    public static string FormatUsableEffects(
        IReadOnlyList<CardEffect> effects,
        IReadOnlyList<string>? keywords,
        CardActorContext context,
        int usesPerTurn,
        int usesThisTurn = 0)
    {
        var body = FormatEffectsWithKeywords(effects, keywords, context);
        if (usesPerTurn <= 0) return body;

        var usage = $"ターン中{usesPerTurn}度だけ使用可能({Math.Clamp(usesThisTurn, 0, usesPerTurn)}/{usesPerTurn})。";
        return string.IsNullOrEmpty(body) ? usage : $"{usage}\n{body}";
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
        && a.ComboMin == b.ComboMin
        && a.MaxComboMin == b.MaxComboMin
        && a.CardRefId == b.CardRefId
        && a.Select == b.Select
        && a.AmountSource == b.AmountSource
        && a.AmountScalePercent == b.AmountScalePercent
        && a.Trigger == b.Trigger
        && a.ChancePercent == b.ChancePercent
        && a.Pile == b.Pile
        && a.CostDuration == b.CostDuration
        && a.CardCount == b.CardCount
        && a.CardTypeFilter == b.CardTypeFilter
        && StatusChangesEqual(a.StatusChanges, b.StatusChanges);

    private static bool StatusChangesEqual(
        IReadOnlyList<StatusChange>? a,
        IReadOnlyList<StatusChange>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (var index = 0; index < a.Count; index++)
        {
            if (a[index] != b[index]) return false;
        }
        return true;
    }

    private static string DescribeGroup(CardEffect e, int count, CardActorContext context)
    {
        // Phase 10.6.B: Trigger == "Passive" の effect は専用文言テーブルで処理
        if (e.Trigger == "Passive")
        {
            return DescribePassiveEffect(e);
        }

        // 猛毒はコンテンツ制約上「1を複数回」だけを許可しているが、
        // カード面では倍率表記ではなく最終付与量として読めるよう合算する。
        // 他の状態異常は耐性で各付与の結果が変わり得るため一般化しない。
        bool combineVirulentPoison = count > 1
            && e.Action == "debuff"
            && e.Name == "virulentPoison"
            && e.AmountSource is null
            && e.Amount > 0;
        var displayedEffect = combineVirulentPoison
            ? e with
            {
                Amount = (int)Math.Min(
                    int.MaxValue,
                    (long)e.Amount * count),
            }
            : e;
        var head = DescribeOne(displayedEffect, context);
        var triggerPrefix = !string.IsNullOrEmpty(e.Trigger) ? $"[T:{e.Trigger}]に" : "";
        var chancePrefix = e.ChancePercent is int chance
            ? $"[N:{chance}]%の確率で"
            : "";
        var momentumPrefix = e.MaxComboMin is int maxComboMin
            ? $"[K:momentum][N:{maxComboMin}]："
            : "";
        if (count <= 1 || combineVirulentPoison)
            return triggerPrefix + chancePrefix + momentumPrefix + head + "。";
        // Phase 10.5.M6.4: 数字 marker 周辺のスペース全削除に伴い × も詰める。
        return triggerPrefix + chancePrefix + momentumPrefix + head + "×[N:" + count + "]。";
    }

    private static string FormatCardAbilities(
        string mainBody,
        string conditionalBody,
        IReadOnlyList<string>? keywords,
        IReadOnlyList<CardAbilityDefinition> abilities)
    {
        var lines = new List<string>();

        // 表示上の優先度は Wild 系 → 終撃 → 喪失。終撃は型付き能力のままとし、
        // ここで表示マーカーだけを通常キーワードと同じ行に合流させる。
        var headerKeywordIds = new List<string>();
        if (keywords is not null)
        {
            foreach (var keyword in keywords)
            {
                if (!headerKeywordIds.Contains(keyword, System.StringComparer.Ordinal))
                    headerKeywordIds.Add(keyword);
            }
        }
        if (abilities.Any(ability => ability is FinisherAbilityDefinition) &&
            !headerKeywordIds.Contains("finisher", System.StringComparer.Ordinal))
        {
            headerKeywordIds.Add("finisher");
        }

        if (headerKeywordIds.Count > 0)
        {
            var keywordLine = string.Join("/", headerKeywordIds
                .Select((id, index) => new { Id = id, Index = index })
                .OrderBy(entry => CardHeaderKeywordPriority(entry.Id))
                .ThenBy(entry => entry.Index)
                .Select(entry => $"[K:{entry.Id}]"));
            lines.Add(keywordLine);
        }

        // 伏策条件は優先キーワード行の後、待機中に解決される本文の前に置く。
        lines.AddRange(abilities
            .OfType<ContingencyAbilityDefinition>()
            .Select(DescribeContingency));
        if (!string.IsNullOrEmpty(mainBody))
            lines.AddRange(mainBody.Split('\n'));

        // 再演は主効果の一部として、条件付き効果より前に表示する。
        lines.AddRange(abilities
            .OfType<RepriseAbilityDefinition>()
            .Select(DescribeReprise));
        if (!string.IsNullOrEmpty(conditionalBody))
            lines.AddRange(conditionalBody.Split('\n'));
        return string.Join("\n", lines);
    }

    private static int CardHeaderKeywordPriority(string id) => id switch
    {
        "wild" or "superwild" => 0,
        "finisher" => 1,
        "exhaust" => 2,
        _ => 3,
    };

    private static string DescribeReprise(RepriseAbilityDefinition ability)
        => $"[K:reprise]：{DescribeRepriseFilter(ability.Filter)}。";

    private static string DescribeRepriseFilter(RepriseCardFilter filter)
    {
        string subject = filter.EffectiveCardTypes.Count switch
        {
            0 => "カード",
            1 => CardTypeJp(filter.EffectiveCardTypes[0]),
            _ => string.Join("・", filter.EffectiveCardTypes.Select(CardTypeJp)),
        };

        string primary = DescribeCostQualifiedSubject(
            filter.MinOriginalCost,
            filter.MaxOriginalCost,
            subject,
            useOriginalPrefixForExact: false,
            prefix: "");
        var additional = new List<string>();
        if (filter.MinPaidCost is not null || filter.MaxPaidCost is not null)
        {
            additional.Add(DescribeCostQualifiedSubject(
                filter.MinPaidCost,
                filter.MaxPaidCost,
                "カード",
                useOriginalPrefixForExact: false,
                prefix: "支払"));
        }
        if (filter.EffectiveRequiredKeywords.Count > 0)
        {
            additional.Add(
                $"{string.Join("・", filter.EffectiveRequiredKeywords.Select(id => $"[K:{id}]"))}を持つカード");
        }
        return additional.Count == 0
            ? primary
            : $"{primary}かつ{string.Join("かつ", additional)}";
    }

    private static string DescribeCostQualifiedSubject(
        int? min,
        int? max,
        string subject,
        bool useOriginalPrefixForExact,
        string prefix = "")
    {
        if (min is null && max is null) return subject;
        var costLabel = prefix.Length == 0 ? "コスト" : $"{prefix}コスト";
        if (min is int exactMin && max == exactMin)
        {
            if (useOriginalPrefixForExact)
                return $"元コスト[N:{exactMin}]の{subject}";
            return $"{costLabel}[N:{exactMin}]の{subject}";
        }
        if (min is null)
            return $"{costLabel}[N:{max}]以下の{subject}";
        if (max is null)
            return $"{costLabel}[N:{min}]以上の{subject}";
        return $"{costLabel}[N:{min}]～[N:{max}]の{subject}";
    }

    private static string DescribeContingency(ContingencyAbilityDefinition ability)
    {
        string trigger = ability.Trigger.Timing switch
        {
            ContingencyTriggerTiming.TurnStart => "ターン開始時",
            ContingencyTriggerTiming.BeforeEnemyAttack => "相手のアタック時",
            ContingencyTriggerTiming.ComboReached =>
                $"[N:{ability.Trigger.ComboThreshold ?? 0}]コンボ達成時",
            _ => ability.Trigger.Timing.ToString(),
        };
        return $"[K:contingency]：{trigger}。";
    }

    private static string CardTypeJp(CardType cardType) => cardType switch
    {
        CardType.Attack => "アタック",
        CardType.Skill => "スキル",
        CardType.Power => "パワー",
        CardType.Unit => "ユニット",
        CardType.Status => "状態異常",
        CardType.Curse => "呪い",
        _ => cardType.ToString(),
    };

    private static string DescribeOne(CardEffect e, CardActorContext context) => e.Action switch
    {
        "attack" => DescribeAttack(e, context),
        "block" => DescribeBlock(e, context),
        "draw" => DescribeDraw(e, context),
        "drawCards" => DescribeDraw(e, context),
        "discard" => DescribeDiscard(e, context),
        "buff" => DescribeStatusChange(e, context, isDebuff: false),
        "debuff" => DescribeStatusChange(e, context, isDebuff: true),
        "heal" => DescribeHeal(e, context),
        // Phase 10.5.M6.7: unit ID は marker [U:id] にして Client 側で JP 名に解決。
        //  "召喚" は keyword 化 ([K:summon])、tooltip 二段目で説明 popup を出す。
        "summon" => $"[U:{e.UnitId ?? "?"}]を[K:summon]",
        "exhaustCard" => DescribeExhaustOrUpgrade(e, context, isUpgrade: false),
        // Phase 10.5.M3: exhaustSelf も keyword "exhaust" 化。後方互換のため
        // formatter は marker を emit して、二段ポップで定義を出す。
        "exhaustSelf" => "[K:exhaust]",
        // retainSelf は Phase 10.5.M2 で「待機」キーワード化された。後方互換のため
        // formatter は文言を残すが、新規カードではキーワードを使うことを推奨。
        "retainSelf" => "[K:wait]",
        "gainEnergy" => $"{ResourceOwnerPrefix(e)}エナジー+{AmountToken(e, context)}",
        "gainComboGauge" => $"{ResourceOwnerPrefix(e)}コンボゲージ+{AmountToken(e, context)}",
        "gainMaxEnergy" => $"{ResourceOwnerPrefix(e)}ターン開始時のエナジー回復量+{AmountToken(e, context)}",
        "upgrade" => DescribeExhaustOrUpgrade(e, context, isUpgrade: true),
        "selfDamage" => $"{TargetPrefix(e.Scope, e.Side)}{AmountToken(e, context)}ダメージ",
        "addCard" => DescribeAddCard(e, context),
        "recoverFromDiscard" => DescribeRecoverFromDiscard(e, context),
        "recoverFromExhaust" => DescribeRecoverFromExhaust(e, context),
        "modifyCardCost" => DescribeModifyCardCost(e),
        "setPhase" => $"{PhaseOwnerPrefix(e)}位相を{PhaseJp(e.Name)}にする",
        "invertPhase" => $"{PhaseOwnerPrefix(e)}位相を反転する",
        "augmentCards" => DescribeAugmentCards(e),
        // Phase 10.5.L1: relic 固有 action の自動文章化。formatter は battle 用と
        // 共有のため、relic action もここに併記する。
        "gainMaxHp" => $"最大HP+{AmountToken(e, context)}",
        "gainGold" => $"ゴールド+{AmountToken(e, context)}",
        "healHp" => $"{AmountToken(e, context)}HP回復",
        // Phase 10.6.B: Trigger=="Passive" の場合は DescribePassiveEffect (上で early-return) が
        // 「休息時の回復量 +N。」と書くので、こちらの legacy 文言は非 Passive 経路用。
        "restHealBonus" => $"休憩でのHP回復+{AmountToken(e, context)}",
        "healPercent" => $"HPを{AmountToken(e, context)}%回復",
        "extraEnergyOnFirstTurn" => $"戦闘1ターン目のみエナジー+{AmountToken(e, context)}",
        _ => $"(未対応 action: {e.Action})",
    };

    /// <summary>
    /// (Scope, Side) の組から日本語の対象表現プレフィックスを返す統一ヘルパ。
    /// 例: (Self, _) → "自身に"、(Single, Enemy) → "敵単体に"、(All, Ally) → "味方全体に"。
    /// 全 action (attack/block/buff/debuff/heal 等) で同じ法則を適用する。
    ///
    /// Phase 10.5.M6.4: 数字 marker 周辺のスペースを全削除する方針 (カード狭幅枠で
    /// テキストがはみ出す問題への対応)。視覚バランスは Client 側 .card-desc-num の
    /// 左右 margin で確保。
    /// </summary>
    private static string TargetPrefix(EffectScope scope, EffectSide? side) => (scope, side) switch
    {
        (EffectScope.Self, _) => "自身に",
        (EffectScope.Single, EffectSide.Enemy) => "敵単体に",
        (EffectScope.Single, EffectSide.Ally) => "味方単体に",
        // Random は「ランダムな (敵|味方) に」(M4: 単体 を除去)
        (EffectScope.Random, EffectSide.Enemy) => "ランダムな敵に",
        (EffectScope.Random, EffectSide.Ally) => "ランダムな味方に",
        (EffectScope.All, EffectSide.Enemy) => "敵全体に",
        (EffectScope.All, EffectSide.Ally) => "味方全体に",
        // side が null のフォールバック
        (EffectScope.Single, _) => "対象に",
        (EffectScope.Random, _) => "ランダムな対象に",
        (EffectScope.All, _) => "全体に",
        _ => "",
    };

    private static string DescribeAttack(CardEffect e, CardActorContext context)
        => $"{TargetPrefix(e.Scope, e.Side)}{AmountToken(e, context)}アタック";

    private static string DescribeBlock(CardEffect e, CardActorContext context)
        => $"{TargetPrefix(e.Scope, e.Side)}{AmountToken(e, context)}ブロック";

    private static string DescribeDraw(CardEffect e, CardActorContext context)
        => e.Scope == EffectScope.Self
            ? $"{AmountToken(e, context)}枚引く"
            : $"{ResourceSubjectPrefix(e)}{AmountToken(e, context)}枚引く";

    private static string ResourceOwnerPrefix(CardEffect e) => (e.Scope, e.Side) switch
    {
        (EffectScope.Self, _) => "",
        (EffectScope.Single, EffectSide.Ally) => "味方単体の",
        (EffectScope.Random, EffectSide.Ally) => "ランダムな味方の",
        (EffectScope.All, EffectSide.Ally) => "味方全体の",
        _ => "対象の",
    };

    private static string ResourceSubjectPrefix(CardEffect e) => (e.Scope, e.Side) switch
    {
        (EffectScope.Single, EffectSide.Ally) => "味方単体が",
        (EffectScope.Random, EffectSide.Ally) => "ランダムな味方が",
        (EffectScope.All, EffectSide.Ally) => "味方全体が",
        _ => "対象が",
    };

    /// <summary>
    /// exhaustCard / upgrade を Select (random/choose/all) + Pile (hand/draw/discard) の
    /// 組合せで翻訳する。共通フォーマット:
    ///   Select=all → "{zone}を全て{verb}"
    ///   Select=random → "{zone}を{amount}枚ランダムに{verb}"
    ///   Select=choose / null → "{zone}を{amount}枚選んで{verb}"
    /// </summary>
    private static string DescribeExhaustOrUpgrade(CardEffect e, CardActorContext context, bool isUpgrade)
    {
        var zone = ZoneJp(e.Pile);
        var verb = isUpgrade ? "強化" : "除外";
        return e.Select switch
        {
            "all" => $"{zone}を全て{verb}",
            "random" => $"{zone}を{AmountToken(e, context)}枚ランダムに{verb}",
            _ => $"{zone}を{AmountToken(e, context)}枚選んで{verb}",
        };
    }

    private static string DescribeHeal(CardEffect e, CardActorContext context)
        => $"{TargetPrefix(e.Scope, e.Side)}{AmountToken(e, context)}HP回復";

    private static string DescribeStatusChange(CardEffect e, CardActorContext context, bool isDebuff)
    {
        if (e.StatusChanges is { Count: > 0 })
        {
            var statuses = string.Join("・", e.StatusChanges.Select(change =>
                $"[S:{change.Name}][N:{change.Amount}]"));
            return $"{TargetPrefix(e.Scope, e.Side)}{statuses}を付与";
        }

        // Phase 10.5.M2: status 名を [S:id] marker として emit。Client 側で JP 名と
        //  ホバー定義 popup へ展開する (旧: bare JP 文字列、scan 困難だった)。
        var statusToken = string.IsNullOrEmpty(e.Name) ? "ステータス" : $"[S:{e.Name}]";
        return $"{TargetPrefix(e.Scope, e.Side)}{statusToken}{AmountToken(e, context)}を付与";
    }

    private static string DescribeDiscard(CardEffect e, CardActorContext context)
    {
        if (string.IsNullOrEmpty(e.Select))
        {
            // 旧仕様 (Select なし) はランダム扱いの簡略文言
            return $"手札{AmountToken(e, context)}枚を捨てる";
        }
        return e.Select switch
        {
            "choose" => $"手札を選んで{AmountToken(e, context)}枚捨てる",
            "random" => $"手札からランダムに{AmountToken(e, context)}枚捨てる",
            "all" => "手札を全て捨てる",
            _ => $"手札{AmountToken(e, context)}枚を捨てる",
        };
    }

    private static string DescribeAddCard(CardEffect e, CardActorContext context)
    {
        var cardRef = !string.IsNullOrEmpty(e.CardRefId) ? $"[C:{e.CardRefId}]" : "[C:?]";
        var zone = ZoneJp(e.Pile);
        return $"{cardRef}を{zone}に{AmountToken(e, context)}枚加える";
    }

    private static string DescribeRecoverFromDiscard(CardEffect e, CardActorContext context)
    {
        var selectJp = SelectJp(e.Select);
        var dest = ZoneJp(e.Pile);
        var amount = AmountToken(e, context);
        if (e.Select == "choose")
        {
            var source = $"捨札の選んだ{amount}枚";
            return e.Pile == "exhaust"
                ? $"{source}を除外する"
                : $"{source}を{dest}に戻す";
        }

        // exhaust の場合は「除外する」、それ以外は「<dest>に戻す」
        if (e.Pile == "exhaust")
        {
            return $"捨札から{selectJp}{amount}枚、除外する";
        }
        return $"捨札から{selectJp}{amount}枚、{dest}に戻す";
    }

    private static string DescribeRecoverFromExhaust(CardEffect e, CardActorContext context)
    {
        var selectJp = SelectJp(e.Select);
        var dest = ZoneJp(e.Pile);
        return $"除外から{selectJp}{AmountToken(e, context)}枚、{dest}に戻す";
    }

    /// <summary>位相の対象。自身は省略し、それ以外は「敵単体の」等。</summary>
    private static string PhaseOwnerPrefix(CardEffect e)
    {
        if (e.Scope == EffectScope.Self) return string.Empty;
        string target = TargetPrefix(e.Scope, e.Side);
        return target.EndsWith("に", System.StringComparison.Ordinal)
            ? target[..^1] + "の"
            : target;
    }

    private static string PhaseJp(string? name) => name switch
    {
        "calm" => "静",
        "motion" => "動",
        _ => "なし",
    };

    private static string DescribeAugmentCards(CardEffect e)
    {
        string keyword = $"[K:{e.Name ?? "?"}]";
        string amount = $"[N:{e.Amount}]";
        if (e.Pile == "self") return $"このカードに{keyword}を{amount}付与する";
        string zone = ZoneJp(e.Pile ?? "hand");
        string kind = string.IsNullOrEmpty(e.CardTypeFilter)
            || !System.Enum.TryParse<CardType>(e.CardTypeFilter, ignoreCase: true, out var type)
            ? "カード"
            : CardTypeJp(type);
        int count = e.CardCount ?? 1;
        return e.Select switch
        {
            "all" => $"{zone}の{kind}全てに{keyword}を{amount}付与する",
            "random" => $"{zone}のランダムな{kind}[N:{count}]枚に{keyword}を{amount}付与する",
            _ => $"{zone}の{kind}[N:{count}]枚を選び、{keyword}を{amount}付与する",
        };
    }

    private static string DescribeModifyCardCost(CardEffect e)
    {
        string zone = ZoneJp(e.Pile);
        int count = e.CardCount ?? 1;
        string targets = e.Select switch
        {
            "all" => $"{zone}の全カード",
            "random" => $"{zone}のランダムな[N:{count}]枚",
            _ => $"{zone}の選んだ[N:{count}]枚",
        };
        if (string.Equals(e.AmountSource, "currentCost", System.StringComparison.Ordinal))
        {
            string zeroDuration = e.CostDuration == "untilTurnEnd"
                ? "ターン中"
                : "使用されるまで";
            return $"{ResourceOwnerPrefix(e)}{targets}のコストを{zeroDuration}0にする";
        }
        long magnitude = System.Math.Abs((long)e.Amount);
        string sign = e.Amount < 0 ? "-" : "+";
        string duration = e.CostDuration == "untilTurnEnd"
            ? "ターン中"
            : "使用されるまで";
        return $"{ResourceOwnerPrefix(e)}{targets}のコストを{duration}{sign}[N:{magnitude}]";
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
            // 能力値参照は使用者の能力値が分かれば具体値（筋力・敏捷等の補正込み）で表示する。
            int? stat = e.AmountSource switch
            {
                "selfAttack" => context.AttackPower,
                "selfDefense" => context.DefensePower,
                _ => null,
            };
            var label = AmountSourceJp(e.AmountSource);
            if (e.AmountScalePercent is { } pct && pct != 100)
                label += $"×{FormatScale(pct)}";
            if (stat is { } statValue)
            {
                // [R:値|修飾:参照ラベル]。カード面は値（上振れ/下振れ色付き）、
                // ポップアップでは「X(Xは参照ラベル)」と表示し、固定値カードと見分けられるようにする。
                var resolved = e with
                {
                    Amount = Battle.Engine.AmountSourceEvaluator.Scale(statValue, e.AmountScalePercent),
                    AmountSource = null,
                    AmountScalePercent = null,
                };
                int adjustedStat = AdjustAmount(resolved, context);
                string statModifier = adjustedStat > resolved.Amount
                    ? "up"
                    : adjustedStat < resolved.Amount ? "down" : "";
                return $"[R:{adjustedStat}|{statModifier}:{label}]";
            }
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

    private static string FormatScale(int pct) =>
        (pct / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static string AmountSourceJp(string src) => src switch
    {
        "handCount" => "手札の数",
        "drawPileCount" => "山札の数",
        "discardPileCount" => "捨札の数",
        "exhaustPileCount" => "除外の数",
        "selfHp" => "自身のHP",
        "selfHpLost" => "自身の失ったHP",
        "selfBlock" => "自身のブロック",
        "comboCount" => "現在のコンボ",
        "maxComboThisTurn" => "このターンの最大コンボ",
        "energy" => "現在のエナジー",
        "powerCardCount" => "パワーカード枚数",
        "selfAttack" => "自身の攻撃力",
        "selfDefense" => "自身の守備力",
        "currentCost" => "対象カードの現在コスト",
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

    private static string JpStatusName(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "ステータス";
        return BattleStatusTextCatalog.All.TryGetValue(id, out var text)
            ? text.Name
            : id;
    }

    /// <summary>
    /// Phase 10.6.B: Passive trigger 用の専用文言テーブル。
    /// trigger プレフィックスなしでプレイヤー視点の意味のある表記を生成する。
    /// 正の値には "+" プレフィックス、負の値には "-" プレフィックスを付け、絶対値を [N:abs] で表示。
    /// </summary>
    private static string DescribePassiveEffect(CardEffect eff)
    {
        int amount = eff.Amount;
        string sign = amount >= 0 ? "+" : "-";
        int abs = System.Math.Abs(amount);
        string n = $"[N:{abs}]";

        return eff.Action switch
        {
            "energyPerTurnBonus"          => $"ターン開始時のエナジー回復量{sign}{n}。",
            "cardsDrawnPerTurnBonus"      => $"ターン開始時の手札枚数 {sign}{n}",
            "goldRewardMultiplier"        => $"[T:OnBattleEnd]のゴールド報酬 {sign}{n}%。",
            "shopPriceMultiplier"         => $"ショップ価格 {sign}{n}%",
            "rewardCardChoicesBonus"      => $"カード報酬選択肢{sign}{n}。",
            "rewardRerollAvailable"       => $"カード報酬のリロール回数{sign}{n}。",
            "unknownTileWeightDelta"      => $"ハテナマスの{UnknownTileLabel(eff.Name)}出現率 {sign}{n}",
            "restHealBonus"               => $"[T:OnRest]の回復量 {sign}{n}。",
            _                             => $"(未対応 Passive action: {eff.Action})",
        };
    }

    private static string UnknownTileLabel(string? name) => name switch
    {
        "enemy"    => "敵戦闘",
        "elite"    => "エリート戦闘",
        "merchant" => "ショップ",
        "rest"     => "休憩所",
        "treasure" => "宝箱",
        "event"    => "イベント",
        _          => "(タイル未指定)",
    };
}
