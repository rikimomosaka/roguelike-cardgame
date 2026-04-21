// src/Core/Json/JsonOptions.cs
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoguelikeCardGame.Core.Json;

/// <summary>
/// Core 層で共有する <see cref="JsonSerializerOptions"/>。
/// </summary>
/// <remarks>
/// VRChat (Udon#) 移植時は System.Text.Json が使えないため、この静的プロパティごと削除し、
/// 各シリアライザが手書きの JSON 変換に置き換わる想定。
/// </remarks>
public static class JsonOptions
{
    public static JsonSerializerOptions Default { get; } = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }
}
