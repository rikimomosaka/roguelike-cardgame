# 敵カードAI化・キャラクター能力値システム 改修仕様（提案・未実装）

> 作成日: 2026-11-23
> 状態: **提案（仕様確認中）**。実装はまだ行っていない。
> 本文書の「確定要件」は依頼者が決定済みの方針、「提案」は Claude の調査に基づく案であり未承認。
> 「未決事項」は依頼者の回答があるまで独自判断で仕様化してはならない。
> 実装完了した部分は `current-*` 文書へ移し、本文書から削除・参照へ置き換える。

---

## 1. 目的

- 敵の固定行動パターン（Move state-machine）を、プレイヤー・CPU味方と共通の
  カード使用・行動予約方式へ置き換える。
- 敵のカード選択は既存CPU味方の選択アルゴリズム（`low_cost_combo`）を共通利用する。
- 将来、プレイヤー・味方・敵で共通の能力値体系（体力・攻撃力・守備力・行動力・ドロー枚数）を持てる構造にする。

## 2. 確定要件（依頼者決定済み）

### 2-1. 基本方針

- 既存の予約制バトル、カード循環、コンボ、バフ・デバフのリアルタイム数値変動を維持する。
- 味方CPUのカード選択ロジックを複製せず共通利用する。敵専用アルゴリズムは新設しない。
- プレイヤー側の予約時即時処理は変更しない。
- 必要以上のリファクタリングをしない。

### 2-2. 敵のカードベース行動

- 敵はデッキ・手札・捨て札・行動力・能力値を持つ。
- 敵は手札からカードを選んで行動を予約し、行動予告を表示する。
- 攻撃等は予約時に実行せず、プレイヤーターン終了後の既存タイミングで実行する。
- ドロー等「予約段階で即時処理する効果」は既存どおり予約時に処理する。
  追加ドローで引いたカードも同ターンの予約候補になる。
- **敵の通常戦闘中のバフ・デバフは予約時に反映しない。** 予約のみ行い、
  敵の予約行動の実行時に反映する（行動予告表示中・プレイヤー行動中は未反映）。
- 能力値参照効果は予約時に数値を固定せず、実行時点の有効値を参照する。

### 2-3. 敵デッキ

- 共通行動カード（複数の敵、将来は味方も使える）＋固有行動カードで構成する。
- 基本カード「通常攻撃」「通常防御」は固定値ではなく使用者の攻撃力・守備力を参照する。

### 2-4. 先制行動

- 敵の登場時に発動する特殊効果（バフ・特殊状態付与等。先制攻撃の意味ではない）。
- カードベースで定義するが、通常デッキに入れず独立した「先制行動プール」で管理する。
- 登場時に発動から効果処理までを完了する。通常のカード使用・予約とは別の仕組み。

### 2-5. 能力値体系（構想）

- 能力値: 体力 / 攻撃力 / 守備力 / 行動力 / 毎ターンドロー枚数。
- 基礎値・永続補正（ラン中）・一時補正（戦闘中、原則戦闘終了で解除）・有効値に分けて管理する案。
- 行動力は **上限付き加算方式**: カードコスト分消費、ターン開始時に規定量を加算、
  上限超過分は切り捨て、未消費分は上限まで持ち越し、不足時はカード使用不可。

### 2-6. 図鑑

- 敵図鑑で「基礎能力値」「通常行動デッキ」「先制行動」を別枠で表示する。
  抽象的な指標（性格グラフ等）より実数値とカード構成を優先する。

---

## 3. 既存コード調査結果（2026-11-23 時点）

### 3-1. 敵の固定行動パターン

