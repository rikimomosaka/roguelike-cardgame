# 攻撃蓄積・ヒット解決仕様

最終更新: 2026-08-08

この文書は、ターン終了時にまとめて攻撃する戦闘システムの現行基準である。
過去の Phase 10 設計書と記述が異なる場合、この文書と実装テストを優先する。

## 1. 基本方針

- 攻撃はカード使用時に即時ダメージを与えず、攻撃者・Scope ごとの順序付きヒット列へ追加する。
- `AddCount` は `HitAmounts.Length` から求める予定ヒット数である。Strength の係数や演出だけの回数ではない。
- 頭上表示や HP 演出では合計値を表示してよいが、Core は必ず 1 ヒットずつ解決する。
- ヒット数は演出だけの数ではなく、Block、バリア、反射、吸血、確率効果、OnHit、OnDamage、OnKill の処理単位である。
- 同じ合計値・ヒット数でも各ヒットの威力が異なり得るため、合計値と件数だけでなく各ヒットの基礎威力と順序を保持する。
- 旧 `Sum` / `AddCount` だけの AttackPool JSON から各ヒット威力は復元できないため、黙って推測せず読み込みエラーにする。現行サーバーは BattleState を永続保存していないため既存ランセーブへの移行は不要。

用語:

- 予定ヒット: ターン中に攻撃列へ追加されたヒット。
- 命中ヒット: 解決時に生存対象が存在し、その対象へ到達したヒット。完全 Block でも命中に含む。
- ダメージヒット: 対象の HP を 1 以上減らしたヒット。

## 2. Scope と対象

### Single

- 現行ソロでは、ターン終了時のプレイヤーの現在対象 1 体へ、蓄積した全 Single ヒットを順番に当てる。
- カード使用時の対象は各ヒットへ固定しない。途中で対象を変更した場合、それ以前に蓄積した Single ヒットも最終対象へ集中する。
- 対象が途中で死亡した場合、残りの Single ヒットは不発。他の敵へ自動照準しない。
- 解決開始前に対象が死亡済みの場合、その攻撃者の Single ヒットはすべて不発。
- マルチでは現在対象を戦闘全体で共有せず、プレイヤーごとに保持する。他プレイヤーの対象変更は影響しない。

### Random

- Random ヒット 1 件ごとに、解決時点の生存敵から独立に 1 体を抽選する。
- 直前のヒットで死亡した敵は、次の抽選候補から除外する。
- 生存敵が存在しなければ、そのヒットは不発。

### All

- All ヒット 1 件ごとに、解決時点の全生存敵へ同じ基礎威力を 1 回ずつ当てる。
- 途中で死亡した敵は、以後の All ヒットの対象から除外する。
- 各対象の Block、Dexterity、Vulnerable は個別に計算する。

### Omnistrike

- Single、Random、All の各ヒットの威力・件数・順序を維持したまま、各ヒットを All として解決する。
- 全ヒットを 1 個の合計ダメージへ統合しない。

## 3. プレイヤー攻撃の順序

現行ソロの攻撃者内順序は次のとおり。

1. Single
2. Random
3. All
4. 召喚ユニット

同じ Scope 内では、カードを使用した順、カード内では効果の記載順に解決する。

マルチではルームで決めたプレイヤー順を Server が保持し、通信到着順に依存せず次の順で解決する。

1. ルーム順 1 人目の Single、Random、All、召喚
2. ルーム順 2 人目の Single、Random、All、召喚
3. 以後同様

## 4. 1 ヒットのダメージ計算

各ヒットについて、次の順で整数計算する。

```text
attackWithStrength = baseAmount + attacker.Strength

attackAfterWeak =
    attacker.Weak > 0
        ? floor(attackWithStrength * 3 / 4)
        : attackWithStrength

attack = max(0, attackAfterWeak)

effectiveBlock =
    max(0, target.Block.Sum + target.Block.AddCount * target.Dexterity)

absorbed = min(attack, effectiveBlock)
unblocked = attack - absorbed

hpDamage =
    target.Vulnerable > 0
        ? floor(unblocked * 3 / 2)
        : unblocked
```

