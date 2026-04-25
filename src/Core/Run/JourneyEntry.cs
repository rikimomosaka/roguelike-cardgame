using RoguelikeCardGame.Core.Map;

namespace RoguelikeCardGame.Core.Run;

/// <summary>1 マス踏破の記録。複数アクトに渡る走行履歴を保持するために <see cref="RunState.JourneyLog"/> へ追記される。</summary>
public sealed record JourneyEntry(int Act, int NodeId, TileKind Kind, TileKind? ResolvedKind);
