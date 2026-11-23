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

### 2-2a. 確認回答による確定事項（2026-11-23）

- **行動力上限**: 設けない。現行のエナジー仕様（上限なし・持越し・`int.MaxValue` 飽和）を
  プレイヤー・CPU味方・敵で共通利用する。§2-5 の「上限付き加算方式」は採用しない。
- **敵の予約＝予測のみ**: 敵のカード使用時は「行動したらこうなる」という予測だけを作り、
  攻撃・ブロック・バフ・デバフ等の戦場への効果は**プレイヤー側の行動終了後（敵の行動時）に実行**する。
  予告にはブロック・バフ・デバフも「予定」として表示するが、実際には処理されていない。
  予約時に即時処理するのは手札・山札系（ドロー等）、コスト支払い、カード移動、コンボ判定のみ。
- **敵の実行順**: カードを使う順番は CPU 行動アルゴリズムに従う。実行時は、後から使ったバフも
  攻撃の前に反映される（プレイヤーの攻撃が解決時に筋力を再計算するのと同じ）。
  結果として **非攻撃効果（バフ・デバフ・ブロック等、使用順）→ 攻撃（使用順）** の順で処理する。
- **敵の単体攻撃対象**: 現行ルール（行動開始時の生存味方で最小 Slot、途中死亡で残りは不発）を維持。
  敵の攻撃は基本的にランダム・全体攻撃を想定。
- **攻撃力＝筋力、守備力＝敏捷**: プレイヤー・敵とも同一の能力値として扱う。
  攻撃力の有効値がダメージ式の「基礎値＋筋力」の筋力に、守備力がブロックの敏捷に相当する。
- **既存 34 体は全員カード化する**。移行完了後、敵の Move 方式は廃止する
  （召喚ユニットの旧 Move 互換は別途判断）。

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
- 行動力はカードコスト分消費、ターン開始時に規定量を加算、未消費分は持ち越し、不足時はカード使用不可。
  上限は設けない（§2-2a）。

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

## 4. 実装状況と残りの段階

### 4-1. 実装済み（2026-11-23） → `current-enemy-card-ai-spec.md`

- **A: 敵参加者基盤**: `EnemyDefinition.Deck` 等（Unit と同形式、Move と共存）、
  `BattleState.EnemyParticipants`、`EnemyCardPlanner`（ターン開始処理・計画）、
  `CpuCardPlayPolicy` の比較核を敵と共通化（`ChooseNextForEnemy`）、
  `PlayCardForActor` を敵 caster に一般化（コスト・コンボ・カード移動を共通利用）。
- **B: 予測のみの予約と敵行動時の実行**: 戦場効果は `CombatActor.PlannedEffects` へ予約し、
  `EnemyAttackingResolver` で「非攻撃 → 攻撃」の順に実行。予告は `CombatIntentProjector` が投影。
- 既存 34 体のデータはまだ Move のまま（§4-2 F）。

### 4-2. 残りの段階

| 段階 | 内容 | 状態 |
|---|---|---|
| C | 先制行動プール | **実装済み**（current §7。複数時の順序は暫定で記載順） |
| D | 図鑑: 基礎能力値・行動デッキ・先制行動の別枠表示。プレイヤー・味方の攻撃力/守備力も掲載 | 一部（カタログ API に敵の能力値・デッキ・先制行動を追加、敵タイルのツールチップに攻撃力/守備力）。詳細パネルは未着手 |
| E | 能力値: 基礎攻撃力・守備力＋能力値参照（`selfAttack` / `selfDefense`） | **基礎値と参照は実装済み**（current §8）。永続補正・一時補正は未着手 |
| F | 既存 34 体のカード化 | **自動生成で実装済み**（current §9）。手動調整待ち |
| — | 敵の行動ログ・再生 UI への表示 | §5 の回答待ち |

### 4-3. 能力値の方針（確定分のみ）

- 攻撃力・守備力は筋力・敏捷とは**別の能力値**で、敵・味方・プレイヤーがそれぞれ初期値を持ち、
  全て図鑑に載せる（2026-11-23 回答で更新）。攻撃力参照のダメージは攻撃力 0 なら 0、そこに筋力が加わる。
- 行動力（エナジー）は上限なし。
- 能力値参照効果は使用時に数値を固定せず、実行時の有効値を参照する
  （既存 `AmountSource` は使用時固定なので別の仕組みにする）。

---

## 5. 未決事項（回答待ち。独自に仕様化しない）

（2026-11-23 回答済み: デッキは自動生成→手動調整、攻撃力・守備力は筋力・敏捷と別の初期値、
攻撃力参照は攻撃力そのもの＋筋力、先制行動は最初の手札の後・全処理の前。）

（同日 追加回答済み: 倍率カードを作る＝`amountScalePercent`・強攻撃、プレイヤー・味方の初期値と
初期デッキの能力値参照化はお任せ（current §10）、敵の行動も再生可能にする（current §11）。）

1. **固有カードの能力値参照化**: 旧 Move を写した固有カードは固定値のまま。倍率付きの能力値参照へ置き換えるか。
2. **強攻撃をどの敵デッキへ入れるか**（現在エナジー毎ターン1・全カード1コストのため、2コストは溜まった時だけ使われる）。
3. **戦闘途中に出現する敵**の先制行動・初期手札の扱い。
5. **永続補正と一時補正**の重複・解除、能力値の上下限、体力（最大／現在）の扱い。
6. **毎ターンドロー枚数**の名称（知力等）と管理方法。
7. **先制行動**: 複数時の実行順、第 1 ターンの開始処理（毒・再生等）の前か後か、
   戦闘途中に出現する敵の扱い、既存効果処理（即時適用）との整合。
8. **敵の行動ログ・再生**: 味方 CPU と同様に敵の計画行動を再生 UI に出すか。
9. **プレイヤーから敵の手札・山札への干渉効果**を作るか。作る場合、予約済み行動を取り消すか
   （現行原則は「確定行動は遡及変更しない」）。
10. **敵の手札・山札の公開範囲**（予告のみか、手札枚数等も見せるか）。
11. **敵カードで未対応の効果**: 選択効果・召喚・パワー・特性等を敵に使わせるか。
