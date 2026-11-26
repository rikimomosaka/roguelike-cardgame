# 位相（静・動）・双極・カードの後付け能力 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** キャラの「位相（静・動）」、カードの後付け能力の仕組み、その最初の利用例「双極」、ストライカーのカード 8 枚を追加し、戦闘画面に表示する。

**Architecture:** 位相は `CombatActor.Phase`、後付け能力は `BattleCardInstance.Augments`（コスト変更と同じくカード個体について回る）に持たせる。ルールは Core の小さな静的クラス（`PhaseRules`）に集め、PlayCard・ターン終了・攻撃計算・敵の再現実行から呼ぶ。Server は DTO に位相と後付け能力入りの説明文を載せ、Client はオーラ・札・キーワード数を表示する。

**Tech Stack:** C# .NET 10（Core / Server、xUnit）、React 19 + TypeScript（vitest）

**Spec:** [docs/superpowers/specs/2026-11-26-phase-and-card-augments-design.md](../specs/2026-11-26-phase-and-card-augments-design.md)

## Global Constraints

- Core は Server/Client に依存しない。Core で ASP.NET Core・ネットワーク関連の using 禁止。record を使う。
- 位相の値は `none` / `calm`（静）/ `motion`（動）。表示名は「静」「動」。
- 使用時の判定はカード効果の実行前。静＋アタック → なし、動＋アタック以外 → なし。
- 動の計算順: `floor((基本値 + 筋力) × 3 / 2)` → 脱力 `×3/4` 切り捨て → 元気 `×3/2` 切り捨て。動と元気は重なる。
- 双極: キーワード ID `bipolar`、表示名「双極」、数 2 以上は「双極2」。説明文:
  「位相が静の時、ターン終了時に捨てられない。位相が動の時に使用すると、エナジーをこの数だけ得て、このカードの双極は全て消える。」
- 後付け能力は戦闘中だけ。カードのコピー・生成では引き継がない。
- 位相を踏み倒す効果（動のまま非アタックを使える、アタック扱いしない等）は作らない。
- フロントのテストは大文字ドライブのパス（`/C/Users/...`）から実行する。
- 開発用上書きデータ `data-local/dev-overrides/` は git 管理外。触らない。

## Review Focus

1. 敵・副参加者のターン終了でも静の双極付与と持ち越しが働くこと（`ApplyTurnEndToProjectedRoot` 経由）。→ Task 5 にテストあり。
2. 双極付きカードを動で使ったとき、エナジーが「使ったキャラの参加者」に入ること（マルチ・副参加者）。→ Task 4 にテストあり。
3. 頭上予告と実ダメージの一致（動＋筋力＋脱力＋元気の組み合わせ）。→ Task 6 にテストあり。
4. 選択待ち（`select: "choose"`）で一時停止したカード使用が再開したとき、位相の判定が二重に走らないこと。→ Task 4 にテストあり。
5. 旧スナップショット（`Phase`・`Augments` が無い JSON）が読み込めること。→ Task 1・2 にテストあり。

---

### Task 1: 位相の状態とルール

**Files:**
- Create: `src/Core/Battle/State/ActorPhase.cs`
- Create: `src/Core/Battle/Engine/PhaseRules.cs`
- Modify: `src/Core/Battle/State/CombatActor.cs`（init プロパティ追加）
- Modify: `src/Core/Battle/Events/BattleEventKind.cs`（`PhaseChanged = 39`）
- Test: `tests/Core.Tests/Battle/Engine/PhaseRulesTests.cs`

**Interfaces:**
- Produces:
  - `public enum ActorPhase { None = 0, Calm = 1, Motion = 2 }`
  - `CombatActor.Phase { get; init; } = ActorPhase.None`
  - `PhaseRules.Parse(string name) -> ActorPhase`（`"none"|"calm"|"motion"`、それ以外は `ArgumentException`）
  - `PhaseRules.Invert(ActorPhase p) -> ActorPhase`（Calm⇔Motion、None→None）
  - `PhaseRules.AfterCardUsed(ActorPhase p, CardType usedType) -> ActorPhase`（使用時の判定）
  - `PhaseRules.SetPhase(BattleState s, string actorInstanceId, ActorPhase next, string reason, ref int order, List<BattleEvent> events) -> BattleState`
    — 変化があるときだけ actor を置き換え、`PhaseChanged` イベント（`TargetInstanceId`=actor、`Note`=`"{前}->{後}:{reason}"`、例 `"calm->none:used"`）を追加。reason は `"effect"` / `"used"`。actor は Allies・Enemies 両方から InstanceId で探す。

