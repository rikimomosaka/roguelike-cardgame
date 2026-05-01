namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// CardTextFormatter が context-aware に [N:N|up/down] マーカーを出すために
/// 受け取る、actor のスタタス snapshot。Phase 10.5.C で導入。
///
/// battle 中の hero (caster) の statuses (strength/weak/dexterity) を渡し、
/// formatter が attack / block の amount を adjust した結果を base と比較して
/// 上振れなら |up、下振れなら |down のマーカーを emit する。
/// </summary>
/// <param name="Strength">attack に加算される筋力</param>
/// <param name="Weak"><![CDATA[>0 なら attack に 0.75 倍 (floor)]]></param>
/// <param name="Dexterity">block に加算される敏捷</param>
public sealed record CardActorContext(
    int Strength,
    int Weak,
    int Dexterity)
{
    /// <summary>
    /// context 不明時のデフォルト。catalog 表示や既存 Format(def, upgraded) API の
    /// 内部委譲先として使う。すべてのフィールドが 0 なので AdjustAmount は
    /// base と同値を返し、結果として無修飾 [N:N] のみが emit される。
    /// </summary>
    public static readonly CardActorContext Empty = new(0, 0, 0);
}
