using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoguelikeCardGame.Core.Run;

/// <summary>RunState JSON のパース失敗を表す例外。</summary>
public sealed class RunStateSerializerException : Exception
{
    public RunStateSerializerException(string message) : base(message) { }
    public RunStateSerializerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>RunState ⇔ JSON 文字列の変換。ファイル I/O は Server 側の SaveRepository が担当。</summary>
public static class RunStateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(RunState state)
    {
        return JsonSerializer.Serialize(state, Options);
    }

    public static RunState Deserialize(string json)
    {
        RunState? state;
        try
        {
            state = JsonSerializer.Deserialize<RunState>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex);
        }

        if (state is null)
            throw new RunStateSerializerException("RunState JSON が null として解釈されました。");

        if (state.SchemaVersion != RunState.CurrentSchemaVersion)
            throw new RunStateSerializerException(
                $"未対応の schemaVersion: {state.SchemaVersion} (対応: {RunState.CurrentSchemaVersion})");

        return state;
    }
}