- [ ] **Step 1: 失敗するテストを書く**（`PhaseRulesTests`）
  - `Invert_SwapsCalmAndMotion_AndKeepsNone`: Invert(Calm)==Motion、Invert(Motion)==Calm、Invert(None)==None
  - `AfterCardUsed_CalmAttack_BecomesNone` / `AfterCardUsed_CalmSkill_StaysCalm`
  - `AfterCardUsed_MotionSkill_BecomesNone` / `AfterCardUsed_MotionPower_BecomesNone` / `AfterCardUsed_MotionAttack_StaysMotion`
  - `AfterCardUsed_None_StaysNone`（全 CardType）
  - `SetPhase_EmitsPhaseChanged_OnlyWhenChanged`: hero を Calm に → イベント 1 件 Note `"none->calm:effect"`、もう一度 Calm → イベント 0 件
  - `SetPhase_FindsEnemyByInstanceId`
  - `LegacyActorJson_WithoutPhase_DeserializesAsNone`（既存の BattleState JSON 往復テストと同じ方法で、`phase` キーを消した JSON を読む）
- [ ] **Step 2: 失敗を確認** — `dotnet test tests/Core.Tests --filter PhaseRulesTests` → コンパイルエラーで FAIL
- [ ] **Step 3: 実装** — 上の Interfaces のとおり。`PhaseChanged` に XML コメント「キャラの位相が変わった（Note = 前->後:理由）」。
- [ ] **Step 4: 成功を確認** — 同コマンドで PASS
- [ ] **Step 5: コミット** — `feat(core): actor phase state and rules`

### Task 2: カードの後付け能力と双極キーワード

**Files:**
- Create: `src/Core/Battle/State/CardAugment.cs`
- Modify: `src/Core/Battle/State/BattleCardInstance.cs`（Augments、等価・ハッシュ）
- Modify: `src/Core/Cards/CardKeywords.cs`（`bipolar` 追加）
- Test: `tests/Core.Tests/Battle/State/CardAugmentTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record CardAugment(string Kind, string KeywordId, int Count)`、定数 `CardAugment.KeywordKind = "keyword"`
  - `BattleCardInstance.Augments { get; init; } : ImmutableArray<CardAugment>?`、`EffectiveAugments`（null/default → Empty）
  - `BattleCardInstance.AddKeyword(string keywordId, int count) -> BattleCardInstance`（同じ KeywordId は Count 加算で 1 件、count<=0 は ArgumentOutOfRange）
  - `BattleCardInstance.KeywordCount(string keywordId) -> int`
  - `BattleCardInstance.RemoveKeyword(string keywordId) -> BattleCardInstance`
  - `BattleCardInstance.AugmentedKeywords() -> IReadOnlyList<(string Id, int Count)>`（追加順）
  - `CardKeywords.Bipolar = "bipolar"`

- [ ] **Step 1: 失敗するテスト**
  - `AddKeyword_Stacks`: AddKeyword("bipolar",1) を 2 回 → KeywordCount==2、EffectiveAugments.Length==1
  - `RemoveKeyword_ClearsAll`
  - `Equality_IncludesAugments`: 後付け能力だけ違う 2 枚は等しくない、ハッシュも異なる（同じなら等しい）
  - `ConsumeCostChangesOnPlay_KeepsAugments`
  - `LegacyCardJson_WithoutAugments_IsEmpty`
  - `CardKeywords_Bipolar_HasJapaneseNameAndDescription`: Get("bipolar").Name=="双極"、Description は Global Constraints の文
- [ ] **Step 2: FAIL を確認** — `dotnet test tests/Core.Tests --filter CardAugmentTests`
- [ ] **Step 3: 実装**（Equals / GetHashCode に EffectiveAugments の SequenceEqual / 各要素を追加）
- [ ] **Step 4: PASS を確認**
- [ ] **Step 5: コミット** — `feat(core): per-card augments and bipolar keyword`

### Task 3: カード効果 setPhase / invertPhase / augmentCards

