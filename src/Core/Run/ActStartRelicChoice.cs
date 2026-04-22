using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Run;

public sealed record ActStartRelicChoice(ImmutableArray<string> RelicIds);
