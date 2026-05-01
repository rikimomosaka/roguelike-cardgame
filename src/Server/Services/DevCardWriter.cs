using System;
using System.Collections.Generic;
using System.IO;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// 開発者ローカル override / base / backup ファイルの disk I/O を集約するヘルパ (Phase 10.5.J)。
///
/// パス構成:
///   - overrideRoot: <c>data-local/dev-overrides</c> 直下に <c>cards/{id}.json</c> を読み書き
///   - baseCardsDir: <c>src/Core/Data/Cards/</c> (promote 用、null なら promote 系は使えない)
///   - backupRoot:   <c>data-local/backups</c> (Promote 時に旧 base を <c>cards/{id}-{ts}.json</c> に退避)
///
/// テスト時は overrideRoot のみ渡せば override 系のみテスト可能。
/// 本クラスは Server 層 (System.IO 利用) のため Core からは参照しない。
/// </summary>
public sealed class DevCardWriter
{
    private readonly string _overrideRoot;
    private readonly string? _baseCardsDir;
    private readonly string? _backupRoot;

    public DevCardWriter(string overrideRoot, string? baseCardsDir = null, string? backupRoot = null)
    {
        _overrideRoot = overrideRoot;
        _baseCardsDir = baseCardsDir;
        _backupRoot = backupRoot;
    }

    /// <summary>override JSON を <c>{overrideRoot}/cards/{cardId}.json</c> に書き込む。</summary>
    public void WriteOverride(string cardId, string json)
    {
        var dir = Path.Combine(_overrideRoot, "cards");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{cardId}.json");
        File.WriteAllText(path, json);
    }

    /// <summary>override JSON を読む。存在しなければ null。</summary>
    public string? ReadOverride(string cardId)
    {
        var path = Path.Combine(_overrideRoot, "cards", $"{cardId}.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>override file を削除。存在しなくても例外は投げない。</summary>
    public void DeleteOverride(string cardId)
    {
        var path = Path.Combine(_overrideRoot, "cards", $"{cardId}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// base カード JSON を上書き。既存があれば backup を取る。
    /// baseCardsDir が未設定なら InvalidOperationException。
    /// </summary>
    public void WriteBaseWithBackup(string cardId, string json)
    {
        if (_baseCardsDir is null)
            throw new InvalidOperationException("baseCardsDir not configured.");

        var basePath = Path.Combine(_baseCardsDir, $"{cardId}.json");
        if (_backupRoot is not null && File.Exists(basePath))
        {
            var backupDir = Path.Combine(_backupRoot, "cards");
            Directory.CreateDirectory(backupDir);
            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var backupPath = Path.Combine(backupDir, $"{cardId}-{ts}.json");
            File.Copy(basePath, backupPath, overwrite: false);
        }
        Directory.CreateDirectory(_baseCardsDir);
        File.WriteAllText(basePath, json);
    }

    /// <summary>base カード JSON を読む。baseCardsDir 未設定や file 不在なら null。</summary>
    public string? ReadBase(string cardId)
    {
        if (_baseCardsDir is null) return null;
        var path = Path.Combine(_baseCardsDir, $"{cardId}.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// override directory に存在するカード ID 一覧 (Phase 10.5.K — override-only 新規カードを GET で
    /// 列挙するため)。directory 不在時は空配列。
    /// </summary>
    public IReadOnlyList<string> ListOverrideIds()
    {
        var dir = Path.Combine(_overrideRoot, "cards");
        if (!Directory.Exists(dir)) return System.Array.Empty<string>();
        var files = Directory.GetFiles(dir, "*.json");
        var ids = new System.Collections.Generic.List<string>(files.Length);
        foreach (var f in files)
        {
            ids.Add(Path.GetFileNameWithoutExtension(f));
        }
        return ids;
    }
}