**Files:**
- Modify: `src/Core/Cards/CardEffect.cs`（`string? CardTypeFilter = null` を末尾に追加、Action 一覧コメント更新）
- Modify: `src/Core/Cards/CardEffectParser.cs`（JSON キー `cardTypeFilter`）
- Modify: `src/Core/Battle/Engine/EffectApplier.cs`（3 action の分岐と実装）
- Modify: `src/Core/Battle/Events/BattleEventKind.cs`（`CardAugmented = 40`）
- Modify: `src/Core/Data/EmbeddedDataCatalogValidator.cs`、`src/Core/Data/ContentAuthoringCatalog.cs`（action 登録・検証）
- Modify: `src/Core/Cards/CardTextFormatter.cs`（文言）
- Modify: `src/Core/Battle/Projection/AfterimageIntentProjector.cs`（非攻撃効果の一覧に `setPhase`・`invertPhase` を追加。`augmentCards` は戦場効果ではないので入れない）
- Test: `tests/Core.Tests/Battle/Engine/EffectApplierPhaseTests.cs`、`tests/Core.Tests/Cards/CardTextFormatterPhaseTests.cs`、検証は既存 `EmbeddedDataCatalogValidator` のテストファイルに追記

**Interfaces:**
- Consumes: Task 1 `PhaseRules.Parse/Invert/SetPhase`、Task 2 `AddKeyword`
- Produces（JSON 形）:
  - `{ "action": "setPhase", "scope": "self", "name": "calm" }`、`{ "action": "setPhase", "scope": "single", "side": "enemy", "name": "none" }`
  - `{ "action": "invertPhase", "scope": "self" }`
  - `{ "action": "augmentCards", "scope": "self", "name": "bipolar", "amount": 1, "pile": "hand", "select": "choose"|"all"|"random", "cardCount": 1, "cardTypeFilter": "Attack" }`、`"pile": "self"` はこのカード自身
- 対象の解決: setPhase / invertPhase は既存の buff と同じ scope/side 解決を使う。augmentCards の選び方（all/random/choose、choose は `PendingCardPlay`）は `ApplyModifyCardCost` と同じ流れを共通化して使う。`cardTypeFilter` に合わないカードは候補から外す。
- `CardAugmented` イベント: `TargetInstanceId`=カード InstanceId、`Note`=`"bipolar:{後の数}"`、`Amount`=足した数。

- [ ] **Step 1: 失敗するテスト**（EffectApplierPhaseTests）
  - `SetPhase_Self_Calm`: hero.Phase==Calm、PhaseChanged 1 件
  - `SetPhase_Overwrites`: Motion の hero に calm → Calm
  - `SetPhase_SingleEnemy_None`: 現在対象の敵が Motion → None、hero は変わらない
  - `InvertPhase_Calm_ToMotion` / `InvertPhase_None_NoEvent`
  - `AugmentCards_AllAttacksInHand`: 手札 [strike, defend, strike] に all + Attack → strike 2 枚だけ KeywordCount==1、CardAugmented 2 件
  - `AugmentCards_Choose_PausesWithPendingChoice_ThenAppliesToChosen`
  - `AugmentCards_Self_TargetsPlayedCard`
  - Validator: `setPhase` で name が不正 → エラー、`augmentCards` で name が未知キーワード → エラー、amount<=0 → エラー
- [ ] **Step 2: FAIL を確認** — `dotnet test tests/Core.Tests --filter "EffectApplierPhaseTests|CardTextFormatterPhaseTests"`
- [ ] **Step 3: 実装**
- [ ] **Step 4: 文言テスト（CardTextFormatterPhaseTests）を書いて実装**
  - setPhase self calm → `位相を静にする。`、motion → `位相を動にする。`、none → `位相をなしにする。`
  - setPhase single enemy none → `敵単体の位相をなしにする。`
  - invertPhase self → `位相を反転する。`
  - augmentCards hand choose 1 Attack bipolar 1 → `手札のアタック1枚を選び、[K:bipolar]を1付与する。`
  - augmentCards hand all Attack → `手札のアタック全てに[K:bipolar]を1付与する。`
- [ ] **Step 5: PASS を確認**（上の filter）
- [ ] **Step 6: コミット** — `feat(core): setPhase, invertPhase and augmentCards effects`

### Task 4: カード使用時の判定と双極のエナジー

