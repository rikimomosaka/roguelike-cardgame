using System;

namespace RoguelikeCardGame.Server.Abstractions;

/// <summary>アカウントメタデータ（ID と作成日時）。</summary>
/// <remarks>Server 専用。VR 移植時は PlayerData の読み出し結果に置き換える。</remarks>
public sealed record Account(string Id, DateTimeOffset CreatedAt);