- Strength、Weak、Block、Dexterity、Vulnerable はヒットごとに評価する。
- Weak と Vulnerable の端数切り捨てもヒットごとに行う。
- Block はヒットごとに消費する。消費後の残量は `Sum` へ確定し、`AddCount=0` となる。
- 完全 Block でも命中ヒットとして扱うが、ダメージヒットには数えない。
- Poison は攻撃ヒットではなく、ターン開始時の Block 無視 HP ダメージとして別処理する。

## 5. ヒット反応の契約

ヒット反応は次の区別に従う。

- OnHit: 生存対象へヒットが到達したとき。完全 Block でも発火。
- OnBlock: Block が 1 以上消費されたとき。
- OnDamage: 対象 HP が 1 以上減ったときだけ発火。
- OnKill: そのヒットで対象が生存から死亡へ変化したとき、1 回だけ発火。
- 固定反射: 命中ヒットごとに発火し、完全 Block でも反射する。
- 吸血: オーバーキルを除いた実際の HP 減少量を基準にする。Block された量は含めない。
- 「命中時 N%」: 命中ヒットごとに独立抽選し、完全 Block でも抽選する。
- 「ダメージ時 N%」: ダメージヒットごとに独立抽選する。
- 反射やヒット反応で攻撃者が死亡した場合、解決中の All ヒットは開始時に確定した対象まで完了し、次のヒット以降は不発になる。

バリア、固定反射、吸血、確率`OnHit`は実装済み。`OnBlock`と汎用の「ダメージ時N%」は、この区別に従って今後追加する。

## 6. 敵と召喚

プレイヤー、敵、召喚で Scope とヒット計算の意味を統一する。

- Single: 選択済みの生存相手 1 体。
- Random: ヒットごとに生存相手から独立抽選。
- All: ヒットごとに全生存相手。
- 複数の attack effect: effect ごとの実ヒット。

敵の Single 対象は敵 AI が行動開始時に決定する。対象方針を定義していない既存データは、生存相手の最小 SlotIndex を暫定 fallback とする。
召喚の Single は所有プレイヤーの現在対象を使用する。

## 7. 表示とイベント

- 頭上表示は Scope ごとに `N HIT / 合計 X` を表示する。`合計 X × N` とは表示しない。
- 合計 X は、各ヒットへ Strength と Weak を適用した後の Block 適用前合計。
- Core は命中ヒットごと、対象ごとに AttackFire と DealDamage を発行する。
- All の同一ヒットが複数対象へ当たる場合、それらは同じ HitIndex を持つ。
- HitIndex は攻撃者の1回の行動内で 0 から始まる。イベントを一意に束ねるキーは `CasterInstanceId + HitIndex` とする。
- AttackFire / DealDamage の Note には実効 Scope（`single` / `random` / `all`、Omnistrike は `omnistrike`）を記録する。
- DealDamage イベントは BlockedAmount を保持し、Client は攻撃値と HP ダメージの差から Block 消費量を推測しない。
- 完全 Block でも実ヒット分の攻撃スライドと Block 減少を再生する。HP ダメージがないため赤いダメージ点滅だけは行わない。

## 8. マルチ時の帰属

各ヒットは現在、次の構造化情報を保持する。

- 所有プレイヤーの論理Slot
- CasterInstanceId
- 召喚の場合のOwnerInstanceId
- SourceCardInstanceId / SourceEffectIndex
- Scope（イベントNote）/ hit sequence
- 実際の TargetInstanceId
- 基礎威力、Block 吸収量、HP ダメージ、反射量、吸血量
- DamageType / 原因チェーン / Killを発生させたActorDeath

Kill は対象を生存から死亡へ変えたヒットの所有プレイヤーへ付与する。召喚は所有プレイヤーへ帰属させる。その他の貢献者はAssistとして別記録可能にする。

現行ソロは論理Slotのみを保持し、アカウントIDをCoreへ持ち込まない。Poisonは最後の付与元を保持するが、付与元別stack、Kill/Assistの永続集計、複数プレイヤーのSlot管理はマルチ実装前の残件とする。