| 要素 | 場所 |
|---|---|
| 定義 | `EnemyDefinition`（`Hp`, `Pool`, `InitialMoveId`, `Moves`）、基底 `CombatActorDefinition`、`MoveDefinition(Id, Kind, Effects, NextMoveId)`、`MoveKind` |
| データ | `src/Core/Data/Enemies/*.json`（34体）。`moves[].effects` は `CardEffect` 形式 |
| 戦闘中状態 | `CombatActor.CurrentMoveId`（敵は `BattleEngine.Start` で `InitialMoveId` を設定） |
| 実行 | `EnemyAttackingResolver.ResolveRemaining`: 敵ごとに Block リセット（堅牢分のみ持越）→ `BeforeEnemyAttack` 有事 → Move の effects を**記載順に即時発射**（attack は per-effect 即時ダメージ、他は `EffectApplier.Apply`）→ `NextMoveId` へ遷移 |
| Single 対象 | 行動開始時の「生存味方で最小 Slot」。途中死亡で残り Single は不発 |
| 予告 | `CombatIntentProjector`: `CurrentMoveId` の Move を投影（筋力・脱力等を live 反映） |
| 注意 | 現行の敵バフは**既に実行時反映**（Move 実行時に `EffectApplier` で付与）。敵 Block は「前ターン末の Move で得て、次のプレイヤー攻撃で消費」される時間軸 |

### 3-2. 味方CPUのカード選択

- `CpuCardPlayPolicy.ChooseNext`（副作用なし、1枚ずつ決定）。比較順は
  コンボ軽減継続 → 継続 → 途切れ → 実支払コスト低 → Unit priority 高 → 手札左。
  単体攻撃対象は「倒せる敵 → HP 少 → Slot 小」。
- `BattleCpuPlanningCoordinator.PlanUntilReady`: 参加者生成（`EnsureParticipants`）、
  1枚ごとに `BattleEngine.PlayCard(participantId, …)` を呼ぶ（人間と同じコマンド）、
  choose は先頭候補を自動選択、上限 30/100 行動・同一状態再訪で停止、Ready 化。
- **敵へ転用できない箇所（味方側前提のハードコード）**:
  - `CpuCardPlayPolicy`: actor を `state.Allies` から検索、`catalog.TryGetUnit` で
    deck/AI profile/priority を取得、対象を `state.Enemies` から選択。
  - `BattleEngine.PlayCardForActor`: caster を `state.Allies[casterAllyIndex]` で扱う。
  - `EffectApplier.ApplyAttack`: Single 対象を `GetSelectedEnemyInstanceId`（root の敵対象）で固定。
  - `BattleCpuPlanningCoordinator`: 生存味方 Unit を走査して参加者を生成、Ready 同期に参加。
  - `BattleParticipantAccess`（ProjectToRoot / Slot 解決）も `state.Allies` 前提。

### 3-3. プレイヤーのカード予約・実行

- `BattleEngine.PlayCard` → participant を root へ投影 → `PlayCardForActor`
  （使用回数・制限・コスト・コンボ・エナジー支払・状態減算）→ 効果を記載順に `EffectApplier.Apply`
  → `CommitRootToParticipant`。
- `EndTurn`/Ready 全員揃い → `PlayerAttackingResolver`（味方の AttackPool を Single→Random→All→召喚順で解決）
  → `EnemyAttackingResolver` → `TurnEndProcessor` → `FormalTurnEndStatusProcessor` → `TurnStartProcessor`。

### 3-4. 予約時に即時反映される効果

`EffectApplier.Apply` の action 別:

| 予約（実行時解決） | 予約時に即時反映 |
|---|---|
| `attack`（`AttackPool` にヒット列として蓄積。筋力・脱力・元気・脆弱等はヒット解決時に live 参照） | `block`（`BlockPool` へ即加算。敏捷は表示/解決時に live 参照）、`buff`/`debuff`、`heal`、`draw`/`drawCards`、`discard`、`exhaust*`、`gainEnergy`、`gainMaxEnergy`、`gainComboGauge`、`upgrade`、`summon`、`selfDamage`、`addCard`、`recover*`、`modifyCardCost` |

