namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// カード／敵 Move／召喚 Move／レリック／ポーション 共通の効果プリミティブ。
/// Phase 10 設計書 (2026-04-25-phase10-battle-system-design.md) 第 2-1 章参照。
/// </summary>
/// <param name="Action">"attack"|"block"|"buff"|"debuff"|"summon"|"heal"|"draw"|"discard"|"upgrade"|"exhaustCard"|"exhaustSelf"|"retainSelf"|"gainEnergy" など</param>
/// <param name="Scope">対象スコープ</param>
/// <param name="Side">対象側（行動主体からの相対視点）。Self では null</param>
/// <param name="Amount">効果量</param>
/// <param name="Name">buff/debuff の種類名 ("strength"|"vulnerable" 等)</param>
/// <param name="UnitId">summon 用：召喚キャラ ID</param>
/// <param name="ComboMin">コンボ N 以上で適用（カードのみ意味あり）</param>
/// <param name="Pile">"hand"|"discard"|"draw" (exhaustCard / upgrade / discard 用)</param>
/// <param name="BattleOnly">true なら戦闘外発動時にスキップ</param>
public sealed record CardEffect(
    string Action,
    EffectScope Scope,
    EffectSide? Side,
    int Amount,
    string? Name = null,
    string? UnitId = null,
    int? ComboMin = null,
    string? Pile = null,
    bool BattleOnly = false)
{
    /// <summary>
    /// JSON ロード時の safety net 正規化。
    /// - Scope=Self なら Side を破棄（null に）
    /// - Action=="attack" なら Side を Enemy に強制
    /// </summary>
    public CardEffect Normalize()
    {
        var side = Side;
        if (Scope == EffectScope.Self) side = null;
        if (Action == "attack") side = EffectSide.Enemy;
        return this with { Side = side };
    }
}
