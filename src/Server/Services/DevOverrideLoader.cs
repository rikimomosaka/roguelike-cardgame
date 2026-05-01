using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// 開発者ローカル override JSON ローダー (Phase 10.5.H)。
///
/// `data-local/dev-overrides/cards/*.json` を読んで id → JSON 文字列の辞書を返す純 I/O レイヤー。
/// マージ自体は Core 側 CardOverrideMerger が行うので、本クラスは disk アクセスのみ。
/// 本番では呼ばれない (Program.cs で env.IsDevelopment() ガード)。
/// </summary>
public static class DevOverrideLoader
{
    /// <summary>
    /// <paramref name="overrideRoot"/> 直下の <c>cards/</c> から *.json を全て読み、id をキーに dict を返す。
    /// dir が無い／読めないファイルは静かに skip。
    /// </summary>
    public static IReadOnlyDictionary<string, string> LoadCards(string overrideRoot)
    {
        var result = new Dictionary<string, string>();
        var cardsDir = Path.Combine(overrideRoot, "cards");
        if (!Directory.Exists(cardsDir)) return result;

        foreach (var path in Directory.EnumerateFiles(cardsDir, "*.json"))
        {
            string json;
            try { json = File.ReadAllText(path); }
            catch { continue; }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) continue;
                if (!doc.RootElement.TryGetProperty("id", out var idEl)) continue;
                if (idEl.ValueKind != JsonValueKind.String) continue;
                var id = idEl.GetString();
                if (string.IsNullOrEmpty(id)) continue;
                result[id] = json;
            }
            catch (JsonException) { continue; }
        }
        return result;
    }
}