- `AmountSource`（`handCount` 等）は `EffectApplier.ResolveAmount` で**使用時に固定**される
  （`current-participant-cpu-replay-spec.md` §5 でも「AmountSourceの使用時算出値」を Intent に固定と明記）。
  → 要件「能力値参照は実行時の有効値」とは別の仕組みが必要。

### 3-5. ドロー・捨て札・山札再構築

- `DrawHelper.Draw`（HandCap 10、参加者別手札上限あり、山札不足時に捨札を Fisher-Yates で再構築）。
- ターン開始: `BattleParticipantTurnLifecycle.ApplyStartToAllParticipants`（エナジー加算＋ドロー）。
- ターン終了: `TurnEndProcessor` / `ApplyEndToSecondaryParticipants`（待機以外の手札を捨札へ）。
- Unit CPU は `EnsureParticipants` でデッキ展開・シャッフル・初期ドロー。

### 3-6. コンボ・コスト・行動力

- コンボ/コンボゲージ/Wild 等は参加者ごと（`BattleParticipantState`）。判定は `BattleCardCostCalculator`。
- 行動力 = 既存「エナジー」。`Energy` と `EnergyPerTurn`（root では互換名 `EnergyMax`）。
  **現行は保有上限なし**（`current-energy-spec.md`）、`int.MaxValue` 飽和のみ。
- プレイヤー: `CharacterDefinition.EnergyPerTurn`(3) / `DrawPerTurn`(5)。
  Unit: `InitialEnergy` / `EnergyPerTurn` / `DrawPerTurn` / `HandLimit`。

### 3-7. バフ・デバフ

- `CombatActor.Statuses`（`strength`/`dexterity` は符号付き、他は 0 以下で削除）＋ `StatusSources` / `StatusDisplayOrder`。
- 減衰: `FormalTurnEndStatusProcessor` / `SideStatusCountdown`（自分の行動ターン終了時）、
  ターン開始時効果は `TurnStartProcessor`。戦闘終了で全消滅（CombatActor ごと破棄）。

### 3-8. 敵図鑑

- Server `BestiaryController` は `PlayerCharacters` / `AllyCharacters`（Actions・SummonCards 付き）を返すが、
  敵は `EncounteredEnemyIds` / `AllKnownEnemyIds` の ID のみ。
- Client `AchievementsScreen` の「モンスター」タブは `useEnemyCatalog`（`/catalog/enemies`: 名前・HP・画像のみ）で
  タイル表示（ツールチップに最大HP）。**行動内容の表示は現状なし**。
- 改修箇所: `BestiaryDto` に敵詳細（基礎能力値・行動デッキ・先制行動）を追加、
  `CatalogController.EnemyCatalogEntryDto`、`AchievementsScreen` に敵詳細パネル（味方の `BuildAllyActions` 表示を流用）。

### 3-9. 能力値の既存構造

- 統一的な能力値構造は**存在しない**。
  - 体力: プレイヤー `RunState.CurrentHp/MaxHp`、敵・Unit は定義の `Hp` 単一値 → `CombatActor.CurrentHp/MaxHp`。
  - 攻撃力・守備力: 相当物は戦闘中ステータス `strength` / `dexterity`（一時、戦闘終了で消滅）のみ。
  - 永続補正: `PassiveModifiers`（レリックの `energyPerTurnBonus` / `cardsDrawnPerTurnBonus` 等）が戦闘開始時に snapshot。
  - 行動力・ドロー: 上記 3-6 のとおりキャラ種別ごとに別フィールド。

---

## 4. 改修案（提案・未承認）

### 4-1. 敵定義（`EnemyDefinition` 拡張、後方互換）

`UnitDefinition` が既に「`Deck` があればカード制御、無ければ旧 Move」の二系統を持つため、同じ方式を敵にも適用する。

