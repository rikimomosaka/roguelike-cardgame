using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// 開発者ローカル override / base / backup ファイルの disk I/O を集約するヘルパ (Phase 10.5.J)。
///
/// パス構成:
///   - overrideRoot: <c>data-local/dev-overrides</c> 直下に <c>{subDir}/{id}.json</c> を読み書き
///   - baseDir:      <c>src/Core/Data/{SubDir-Capitalized}/</c> (promote 用、null なら promote 系は使えない)
///   - backupRoot:   <c>data-local/backups</c> (Promote 時に旧 base を <c>{subDir}/{id}-{ts}.json</c> に退避)
///
/// テスト時は overrideRoot のみ渡せば override 系のみテスト可能。
/// 本クラスは Server 層 (System.IO 利用) のため Core からは参照しない。
///
/// Phase 10.5.L1: <paramref name="subDir"/> 引数化で relic 等の他資産にも転用可能。
/// 既存呼び出し互換のため default は "cards"。relic 用は別 instance を <c>subDir: "relics"</c> で作る。
/// </summary>
public sealed class DevCardWriter
{
    private readonly string _overrideRoot;
    private readonly string? _baseDir;
    private readonly string? _backupRoot;
    private readonly string _subDir;

    public DevCardWriter(
        string overrideRoot,
        string? baseDir = null,
        string? backupRoot = null,
        string subDir = "cards")
    {
        _overrideRoot = overrideRoot;
        _baseDir = baseDir;
        _backupRoot = backupRoot;
        _subDir = subDir;
    }

    /// <summary>override JSON を <c>{overrideRoot}/{subDir}/{id}.json</c> に書き込む。</summary>
    public void WriteOverride(string id, string json)
    {
        var dir = Path.Combine(_overrideRoot, _subDir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{id}.json");
        WriteAtomic(path, json);
    }

    /// <summary>override JSON を読む。存在しなければ null。</summary>
    public string? ReadOverride(string id)
    {
        var path = Path.Combine(_overrideRoot, _subDir, $"{id}.json");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>override file を削除。存在しなくても例外は投げない。</summary>
    public void DeleteOverride(string id)
    {
        var path = Path.Combine(_overrideRoot, _subDir, $"{id}.json");
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// base JSON を上書き。既存があれば backup を取る。
    /// baseDir が未設定なら InvalidOperationException。
    /// </summary>
    public void WriteBaseWithBackup(string id, string json)
    {
        if (_baseDir is null)
            throw new InvalidOperationException("baseDir not configured.");

        var basePath = ResolveBasePath(id) ?? GetNewBasePath(id);
        if (_backupRoot is not null && File.Exists(basePath))
        {
            var backupDir = Path.Combine(_backupRoot, _subDir);
            Directory.CreateDirectory(backupDir);
            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var safeBaseName = Path.GetFileNameWithoutExtension(basePath);
            var backupPath = Path.Combine(
                backupDir,
                $"{safeBaseName}-{ts}-{Guid.NewGuid():N}.json");
            File.Copy(basePath, backupPath, overwrite: false);
        }
        Directory.CreateDirectory(_baseDir);
        WriteAtomic(basePath, json);
    }

    /// <summary>base JSON を読む。baseDir 未設定や file 不在なら null。</summary>
    public string? ReadBase(string id)
    {
        if (_baseDir is null) return null;
        var path = ResolveBasePath(id);
        if (path is null) return null;
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// base JSON file を削除。削除前に backup を取る (Phase 10.5.M)。
    /// baseDir 未設定なら InvalidOperationException。file 不在なら何もしない。
    /// </summary>
    public void DeleteBaseWithBackup(string id)
    {
        if (_baseDir is null)
            throw new InvalidOperationException("baseDir not configured.");

        var basePath = ResolveBasePath(id);
        if (basePath is null) return;

        if (_backupRoot is not null)
        {
            var backupDir = Path.Combine(_backupRoot, _subDir);
            Directory.CreateDirectory(backupDir);
            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            var safeBaseName = Path.GetFileNameWithoutExtension(basePath);
            var backupPath = Path.Combine(
                backupDir,
                $"{safeBaseName}-deleted-{ts}-{Guid.NewGuid():N}.json");
            File.Copy(basePath, backupPath, overwrite: false);
        }
        File.Delete(basePath);
    }

    /// <summary>
    /// base directory 直下の JSON を root id で検索する。
    /// ファイル名は編集履歴上の連番名などを許容し、同一 id が複数ある場合は
    /// 曖昧なファイルを更新・削除しないよう例外にする。
    /// </summary>
    private string? ResolveBasePath(string id)
    {
        EnsureSafeId(id);
        if (_baseDir is null || !Directory.Exists(_baseDir)) return null;

        var matches = new List<string>();
        foreach (var path in Directory.EnumerateFiles(_baseDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("id", out var idElement)
                    && idElement.ValueKind == JsonValueKind.String
                    && string.Equals(idElement.GetString(), id, StringComparison.Ordinal))
                {
                    matches.Add(path);
                }
            }
            catch (JsonException)
            {
                // 対象 id を宣言できない不正 JSON は、この検索では一致しない。
                // 内容自体の検証とエラー表示は既存の authoring validation が担う。
            }
        }

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Multiple base JSON files declare id '{id}': "
                + string.Join(", ", matches.Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal)))
        };
    }

