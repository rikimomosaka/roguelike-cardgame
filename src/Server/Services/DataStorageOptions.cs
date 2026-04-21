// src/Server/Services/DataStorageOptions.cs
namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> の
/// <c>DataStorage</c> セクションにバインドされる設定 POCO。
/// </summary>
public sealed class DataStorageOptions
{
    public const string SectionName = "DataStorage";

    /// <summary>ファイル代理 DB のルートディレクトリ（相対 or 絶対パス）。</summary>
    public string RootDirectory { get; set; } = "./data-local";
}