**Files:**
- Modify: `src/Core/Battle/Engine/BattleEngine.PlayCard.cs`（`PlayCardForActor`）
- Modify: `src/Core/Battle/Engine/EnemyAttackingResolver.cs`（カード制御の敵の再現実行）
- Test: `tests/Core.Tests/Battle/Engine/PhasePlayCardTests.cs`

**Interfaces:**
- Consumes: Task 1・2

**挿入位置と順番（PlayCardForActor）:** `PlayCard` イベントを作った直後、`planEnemyEffects` の分岐より前に、次を行う。
1. `usedPhase = actingActor の現在の Phase`
2. 双極: `usedPhase == Motion` かつ `playedCard.KeywordCount("bipolar") = n > 0` なら、使ったキャラの参加者のエナジーを n 増やし（既存 `ApplyGainEnergy` と同じ参加者解決）、`playedCard = playedCard.RemoveKeyword("bipolar")`、エナジー獲得イベントの `Note` に `"bipolar"`。
3. 判定: `planEnemyEffects == false` のときだけ `PhaseRules.AfterCardUsed(usedPhase, def.CardType)` で変わるなら `SetPhase(..., reason: "used")`。
- ResolveCardChoice（選択待ちからの再開）では 1〜3 を行わない。
- 敵（`planEnemyEffects == true`）の判定は `EnemyAttackingResolver` の再現実行で行う: 予約効果を順に適用する前に、その `Source.InstanceId` のカードが初めて出てきたら、`Source.DefinitionId` のカード種別で `AfterCardUsed` を判定する（`EnemyCardAction` イベントを出す箇所と同じ単位）。

- [ ] **Step 1: 失敗するテスト**
  - `CalmHero_PlaysAttack_BecomesNone_BeforeEffects`: 「7 アタック、位相を静にする」アタックを Calm で使う → 最終 Calm（判定で None → 効果で Calm）、PhaseChanged の Note 順が `calm->none:used`、`none->calm:effect`
  - `NoneHero_PlaysMotionSkill_EndsMotion`: 「位相を動にする」スキルを None で使う → Motion
  - `MotionHero_PlaysSkill_BecomesNone`
  - `MotionHero_PlaysBipolar2Attack_GainsTwoEnergy_AndClearsBipolar`: Energy 3、コスト 1、双極2 → Energy 4、捨て札のそのカードの KeywordCount("bipolar")==0
  - `CalmHero_PlaysBipolarAttack_NoEnergy_KeepsBipolar_AndBecomesNone`
  - `SecondaryParticipant_BipolarEnergy_GoesToThatParticipant`（参加者 2 人の既存フィクスチャ）
  - `ChooseResume_DoesNotJudgeTwice`: Motion の hero が augmentCards(choose) スキルを使う → 一時停止時点で None、選択を解決した後も None のまま、PhaseChanged は 1 件
  - `CardControlledEnemy_PhaseJudgedAtReplay_NotAtPlanning`: Calm の敵がアタックを予約 → 計画後も Calm、敵の行動実行後に None
- [ ] **Step 2: FAIL を確認** — `dotnet test tests/Core.Tests --filter PhasePlayCardTests`
- [ ] **Step 3: 実装**
- [ ] **Step 4: PASS を確認**、続けて `dotnet test tests/Core.Tests` 全体が PASS
- [ ] **Step 5: コミット** — `feat(core): phase judgment on card use and bipolar energy`

### Task 5: 静のターン終了（双極付与と持ち越し）

**Files:**
- Modify: `src/Core/Battle/Engine/BattleCardRetention.cs`
- Modify: `src/Core/Battle/Engine/TurnEndProcessor.cs`（Step 5 の前）
- Modify: `src/Core/Battle/Engine/BattleParticipantTurnLifecycle.cs`（`ApplyTurnEndToProjectedRoot`）
- Modify: `src/Server/Services/BattleStateDtoMapper.cs`（`RetainsAtTurnEnd` の呼び出し）
- Test: `tests/Core.Tests/Battle/Engine/PhaseTurnEndTests.cs`