```text
EnemyDefinition に optional 追加:
  Deck: IReadOnlyList<UnitDeckEntry>?      // 通常行動デッキ（共通＋固有カードを列挙）
  OpeningActions: IReadOnlyList<...>?      // 先制行動プール（デッキと別）
  InitialEnergy / EnergyPerTurn / DrawPerTurn / HandLimit / AiProfileId
  （能力値は §4-6 確定後に追加）
IsCardControlled => Deck.Count > 0
```

- 34体を一括移行せず、`Deck` を持つ敵だけカード制御にする（段階移行・既存テスト維持）。
- 敵用カードは既存 `CardDefinition`（`Rarity: Token` 等、図鑑コレクション対象外）で定義し、
  共通カードは `enemy_common_*`、固有は `<enemyId>_*` 等の命名で区別（命名は要確認）。
- デッキ構築は当面「手動の固定リスト（`cardId`＋`count`）」とし、自動生成は将来
  `DeckGenerator` を差し込める形（データ側に `deck` を直接書く／生成ルールIDを書くの二択にできる）にする。

### 4-2. 敵参加者

- 敵ごとに `BattleParticipantState`（`ControllerKind=Cpu`）を生成し、デッキ・手札・捨札・エナジー・コンボを持たせる。
- **側（Side）を参加者に持たせる**、または actor の `Side` から判定し、以下を「味方固定」から「actor の Side 基準」に一般化する:
  `CpuCardPlayPolicy`（actor 検索・対象側・定義取得を Unit/Enemy 両対応に）、
  `PlayCardForActor`（caster index）、`ApplyAttack` の Single 対象（敵 caster は味方側の対象）、
  `BattleParticipantAccess` の Slot/投影。
- 敵参加者は **Ready 同期に参加しない**（敵の計画はターン開始処理内で完結）。
- 行動ログ/再生は敵の計画行動を記録するかを別途決める（下記未決）。

### 4-3. 計画タイミング

- ターン開始処理（エナジー加算・ドロー・ターン開始時効果）の後、味方CPU計画の**前**に
  敵CPUを Slot 順に計画し、行動予告を確定させる（プレイヤーは予告を見て行動）。
- 計画は既存 `BattleCpuPlanningCoordinator` の参加者ループを共通利用（コピーしない）。

### 4-4. 敵の予約と実行

- `attack`: 既存どおり actor の `AttackPool` に蓄積し、`EnemyAttackingResolver` で
  プール方式（Single→Random→All）として解決する（Move 方式の per-effect 即時発射から置換）。
- **敵の `buff`/`debuff`**: 予約時は適用せず、新設の「遅延効果キュー」
  （actor または参加者に `ImmutableArray<DeferredCardEffect>`：効果・発生源・順序・対象指定）へ積み、
  敵の実行時に `EffectApplier.Apply` で順に適用する。判定は「caster が敵側 かつ 通常戦闘の予約」の時だけ。
  プレイヤー・CPU味方・先制行動は従来どおり即時。
- `draw` 等の手札系は予約時に即時。
- 行動予告（`CombatIntentProjector`）はカード制御の敵でも `AttackPool`＋遅延効果キューから投影
  （攻撃値は live、バフ/デバフはアイコン）。

### 4-5. 先制行動

- `BattleEngine.Start` で敵 actor 生成後、第1ターン開始処理の前（または後、未決）に
  先制行動プールの各カード効果を `EffectApplier.Apply` で即時解決する（遅延キューを使わない）。
- 山札・手札・捨札・コンボ・エナジーには一切触れない。戦闘中の増援（召喚された敵）にも「登場時」として適用可能な形にする。

### 4-6. 能力値（段階導入案）

- 共通 record `CharacterStats`（Hp, Attack, Defense, EnergyPerTurn, EnergyCap, DrawPerTurn）を
  プレイヤー（`CharacterDefinition`＋`RunState` の永続補正）、Unit、Enemy に持たせ、
  `有効値 = 基礎 + 永続補正 + 一時補正` を計算する純関数を Core に置く。
