using System;
using System.IO;
using System.Text;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Server.Services;

/// <summary>アカウント ID ごとに単一スロットのラン状態をファイルに保存／読込する。</summary>
public sealed class SaveRepository
{
    private readonly string _rootDir;

    public SaveRepository(string rootDir)
    {
        if (string.IsNullOrWhiteSpace(rootDir))
            throw new ArgumentException("rootDir は空にできません。", nameof(rootDir));
        _rootDir = rootDir;
        Directory.CreateDirectory(_rootDir);
    }

    public void Save(string accountId, RunState state)
    {
        ValidateAccountId(accountId);
        var json = RunStateSerializer.Serialize(state);
        File.WriteAllText(PathFor(accountId), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public bool TryLoad(string accountId, out RunState? state)
    {
        ValidateAccountId(accountId);
        var path = PathFor(accountId);
        if (!File.Exists(path))
        {
            state = null;
            return false;
        }
        var json = File.ReadAllText(path, Encoding.UTF8);
        state = RunStateSerializer.Deserialize(json);
        return true;
    }

    private string PathFor(string accountId) =>
        Path.Combine(_rootDir, accountId + ".json");

    private static void ValidateAccountId(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("accountId は空にできません。", nameof(accountId));
        if (accountId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException(
                $"accountId にファイル名として使えない文字が含まれています: {accountId}", nameof(accountId));
    }
}
