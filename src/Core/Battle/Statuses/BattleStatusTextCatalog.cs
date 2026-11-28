using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>
/// Canonical Japanese display text for status authoring and diagnostics.
/// The client mirrors this catalog because Core intentionally has no UI dependency.
/// </summary>
public sealed record BattleStatusText(string Name, string Description);

public static class BattleStatusTextCatalog
{
    public static IReadOnlyDictionary<string, BattleStatusText> All { get; } =
        new Dictionary<string, BattleStatusText>(System.StringComparer.Ordinal)
        {
            [BattleStatusIds.Strength] = new("筋力", "アタックで与えるダメージが[N:X]増加する。"),
            [BattleStatusIds.Dexterity] = new("敏捷", "得るブロックが[N:X]増加する。"),
            [BattleStatusIds.Omnistrike] = new("拡散", "全ての攻撃を全体攻撃に変更する。ターン終了時に[S:omnistrike]が1減少する。"),
            [BattleStatusIds.Barrier] = new("バリア", "アタックによってHPが1以上減る場合、そのダメージを無効化し、[S:barrier]が1減少する。"),
            [BattleStatusIds.Lifesteal] = new("吸血", "アタックによって減らしたHPの[N:X]%を回復する。"),
            [BattleStatusIds.Reflect] = new("反射", "アタックを受けた時、攻撃元にアタックの回数分だけ[N:X]ダメージを返す(この際ブロックを無視する)。"),
            [BattleStatusIds.HandCostReduction] = new("節制", "手札のカードの使用コストを[N:X]軽減する。カードの使用時に[S:handCostReduction]が1減少する。"),
            [BattleStatusIds.Weak] = new("脱力", "アタックで与えるダメージが0.75倍になる。ターン終了時に[S:weak]が1減少する。"),
            [BattleStatusIds.Vulnerable] = new("脆弱", "アタックで受けるダメージが1.5倍になる。ターン終了時に[S:vulnerable]が1減少する。"),
            [BattleStatusIds.Poison] = new("毒", "ターン開始時に[N:X]ダメージを受け、[S:poison]が1減少する。"),
            [BattleStatusIds.HandCostIncrease] = new("浪費", "手札のカードの使用コストを[N:X]増加する。カードの使用時に[S:handCostIncrease]が1減少する。"),
            [BattleStatusIds.Vigor] = new("元気", "アタックで与えるダメージが1.5倍になる。ターン終了時に[S:vigor]が1減少する。"),
            [BattleStatusIds.Pursuit] = new("追撃", "攻撃直前に、「敵全体に[N:X]アタック。」を得て、[S:pursuit]が1減少する。"),
            [BattleStatusIds.Regeneration] = new("再生", "ターン開始時にHPを[N:X]回復し、[S:regeneration]が1減少する。"),
            [BattleStatusIds.Fortify] = new("堅牢", "ブロックを最大[N:X]まで次のターンに持ち越す。"),
            [BattleStatusIds.Resistance] = new("耐性", "デバフを無効化し、[S:resistance]が1減少する。"),
            [BattleStatusIds.Revive] = new("復活", "死亡時に1度だけHPを[N:X]回復し、[S:revive]が消滅する。"),
            [BattleStatusIds.Growth] = new("成長", "ターン開始時に[S:strength][N:X]を得る。"),
            [BattleStatusIds.Insight] = new("洞察", "ターン開始時に[N:X]枚追加で引き、[S:insight]が消滅する。"),
            [BattleStatusIds.Haste] = new("加速", "ターン開始時のエナジー回復量を+[N:X]し、[S:haste]が消滅する。"),
            [BattleStatusIds.Continuity] = new("継続", "コンボ条件を満たさないカードを使った時、コンボを継続し、[S:continuity]が1減少する。"),
            [BattleStatusIds.Mastery] = new("熟練", "ターン開始時にコンボゲージを[N:X]得て、[S:mastery]が1減少する。"),
            [BattleStatusIds.Afterimage] = new("残光", "自身の攻撃終了時、このターン最後の攻撃を[N:X]回実行し、[S:afterimage]が消滅する。"),
            [BattleStatusIds.Steadfast] = new("堅持", "ターン終了時に消滅するブロックの[N:50]%を次のターンに持ち越す。ターン終了時に[S:steadfast]が1減少する。"),
            [BattleStatusIds.Cover] = new("護衛", "アタックによって味方が受けるダメージを、自身のブロックで可能な限り防ぐ。ターン終了時に[S:cover]が1減少する。"),
            [BattleStatusIds.DefensiveStance] = new("守勢", "自身のブロックを使って敵のアタックを完全に防いだ時、コンボゲージを[N:1]得る。ターン終了時に[S:defensiveStance]が1減少する。"),
            [BattleStatusIds.ComboCarryover] = new("連環", "ターン終了時、現在コンボを次のターンへ持ち越し、[S:comboCarryover]が1減少する。次のターンに最初に使用するカードは条件を問わずコンボを継続するが、コンボによるコスト軽減を受けない。"),
            [BattleStatusIds.Afterglow] = new("余韻", "ターン終了時に減少または消滅する自身の他のバフが[N:1]つ以上ある時、それらを全て維持し、[S:afterglow]が消滅する。"),
            [BattleStatusIds.Resonance] = new("共鳴", "自身のみに[S:resonance]以外のバフが付与された時、付与された全てのバフを他の全ての味方に付与し、[S:resonance]が1減少する。"),
            [BattleStatusIds.VirulentPoison] = new("猛毒", "[S:poison]によって受けるダメージが[N:X]倍になる。"),
            [BattleStatusIds.Stagnation] = new("停滞", "コンボゲージが増えなくなる。ターン終了時に[S:stagnation]が1減少する。"),
            [BattleStatusIds.Burn] = new("火傷", "ターン開始時に現在HPの[N:X]%のダメージを受け、[S:burn]が消滅する。"),
            [BattleStatusIds.Scorch] = new("灼痕", "[S:burn]が消滅する時、代わりに[S:scorch]が1減少する。"),
            [BattleStatusIds.Confusion] = new("混乱", "カードが手札へ入った時、その使用コストを0～3のランダムな数値に変更する。ターン終了時に[S:confusion]が1減少する。"),
            [BattleStatusIds.Curse] = new("呪縛", "カードを使用した時に[S:curse]が1減少する。[S:curse]が消滅したターンはカードを使用できなくなる。"),
            [BattleStatusIds.Bind] = new("束縛", "アタックカードを使用できない。ターン終了時に[S:bind]が1減少する。"),
            [BattleStatusIds.Silence] = new("沈黙", "アタック以外のカードを使用できない。ターン終了時に[S:silence]が1減少する。"),
            [BattleStatusIds.Seal] = new("封印", "戦技・奥義を使用できない。ターン終了時に[S:seal]が1減少する。"),
            [BattleStatusIds.Exhaustion] = new("消耗", "ターン開始時にエナジーを[N:X]失い、[S:exhaustion]が1減少する。"),
            [BattleStatusIds.Distress] = new("動揺", "カードを使用することでコンボが途切れた時、エナジーとコンボゲージを0にする。ターン終了時に[S:distress]が1減少する。"),
            [BattleStatusIds.Disruption] = new("攪乱", "自身の全てのアタックをランダム攻撃に変更し、全ての行動がX％の確率で失敗する(Xは攪乱の数×10、最大50％)。ターン終了時に[S:disruption]が1減少する。"),
        };

    public static BattleStatusText Get(string id) => All[id];
}
