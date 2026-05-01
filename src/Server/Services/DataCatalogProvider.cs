using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// DataCatalog をミュータブルに保持し、override 変更時に rebuild できる Provider (Phase 10.5.J)。
///
/// 既存の DataCatalog 直 inject 先には Program.cs で Transient 経由 (Provider.Current) で
/// 差し込まれるので、controller 側は無修正のまま常に最新 catalog を見る。
/// 本クラスは Server プロセス内で 1 個 (Singleton) として保持される。
/// </summary>
public sealed class DataCatalogProvider
{
    private readonly IWebHostEnvironment _env;
    private DataCatalog _current;

    public DataCatalogProvider(IWebHostEnvironment env)
    {
        _env = env;
        _current = BuildCatalog();
    }

    /// <summary>現在の DataCatalog instance。Rebuild 後は新 instance に差し替わる。</summary>
    public DataCatalog Current => _current;

    /// <summary>override fileの変更後に呼ぶと catalog を再構築する。</summary>
    public void Rebuild()
    {
        _current = BuildCatalog();
    }

    private DataCatalog BuildCatalog()
    {
        if (!_env.IsDevelopment())
            return EmbeddedDataLoader.LoadCatalog();

        var overrideRoot = Path.Combine(_env.ContentRootPath, "..", "..", "data-local", "dev-overrides");
        var overrides = DevOverrideLoader.LoadCards(overrideRoot);
        return overrides.Count == 0
            ? EmbeddedDataLoader.LoadCatalog()
            : EmbeddedDataLoader.LoadCatalogWithOverrides(overrides);
    }
}
