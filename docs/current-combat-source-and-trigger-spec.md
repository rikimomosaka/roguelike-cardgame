# 戦闘ダメージの出所・ヒット時効果仕様

更新日: 2026-08-08

この文書は `current-attack-resolution-spec.md` を補足し、1ヒットが「誰の、何による攻撃か」と、そのヒットから派生する効果の順序を定義する。

## 1. 基本原則

- ダメージ処理の最小単位は「1体に対する1ヒット」とする。
- `All` の1ヒットが敵3体へ当たる場合は、同じ論理ヒット番号を共有する3件の対象別ヒットとして処理する。
- 完全に防がれてHPが減らなくても命中は成立する。
- 攻撃値、Block吸収量、実HP減少量、Overkill量を別々に記録する。
- 召喚の物理的な攻撃者は召喚自身だが、効果と撃破の所有者は召喚主とする。
- リプレイ可能性を保つため、原因IDへ時刻やランダムなGUIDを使用しない。

## 2. ダメージ種別

| 種別 | 用途 | Strength / Weak | Vulnerable / Block | OnHit | 吸血 | 反射 |
|---|---|---:|---:|---:|---:|---:|
| Attack | カード・敵・召喚の通常攻撃 | 適用 | 適用 | 発火 | 対象 | 対象 |
| Poison | 毒のターンダメージ | 不適用 | 不適用 | 不発 | 不発 | 不発 |
| DirectHpLoss | 効果による直接HP減少 | 不適用 | 不適用 | 不発 | 不発 | 不発 |
| SelfDamage | 自傷 | 不適用 | 不適用 | 不発 | 不発 | 不発 |
| Reflected | 反射による固定ダメージ | 不適用 | 不適用 | 不発 | 不発 | 再反射しない |
| Environment | 将来の罠・地形 | 個別定義 | 個別定義 | 原則不発 | 不発 | 不発 |

## 3. 出所情報

各ヒットと派生イベントは、可能な範囲で次を保持する。

- 所有プレイヤーの論理Slot
- 所有Actor ID
- 実際に行動したActor ID
- 出所種別（CardEffect / PotionEffect / RelicEffect / PowerEffect / EnemyMove / SummonMove / Status / Environment）
- 出所定義ID
- カードInstance ID
- effect index
- Scope
- DamageType
- 原因チェーン

同じ論理ヒットは`HitIndex`で保持する。Clientへは上記情報に加えて`ActualHpLoss`、`Overkill`、`HitLanded`をDTOで渡す。

原因チェーンは、通常攻撃から反射や追加攻撃が発生した場合に親子関係を記録する。反射済みのチェーンから再反射は発生させない。

## 4. 1対象1ヒットの処理順

1. 発射元・所有者・対象が有効か確認する。
2. StrengthとWeakをそのヒットへ適用する。
3. 正のAttackに対してBarrierを確認する。
4. BarrierがなければBlockを消費する。
5. Block通過後にVulnerableを適用する。
6. HPを0未満にせず減らし、実HP減少量とOverkill量を記録する。
7. `DealDamage`を0ダメージの場合も発行する。
8. 死亡した場合は`ActorDeath`を一度だけ発行する。
9. プレイヤー／召喚のAttackであれば、完全Block・Barrierを含めてOnHitをレリック→Powerの順で発火する。
10. 実HP減少量を基準に吸血する。
11. 攻撃開始時点で対象が持っていた反射を解決する。
12. 敵死亡トリガーとKill帰属を確定する。

敵から味方への攻撃では、対象ごとに吸血→反射まで処理し、同じ`All`ヒットの全対象が終わった後、生存中の主人公が実HPを失っていれば`OnDamageReceived`を1回発火する。

同じ`All`ヒットでは対象一覧と攻撃者の攻撃補正を開始時に固定する。対象ごとの反応で攻撃者が変化しても、その変化は次の論理ヒットから反映する。

## 5. Barrier

- `barrier`の値は、攻撃でHPが減少するヒットを無効化できる回数を表す。
- 攻撃がBlockを貫通してHPを1以上減らす場合、HPを減らさずに1減る。
- Barrierを消費したヒットはBlockを消費しない。
- 攻撃が完全にBlockされてHPが減らない場合、Barrierは消費しない（その場合は通常どおりBlockを消費する）。
- 元の攻撃値が0以下、または対象のHPが0の場合はBarrierを消費しない。
- 命中とOnHitは成立するが、OnDamageReceived・吸血は発生しない。

## 6. Lifesteal

- `lifesteal`の値は回復率（%）を表す。
- 回復量は `floor(実HP減少量 × lifesteal / 100)` とする。
- Overkill、Block、Barrierで防がれた量は含めない。
- 回復先は攻撃所有者。召喚攻撃の場合は召喚主を回復する。
- 最大HPを超えて回復しない。

## 7. Reflect

- `reflect`の値は返す固定HPダメージを表す。
- 元ヒットがAttackの場合だけ発生する。
- Strength、Weak、Vulnerable、Blockの影響を受けない。
- 反射から吸血、OnHit、追加反射は発生しない。
- 元の対象がそのヒットで死亡しても、ヒット開始時に反射を持っていれば1回返す。

## 8. 確率OnHit

- `OnHit`はAttackの対象別ヒットごとに1回判定する。
- 完全BlockとBarrierでも判定する。
- 死亡済みで命中しなかった対象には判定しない。
- 0%は乱数を消費せず不発、100%は乱数を消費せず必ず発火する。
- `Random`や確率判定は保存時刻・プレイ時間ではなく、戦闘RNGからのみ取得する。
- 発火したdebuff等は、現在の選択対象ではなく実際に命中した対象へ付与する。
- `OnHit`の`attack`は親ヒットの実対象へ即時の追加Singleヒットとして解決し、独立した`HitIndex`を持つ。
- `OnHit`由来の追加attackはBarrier、Block、吸血、反射、Killを通常どおり処理するが、そこから`OnHit`を再発火しない。原因チェーンへ`OnHit`を残し、無限連鎖を防ぐ。

## 9. マルチプレイへの契約

- プレイヤーSlotはルームで決め、ラン開始後は固定する。
- 主人公と召喚は`OwnerPlayerSlot`と`OwnerActorInstanceId`を保持する。召喚時に発生元から固定し、解決時に「先頭の主人公」へ付け替えない。旧戦闘データで両方がない場合だけソロ用fallbackを使う。
- 同じターンの攻撃はSlot順に解決する。
- 各プレイヤーは自分専用のSingle対象を持つ。
- Killは死亡を発生させたイベントの原因チェーンから所有Slotを判定する。
- 現行Poisonは状態ごとに最後の付与元を保存し、tickとKillをその所有Slotへ帰属する。複数プレイヤーが同じPoisonへ積んだ場合も最後の付与者がKill所有者となる暫定規則である。
- Assistまで正確に集計する段階で、同一状態の付与量と付与元を分けて保持するStatus contribution方式へ拡張する。
- Serverを正とし、Clientはイベントの出所情報から演出・ログ・戦績を構築する。