    private string GetNewBasePath(string id)
    {
        EnsureSafeId(id);
        var path = Path.Combine(_baseDir!, $"{id}.json");
        if (File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Cannot create base id '{id}' because '{Path.GetFileName(path)}' already declares a different id.");
        }

        return path;
    }

    private static void EnsureSafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)
            || Path.IsPathRooted(id)
            || id is "." or ".."
            || id.Contains('/')
            || id.Contains('\\')
            || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("id must be a safe file name segment.", nameof(id));
        }
    }

    /// <summary>
    /// override directory に存在する ID 一覧 (Phase 10.5.K — override-only 新規エントリを GET で
    /// 列挙するため)。directory 不在時は空配列。
    /// </summary>
    public IReadOnlyList<string> ListOverrideIds()
    {
        var dir = Path.Combine(_overrideRoot, _subDir);
        if (!Directory.Exists(dir)) return System.Array.Empty<string>();
        var files = Directory.GetFiles(dir, "*.json");
        var ids = new System.Collections.Generic.List<string>(files.Length);
        foreach (var f in files)
        {
            ids.Add(Path.GetFileNameWithoutExtension(f));
        }
        return ids;
    }

    private static void WriteAtomic(string path, string json)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("A destination directory is required.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}

/// <summary>
/// Phase 10.5.L1: relic 用 writer (DevCardWriter の subDir="relics" instance)。
/// 単に DI 上で別 type として持ちたいだけなので、内部で DevCardWriter を委譲する薄ラッパ。
/// </summary>
public sealed class DevRelicWriter
{
    private readonly DevCardWriter _inner;

    public DevRelicWriter(string overrideRoot, string? baseDir = null, string? backupRoot = null)
    {
        _inner = new DevCardWriter(overrideRoot, baseDir, backupRoot, subDir: "relics");
    }

    public void WriteOverride(string id, string json) => _inner.WriteOverride(id, json);
    public string? ReadOverride(string id) => _inner.ReadOverride(id);
    public void DeleteOverride(string id) => _inner.DeleteOverride(id);
    public void WriteBaseWithBackup(string id, string json) => _inner.WriteBaseWithBackup(id, json);
    public string? ReadBase(string id) => _inner.ReadBase(id);
    public void DeleteBaseWithBackup(string id) => _inner.DeleteBaseWithBackup(id);
    public IReadOnlyList<string> ListOverrideIds() => _inner.ListOverrideIds();
}
