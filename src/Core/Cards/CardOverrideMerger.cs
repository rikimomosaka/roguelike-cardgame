using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// 開発者ローカル override JSON を base カード JSON にマージする純関数。
/// マージ規則:
///   - versions は union (override 優先で同 version 識別子重複時は override 採用)
///   - override.activeVersion が指定されていれば base.activeVersion を上書き
///   - id mismatch なら CardJsonException を送出
///   - override に id 等のメタが無くても base 側を使う
/// 入出力は JSON 文字列。Core 層内なので file I/O は触れない。
/// Phase 10.5.H 設計書 §4 参照。
/// </summary>
public static class CardOverrideMerger
{
    public static string Merge(string baseJson, string overrideJson)
    {
        var baseNode = JsonNode.Parse(baseJson)?.AsObject()
            ?? throw new CardJsonException("base JSON は object でなければなりません。");
        var overrideNode = JsonNode.Parse(overrideJson)?.AsObject()
            ?? throw new CardJsonException("override JSON は object でなければなりません。");

        var baseId = baseNode["id"]?.GetValue<string>();
        var overrideId = overrideNode["id"]?.GetValue<string>();
        if (overrideId is not null && baseId is not null && baseId != overrideId)
            throw new CardJsonException(
                $"override id '{overrideId}' は base id '{baseId}' と一致しません。");

        var baseVersions = baseNode["versions"] as JsonArray ?? new JsonArray();
        var overrideVersions = overrideNode["versions"] as JsonArray ?? new JsonArray();

        // override に含まれる version 識別子集合を先に作る (base 側 skip 判定用)。
        var overrideIds = new HashSet<string>();
        foreach (var v in overrideVersions)
        {
            if (v is null) continue;
            var verId = v["version"]?.GetValue<string>();
            if (verId is not null) overrideIds.Add(verId);
        }

        var merged = new JsonArray();

        // base version: override に同 id があれば skip。
        foreach (var v in baseVersions)
        {
            if (v is null) continue;
            var verId = v["version"]?.GetValue<string>();
            if (verId is not null && overrideIds.Contains(verId)) continue;
            merged.Add(v.DeepClone());
        }
        // override version: 全部追加。
        foreach (var v in overrideVersions)
        {
            if (v is null) continue;
            merged.Add(v.DeepClone());
        }

        baseNode["versions"] = merged;

        // override.activeVersion が指定されていれば上書き。
        var overrideActive = overrideNode["activeVersion"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(overrideActive))
        {
            baseNode["activeVersion"] = overrideActive;
        }

        return baseNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
