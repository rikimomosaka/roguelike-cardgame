# 現行コンボゲージ・キャラクタースキル仕様

> 更新日: 2026-08-10  
> 本文書は Phase 10 時点の旧設計書より優先する。

## 状態

| フィールド | 意味 |
|---|---|
| `ComboGauge` | キャラクタースキル用ゲージ。戦闘開始時0、範囲0～10、ターンを跨いで保持 |
| `UsedCharacterSkillIdsThisTurn` | 「ターン1回」属性を持ち、このターン中に使用済みのキャラクタースキルID集合 |

`ComboGauge`のゲーム上限は10。上限到達後の獲得分は切り捨てる。
Core のカード使用・キャラクタースキル入口では、`0 <= ComboGauge <= 10`、
`0 <= ComboCount <= MaxComboThisTurn`を不変条件として検証し、壊れたstateは補正せず拒否する。

## カード使用によるゲージ獲得

1. カード使用で更新後の`ComboCount`を求める。連続加算が`int.MaxValue`を超える場合はそこで飽和する。
2. `newMax = max(MaxComboThisTurn, newComboCount)`を求める。
3. `newMax - oldMax`が正の場合だけ、その実差分を`ComboGauge`へ加える。
4. `ComboGauge`は10で飽和し、`ComboGaugeChanged`の`Amount`には飽和後の実増加量を入れる。

通常進行では、そのターンの最大コンボ記録を1更新するたびにゲージを1得る。コンボが切れた後、同じターンの過去最大値まで戻しただけでは得られず、過去最大値を超えた分から再び得る。`MaxComboThisTurn`はターンごとに0へ戻るため、ゲージ獲得判定も次ターンに新しく始まる。一方、`ComboGauge`自体はリセットしない。

選択待ち効果を持つカードは、支払いとコンボ・ゲージ更新後に`PendingCardPlay`へ入る。このため選択待ち中のstateと返却イベントにはゲージ獲得を反映する。ただし選択を解決するまでキャラクタースキルは使えない。

## 戦技

- API: `BattleEngine.UseTacticalSkill(BattleState)`
- 必要・消費ゲージ: 3
- 現プロトタイプでは「ターン1回」属性を持つ。成功時に`tactical`を`UsedCharacterSkillIdsThisTurn`へ記録し、同じターンの2回目はstateを変更せず拒否する。
- 使用済みID集合は通常のターン終了、直接のターン開始、戦闘決着で空へ戻り、新しい戦闘も空集合から始める。奥義はこの属性を持たず、同集合へ記録しない。
- 生存中のheroへ永続`strength +1`。`int.MaxValue`で飽和する。
- `omnistrike +1`。既存値へ加算し、`int.MaxValue`で飽和する。
- `omnistrike`は既存のターン終了カウントにより、このターンに蓄積した攻撃を全体化してから減少する。
- 新たに付いたstatusの`StatusSources`とstatusイベントには`CombatSourceKind.CharacterSkill`を記録する。
- 複数heroを持つ将来構成では、生存heroのうち`SlotIndex`が最小のものを使用者とする。

## 奥義

- API: `BattleEngine.UseUltimateSkill(BattleState, DataCatalog)`
- 必要・消費ゲージ: 10
- 発動時点の`Hand`だけを対象に、各カードのコンボ軽減前の現在コストと同量の負の`UntilTurnEnd` modifierを追加する。
- 発動直後の対象カードは0コストになる。後から手札コスト増加statusを受けた場合は0より大きくなり得る。
- 発動後に手札へ加わった別Instanceは対象外。同じターンに再発動した場合は新しいmodifierを追加する。
- 対象カードを使用すると、他の個別コスト変更と同じくmodifierは失われる。
- 元コストが`null`のプレイ不可カードは変更しない。
- 手札が空なら誤操作防止のため発動を拒否する。

`UntilTurnEnd` modifierは通常のターン終了、直接のターン開始、戦闘決着時に全領域から消去する。`ComboGauge`はこれらの処理で保持し、次の戦闘開始時だけ0へ戻す。詳細は`docs/current-card-cost-spec.md`を参照。

## 使用できる状態

両スキルは以下をすべて満たす場合だけ使用できる。

- `Outcome=Pending`
- `Phase=PlayerInput`
- `PendingCardPlay=null`
- 生存heroが存在する
- 必要ゲージ以上かつ、stateのゲージが上限10以下
- 奥義は手札が1枚以上

敵行動中、戦闘決着後、選択待ち中、ゲージ不足ではstateを変更せず例外で拒否する。

## 現行ソロ境界とマルチ移行

現時点の`BattleState`は手札・コンボゲージ・カードコストmodifierを1組だけ持つソロ構造である。
また、戦技・奥義のID、消費量、効果は今回の操作検証用プロトタイプとしてCore定数で固定している。
そのため、複数heroを直接入れたテスト状態では生存heroのうち`SlotIndex`が最小のものを
暫定的な使用者とする。この選択規則をマルチプレイの正式仕様にはしない。

ルーム対応へ入る前に、少なくとも次を同時にplayer単位へ移行する。

- コンボゲージとカードコストmodifierの所有者
- 手札・エナジー・コンボ状態
- スキルcommandの`PlayerSlot`または`ActorInstanceId`
- APIの認証済み送信者と操作キャラクターの対応検証
- キャラクター定義から常時特性・戦技・奥義・消費量を読み込むスキル定義と、そのDTO表示

これにより、他プレイヤーのゲージ消費や、最小Slotのheroへの誤発動を防ぐ。

## イベント

- スキル成功時は`UseCharacterSkill`、続いてゲージ消費を表す`ComboGaugeChanged`を返す。
- `UseCharacterSkill.Amount`は消費量の正数、`Note`は`tactical`または`ultimate`。
- ゲージ消費イベントの`Amount`は`-3`または`-10`、`Note`は`remaining=N`。
- 戦技でstatusが実際に変化した場合は、その後に既存の`ApplyStatus`または`RemoveStatus`を返す。
- 奥義でカードへmodifierを付けた場合は、対象Instanceごとに`ModifyCardCost`を返す。
- カード由来のゲージ獲得イベントは`PlayCard`の直後、カード効果イベントより前に置く。`Amount`は実増加量、`Note`は`remaining=N`。