- 一時補正の保持先として既存 `strength`/`dexterity` を流用するか、新ステータスにするかは未決（§5）。
- 能力値参照効果は「予約時に数値固定しない」必要があるため、`AmountSource`（使用時固定）とは別に、
  `AttackHit` / ブロック予約に「参照能力値＋倍率/加算」を持たせ、実行時に有効値で確定させる方式を想定。

### 4-7. 段階（Phase 分割案）

1. **A: 敵参加者基盤** — Side 一般化、敵参加者生成、CPU 計画共通化、Move 敵との共存。テスト: 既存全テスト維持＋カード制御敵1体。
2. **B: 敵バフ・デバフ遅延キュー＋予告投影**。
3. **C: 先制行動プール**。
4. **D: 図鑑（基礎能力値・行動デッキ・先制行動の別枠表示）**。
5. **E: 能力値体系＋能力値参照カード（通常攻撃・通常防御）**。§5 の回答待ち。
6. **F: 敵データ移行**（敵ごとにデッキ設計。データ作業）。
7. **G: 行動力上限**（プレイヤーへ適用するかは §5 の回答次第）。

---

## 5. 未決事項・確認が必要な矛盾

依頼者が挙げた未決事項（デッキ構築方式、枚数、ドロー枚数の名称・管理、能力値参照の記述方式・計算式、
体力の扱い、能力値上下限、補正の重複・解除、行動力の名称、行動力加算量・上限の設定方法、
先制行動の複数時の順序、先制行動と既存効果処理の整合、敵バフ遅延の具体実装）に加え、
調査で判明した以下を回答待ちとする。**回答前に仕様化しない。**

1. **行動力上限とプレイヤーの現行仕様の矛盾**: 現行プレイヤー/Unit のエナジーは上限なし
   （`current-energy-spec.md`）。上限付き方式をプレイヤーにも適用するか、敵（＋Unit）だけ先行か。
2. **敵のブロックの扱い**: プレイヤー側の `block` は予約時に即時加算される。敵の `block` を
   (a) 予約時即時（プレイヤーと同じ。予告時点から敵がブロックを持つ）、
   (b) バフと同様に実行時反映（次ターンのプレイヤー攻撃を防ぐ現行 Move と同じ時間軸）のどちらにするか。
   現行敵は「実行時にブロック獲得→次のプレイヤー攻撃で消費→次の自分の行動前にリセット」。
3. **敵の実行順**: 遅延バフ/デバフと AttackPool の解決順。カード使用順どおり（例: 筋力アップ→攻撃）か、
   攻撃→バフ等の固定順か。
4. **敵の単体攻撃対象**: 現行敵は「最小 Slot の生存味方」。共通 CPU ロジックの
   「倒せる→HP 少→Slot 小」を敵にも使うか（召喚ユニットが優先的に狙われやすくなる）。
   予約時に固定するか、実行時に選ぶか。
5. **攻撃力・守備力と筋力・敏捷の関係**: 攻撃力＝筋力の基礎値として統合するか、別の能力値として
   筋力とは独立に加算するか（ダメージ式 `基礎値+筋力→脱力→元気` への組み込み位置）。
6. **AI profile**: 敵も `low_cost_combo` を使い Unit と同じ `priority` 補正を持つ前提でよいか。
7. **敵の行動ログ・再生**: 味方 CPU と同様に敵の計画行動もログ／再生 UI の対象にするか。
8. **プレイヤーが敵の手札・山札へ干渉する効果**（敵に捨てさせる等）を将来作るか。作る場合、
   予約済み行動を取り消すか（現行原則は「確定行動は遡及変更しない」）。
9. **敵の手札・山札の公開範囲**: プレイヤーに見せるのは行動予告のみか、手札枚数等も見せるか。
10. **戦闘途中に出現する敵**（召喚・増援）の初期手札と先制行動の発動タイミング。
11. **既存34体の移行方針**: 全移行するか、Move 方式を一部（ボス等）残すか。移行時のバランス維持の基準。