**Interfaces:**
- Produces:
  - `BattleCardRetention.ShouldRetain(BattleCardInstance card, CardDefinition definition, ActorPhase ownerPhase) -> bool` — 既存条件に「`ownerPhase == Calm` かつ `card.KeywordCount("bipolar") > 0`」を OR。旧 2 引数版は削除し、呼び出し元をすべて更新（DTO では手札の持ち主の Phase を渡す）。
  - `PhaseRules.ApplyCalmTurnEnd(BattleState projected, CombatActor? owner, DataCatalog catalog, ref int order, List<BattleEvent>? events) -> BattleState` — owner が Calm なら手札のアタック全てに `AddKeyword("bipolar", 1)`。events が null でなければ `CardAugmented` を追加。
- 呼び出し: `TurnEndProcessor` は primaryActor を owner に、手札整理（Step 5）の直前で呼び、イベントを返す。`ApplyTurnEndToProjectedRoot` は participantActor を owner に、手札整理の直前で呼ぶ（events は null）。

- [ ] **Step 1: 失敗するテスト**
  - `CalmTurnEnd_GrantsBipolarToHandAttacks_AndRetainsThem`: 手札 [strike, defend] の Calm hero → ターン終了後の手札は strike のみ、KeywordCount==1、defend は捨て札
  - `CalmTwoTurns_StacksToBipolar2`
  - `NoneTurnEnd_DiscardsBipolarCards_ButKeepsTheirBipolar`: 双極1 の strike を持つ None hero → 捨て札へ、KeywordCount==1 のまま
  - `MotionTurnEnd_DoesNotGrantOrRetain`
  - `Phase_PersistsAcrossTurns`: Calm hero の EndTurn 後、次ターン開始時も Calm
  - `SecondaryParticipant_CalmTurnEnd_GrantsAndRetains`（ApplyTurnEndToProjectedRoot 経由）
  - `CardControlledEnemy_CalmTurnEnd_GrantsAndRetains`
- [ ] **Step 2: FAIL を確認** — `dotnet test tests/Core.Tests --filter PhaseTurnEndTests`
- [ ] **Step 3: 実装**
- [ ] **Step 4: PASS を確認**、`dotnet build` が通る（Server の呼び出し更新込み）
- [ ] **Step 5: コミット** — `feat(core): calm turn end grants and retains bipolar attacks`

### Task 6: 動のダメージ 1.5 倍（実ダメージと予告）

**Files:**
- Modify: `src/Core/Battle/Engine/AttackDamageMath.cs`
- Modify: `src/Core/Battle/Engine/DealDamageHelper.cs`、`src/Core/Battle/Projection/CombatIntentProjector.cs`（4 か所）、`src/Core/Battle/Projection/AfterimageIntentProjector.cs`
- Test: `tests/Core.Tests/Battle/Engine/AttackDamageMathPhaseTests.cs`

**Interfaces:**
- Produces: `AttackDamageMath.AdjustForAttacker(int baseAmount, int strength, int weak, int vigor = 0, bool motion = false) -> int`
  — 計算順は Global Constraints のとおり（`motion` なら `(base+strength)*3/2` を先に切り捨て）。各呼び出し元は攻撃者（予告では仮想 actor）の `Phase == ActorPhase.Motion` を渡す。

- [ ] **Step 1: 失敗するテスト**
  - `Motion_MultipliesBeforeWeak`: base 7、strength 1、weak 1、motion → floor(floor(8×1.5)×3/4) = 9
  - `Motion_StacksWithVigor`: base 7、vigor 1、motion → floor(floor(7×1.5)×1.5) = 15
  - `NoMotion_Unchanged`: base 7 → 7
  - `PlayerAttacking_UsesPhaseAtResolution`: 7 アタックを溜めた hero を Motion にしてから EndTurn → 敵に 10 ダメージ、None に戻してから EndTurn → 7
  - `Intent_ShowsMotionDamage`: Motion の hero の予告合計が 10、None にすると 7（CombatIntentProjector）
  - `Enemy_Motion_DealsMoreDamage`
- [ ] **Step 2: FAIL を確認** — `dotnet test tests/Core.Tests --filter AttackDamageMathPhaseTests`
- [ ] **Step 3: 実装**
- [ ] **Step 4: PASS を確認**、`dotnet test tests/Core.Tests` 全体 PASS
- [ ] **Step 5: コミット** — `feat(core): motion phase multiplies attack damage`

### Task 7: ストライカーのカード 8 枚

