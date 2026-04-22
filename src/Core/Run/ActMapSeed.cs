namespace RoguelikeCardGame.Core.Run;

public static class ActMapSeed
{
    public static ulong Derive(ulong runSeed, int act)
        => unchecked(runSeed * 2654435761UL + (ulong)act);
}
