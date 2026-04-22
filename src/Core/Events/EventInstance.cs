using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Events;

/// <summary>RunState に保持される「現在解決中のイベント」スナップショット。</summary>
public sealed record EventInstance(
    string EventId,
    ImmutableArray<EventChoice> Choices,
    int? ChosenIndex = null);