**Files:**
- Create: `src/Core/Data/Cards/striker_phase_calm_form.json` ほか 7 枚（下表の ID）
- Modify: 件数を数えるテスト（`ExpandedRewardCardCatalogTests`、`EmbeddedDataLoaderTests` など、`dotnet test` で落ちたもの）
- Test: `tests/Core.Tests/Data/StrikerPhaseCardsTests.cs`

形式は既存のストライカー報酬カード（例 `reward_common_02.json`）と同じ: `poolKinds: ["Reward"]`、`poolClass: "Striker"`、`eligibleCharacterIds: []`、`rarity: 1`、`activeVersion: "v1"`、version label `"phase-v1"`。

| ID | name | 種類・コスト | effects | upgradedEffects |
|---|---|---|---|---|
| `striker_phase_calm_form` | 静の型 | Skill 1 | block self 5、setPhase self calm | block 8、setPhase calm |
| `striker_phase_motion_form` | 動の型 | Skill 1 | draw self 1、setPhase self motion | 同じ効果、`"upgradedCost": 0` |
| `striker_phase_calm_blade` | 静の太刀 | Attack 1 | attack single enemy 7、setPhase self calm | attack 10、setPhase calm |
| `striker_phase_blazing_step` | 烈火の踏み込み | Attack 1 | attack single enemy 5、setPhase self motion | attack 8、setPhase motion |
| `striker_phase_flowing_stance` | 流転の構え | Skill 0 | invertPhase self | invertPhase self、draw self 1 |
| `striker_phase_return_edge` | 返し刃 | Attack 1 | attack single enemy 6、invertPhase self | attack 9、invertPhase self |
| `striker_phase_honing` | 研ぎ澄まし | Skill 1 | augmentCards bipolar 1、pile hand、select choose、cardCount 1、cardTypeFilter Attack | 同じで amount 2 |
| `striker_phase_quelling_shout` | 鎮めの一喝 | Skill 1 | setPhase single enemy none、block self 2 | setPhase none、block 5 |

- [ ] **Step 1: 失敗するテスト**（StrikerPhaseCardsTests）: 8 ID が全て読み込め、PoolClass==Striker、Reward プールに入り、各カードの effects の action 列が表のとおり（例 calm_form → `["block","setPhase"]`）
- [ ] **Step 2: FAIL を確認** — `dotnet test tests/Core.Tests --filter StrikerPhaseCardsTests`
- [ ] **Step 3: JSON を追加**
- [ ] **Step 4: `dotnet test` 全体を流し、件数系テストの期待値を +8 に更新**（理由をコメントに一行）
- [ ] **Step 5: PASS を確認** — `dotnet test`（Core と Server）
- [ ] **Step 6: コミット** — `feat(cards): first striker phase cards`

### Task 8: Server の DTO

**Files:**
- Modify: `src/Server/Dtos/CombatActorDto.cs`（`string Phase = "none"` を末尾に追加）
- Modify: `src/Server/Services/BattleStateDtoMapper.cs`（Phase、後付け能力入り説明文）
- Modify: `src/Core/Cards/CardTextFormatter.cs`（後付けキーワードを受ける引数）
- Test: `tests/Server.Tests/Services/BattleStateDtoMapperPhaseTests.cs`

**Interfaces:**
- Produces:
  - `CardTextFormatter.FormatWithUsageCount(CardDefinition def, bool upgraded, CardActorContext context, int usageCount, IReadOnlyList<(string Id, int Count)>? augmentedKeywords)` — キーワード行に定義のキーワードの後ろへ `[K:bipolar]`（数 1）/`[K:bipolar|2]`（数 2 以上）を足す。定義と同じ ID は数を合算して 1 つにする。既存の 4 引数版はこの版へ null で委譲。
  - 手札などのカード DTO の `AdjustedDescription` / `AdjustedUpgradedDescription` は `card.AugmentedKeywords()` を渡して作る。
  - `CombatActorDto.Phase` は `"none"|"calm"|"motion"`。

- [ ] **Step 1: 失敗するテスト**
  - `ActorDto_HasPhase`: Calm の hero → `"calm"`
  - `HandCard_WithBipolar2_DescriptionHasCountedKeyword`: AdjustedDescription に `[K:bipolar|2]` を含む
  - `HandCard_RetainsAtTurnEnd_WhenOwnerCalmAndBipolar`
- [ ] **Step 2: FAIL を確認** — `dotnet test tests/Server.Tests --filter BattleStateDtoMapperPhaseTests`
- [ ] **Step 3: 実装**
- [ ] **Step 4: PASS を確認**、`dotnet test` 全体 PASS
- [ ] **Step 5: コミット** — `feat(server): expose phase and augmented keywords`

### Task 9: 画面（オーラ・札・双極の表示）

**Files:**
- Modify: `src/Client/src/api/types.ts`（`CombatActorDto.phase?: 'none' | 'calm' | 'motion'`、`BattleEventKind` に `'PhaseChanged' | 'CardAugmented'`）
- Modify: `src/Client/src/components/CardDesc.tsx`（`KEYWORD_DEFS.bipolar`、K マーカーの数表示）
- Modify: `src/Client/src/screens/BattleScreen.tsx`（`CharacterDemo.phase`、Slot に位相クラスと札）
- Modify: `src/Client/src/screens/battleScreen/dtoAdapter.ts`（actor → CharacterDemo に phase）
- Modify: `src/Client/src/screens/BattleScreen.css`（オーラ）
- Test: `src/Client/src/components/CardDesc.test.tsx`、`src/Client/src/screens/BattleScreen.phase.test.tsx`

**仕様:**
- `[K:bipolar|2]` は「双極2」と表示（ラベル + 数）。`[K:bipolar]` は「双極」。他のキーワードの `|` 付きは従来どおり無視しない（今は使われていないので数として扱ってよい）。
- Slot: `phase` が calm/motion のとき、ルート要素に `has-phase has-phase--calm|motion`、スプライトの後ろに `<span class="slot-phase-aura slot-phase-aura--calm|motion" aria-hidden>`、名前の近くに `<span class="slot-phase-tag">静|動</span>`。
- オーラ: 足元から立ちのぼる縦長の光。静 = 青（`rgba(110,170,255,…)`）ゆっくり（2400ms 周期）、動 = 赤（`rgba(255,90,70,…)`）速め（1100ms 周期）、無限ループ。`html[data-motion='off']` では静止、`reduced` では周期を 2 倍。
- 位相が変わったときは、既存の一時オーラ（`showSlotAura`）を buff 色で 1 回出す（`PhaseChanged` イベント）。

- [ ] **Step 1: 失敗するテスト**
  - CardDesc: `renders counted keyword`: `[K:bipolar|2]` → テキスト「双極2」、data-keyword="bipolar"
  - BattleScreen.phase: calm の hero → `.slot-phase-aura--calm` と「静」の札がある、none → どちらも無い
- [ ] **Step 2: FAIL を確認** — `cd /C/Users/Metaverse/projects/roguelike-cardgame/src/Client && npx vitest run src/components/CardDesc.test.tsx src/screens/BattleScreen.phase.test.tsx`
- [ ] **Step 3: 実装**
- [ ] **Step 4: PASS を確認**、`npx tsc --noEmit -p .` と `npx vitest run` 全体 PASS
- [ ] **Step 5: コミット** — `feat(client): phase aura, phase tag and counted keywords`

### Task 10: 仕様書の移動と実機確認

**Files:**
- Create: `docs/current-phase-spec.md`（設計書 §2〜§6 と、実装で決めた細部を現行仕様として書く）
- Modify: `docs/superpowers/specs/2026-11-26-phase-and-card-augments-design.md`（冒頭の状態を「実装済み、現行仕様は current-phase-spec.md」に）
- Modify: `CLAUDE.md`（「進行中の仕様変更」に位相の現行仕様へのリンクを 1 行）

- [ ] **Step 1: 文書を書く**
- [ ] **Step 2: 全テスト** — `dotnet test` と `npx vitest run`（大文字パス）が PASS
- [ ] **Step 3: 実機確認** — テスト用サーバー（API 5214・Vite 5273）を作り直して起動し、セラで戦闘に入る。開発用にデッキへ新カードを入れる手段が無いので、`tests/Server.Tests` の戦闘 API テストで「静の型 → ターン終了 → 手札のアタックに双極・持ち越し → 動の型 → 双極アタックでエナジー」を通しで確認するテストを 1 本追加して代える（`PhaseFlowApiTests`）。画面のオーラと札は vitest の描画テストで確認済みとする。
- [ ] **Step 4: コミット・プッシュ** — `docs: current phase spec`
