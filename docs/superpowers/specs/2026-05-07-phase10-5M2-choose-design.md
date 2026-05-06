# Phase 10.5.M2-Choose: UI Card Selection Modal Design

**Status:** Draft (2026-05-07)
**Phase:** 10.5.M2-Choose
**Predecessor:** Phase 10.5.M (M6.9 で `Select="choose"` を 4 actions で random fallback 化、TODO として留め置き)
**Successor:** なし (続く Phase 候補は 10.5.L2 / 10.5.L3 / 10.5.L4 / 10.6.B-Reroll N 回 / Phase 9 multiplayer)

---

## 1. Overview / Goals

カード effect 4 種 (`discard` / `exhaustCard` / `upgrade` / `recoverFromDiscard`) で `Select="choose"` 指定時に **プレイヤーがどのカードを対象にするか UI で選択** できるようにする。現状は random fallback で実装されており、formatter 表示 (例: 「手札を選んで除外」) と engine 動作 (random) が乖離している。

**ゲーム体験の変化:**
- 「叡智の奔流(強化版): 手札を 1 枚選んで除外する」のようなカードがプレイヤーの選択意思を反映するようになる
- 戦略性向上、formatter 表示と engine 動作の整合
- STS 系ローグライクで標準的な「カードを選んで〜」操作感が完成

**スコープ (確定済 Q1〜Q3):**
- Q1: 4 actions 全部対応 (discard / exhaustCard / upgrade / recoverFromDiscard)
- Q2: キャンセル不可 (modal にキャンセルボタン無し、確定後の effect は完了する)
- Q3: 複合 choose 効果は順次 pause (1 カードに choose 2 個ある場合、modal を 2 回連続で出す)

---

## 2. Out of Scope (本 phase 非対応)

- **新 action の choose 対応**: 既存 4 action 以外で choose を追加しない (将来 add 時は同じ枠組みを再利用)
- **カードプレイ前 preview**: 選択前の効果プレビューモーダル (どんな選択肢が出るか事前に見る) は対象外
- **キャンセル機構**: Q2=A 確定により modal キャンセル不可
- **choose 中の relic 発火確認**: OnCardDiscarded / OnCardExhausted 等の発火タイミングは既存と同じ (effect 完了後)
- **タイル選択 / 敵選択** など card 以外の choose: 別 phase
- **マルチプレイ時の同期**: Phase 9 着手時に再検討

---

## 3. Design Decisions (Q1〜Q3 + 付帯)

### Q1. Scope: 4 actions all-in-one ✅

`discard` / `exhaustCard` / `upgrade` / `recoverFromDiscard` の 4 action を 1 phase で対応。

**Why:** 共通の pause/resume 基盤を作るので、4 action 追加コストは「同じ pattern を 4 箇所に書く」だけで分割しても効率変わらず。むしろ「半端実装」状態を残すと UI 混乱。

### Q2. Cancel: 不可 ✅

Modal にキャンセルボタン無し、Esc / 外側クリックでも閉じない (modal blocking)。プレイヤーは必ず N 枚選んで確定する。

**Why:** STS 慣例 + エナジー消費 / OnPlayCard 発火 後の巻き戻しは複雑。誤クリック対策は別レイヤ (将来「カード詳細表示」「長押し確認」等) で対応可能。

### Q3. Multi-effect: 順次 pause ✅

カードに choose 効果が 2 個ある場合、effect[0] で modal 1 → 確定 → 状態更新 → effect[1] で modal 2 → 確定 → 残り適用、と順次に処理。

**Why:** semantically 正しい (各 effect が直前の state を反映)。実装も「PendingChoice を毎回設定→resume」を繰り返すだけで自然。既存 choose カードは全て choose 1 個なので実害なし、将来複合カードも対応済。

### Q4. Pile 別 UI 分岐 ✅ (ユーザー要望)

- `pile == 'hand'` → **Hand mode**: 手札 UI そのまま、選択可能カードを highlight、非選択は dim、N 枚選んで確定
- `pile == 'draw' | 'discard' | 'exhaust'` → **List mode**: カードを縦/グリッドリストで表示、クリックで選択、N 枚で確定

**Why:** 手札からの選択は「プレイ時操作と同じ UI」が直感的、見えない山札/捨札/除外からの選択は「リスト一覧から選ぶ」が必要。

### Q5. Auto-skip ✅ (ユーザー要望)

Effect.Amount >= 候補数 (pile 内カード数) の場合、modal をスキップして全自動選択 (= `select=all` と同等動作)。

**Why:** 「3 枚選べと言われたが手札が 2 枚しかない」場合、UI を出しても無意味。プレイヤー操作を省略。

### Q6. 候補リスト

Pile 内の **全カード** が選択候補。カードタイプ等によるフィルタは現時点では不要 (例: upgrade は強化可能カードに限定する必要があるが、これは `ApplyUpgrade` 内のロジックで処理)。

**Note**: `upgrade` の場合は IsUpgradable && !IsUpgraded のカードのみが候補。これは選択候補リストを返す段階で server 側でフィルタする。

---

## 4. Architecture

### 4-1. Server: BattleState 拡張

**新 record (`src/Core/Battle/State/PendingCardPlay.cs`):**

```csharp
public sealed record PendingCardPlay(
    string CardInstanceId,    // どのカードプレイ中か (BattleCardInstance.InstanceId)
    int EffectIndex,          // どの effect[i] で pause したか
    PendingChoice Choice      // 何を選ばせるか
);

public sealed record PendingChoice(
    string Action,            // "discard"|"exhaustCard"|"upgrade"|"recoverFromDiscard"
    string Pile,              // "hand"|"draw"|"discard" (recoverFromDiscard は "discard" 固定)
    int Count,                // 選ぶ枚数 (= effect.Amount、ただし候補数で clamp 済)
    ImmutableArray<string> CandidateInstanceIds  // 選択可能な BattleCardInstance.InstanceId 一覧
);
```

**`BattleState` に field 追加 (末尾、optional):**
```csharp
PendingCardPlay? PendingCardPlay = null
```

(default null で既存 `new BattleState(...)` 全箇所が無変更で動く。Phase 10.6.B T3 と同パターン。)

**Persistence note:** `BattleState` は Server 側 in-memory のみ (`BattleSession.State`)。RunState には含まれない (RunState には薄い `BattlePlaceholderState` 別 record しか持たない)。**save schema 影響なし**。

### 4-2. Server: BattleEngine.PlayCard 改修

現状の `PlayCard(state, instanceId, rng, catalog) -> (newState, events)` を per-effect state machine に refactor:

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) PlayCard(...)
{
    // 既存 pending があればエラー (resolve 必須)
    if (state.PendingCardPlay is not null)
        throw new InvalidOperationException(
            "Cannot play card while PendingCardPlay is set; resolve first via ResolveCardChoice.");

    // 既存ロジック: validate / energy 消費 / OnPlayCard 発火 / state.Hand 更新
    // ...

    // effect 順次適用
    return ApplyEffectsFrom(state, caster, card, effects, startIndex: 0, rng, catalog);
}

private static (BattleState, IReadOnlyList<BattleEvent>) ApplyEffectsFrom(
    BattleState state, CombatActor caster, BattleCardInstance card,
    IReadOnlyList<CardEffect> effects, int startIndex,
    IRng rng, DataCatalog catalog)
{
    var s = state;
    var events = new List<BattleEvent>();
    for (int i = startIndex; i < effects.Count; i++)
    {
        var eff = effects[i];
        // choose かつ 候補 > Amount → pause
        if (NeedsPlayerChoice(s, eff))
        {
            var pending = new PendingCardPlay(
                CardInstanceId: card.InstanceId,
                EffectIndex: i,
                Choice: BuildPendingChoice(s, eff));
            return (s with { PendingCardPlay = pending }, events);
        }
        // 通常 effect 適用 (random / all / non-choose)
        var (afterEff, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
        s = afterEff;
        events.AddRange(evs);
    }
    // 全 effect 完了 → カード移動 (discard/exhaust pile)
    s = FinalizeCardPlay(s, card);
    return (s, events);
}
```

### 4-3. Server: BattleEngine.ResolveCardChoice 新メソッド

```csharp
public static (BattleState, IReadOnlyList<BattleEvent>) ResolveCardChoice(
    BattleState state, ImmutableArray<string> selectedInstanceIds,
    IRng rng, DataCatalog catalog)
{
    var pending = state.PendingCardPlay
        ?? throw new InvalidOperationException("No PendingCardPlay to resolve");

    // validate selection
    if (selectedInstanceIds.Length != pending.Choice.Count)
        throw new InvalidOperationException(
            $"Expected {pending.Choice.Count} selections, got {selectedInstanceIds.Length}");
    foreach (var id in selectedInstanceIds)
    {
        if (!pending.Choice.CandidateInstanceIds.Contains(id))
            throw new InvalidOperationException($"Selected '{id}' not in candidates");
    }

    // 該当 effect を選択で適用
    var card = FindCard(state, pending.CardInstanceId)
        ?? throw new InvalidOperationException("Pending card not found in state");
    var effects = GetCardEffects(card, catalog);
    var pendingEffect = effects[pending.EffectIndex];

    // PendingChoice → 選択済 instance ids でカード操作
    var s = state with { PendingCardPlay = null };  // pending クリア
    var caster = s.Allies.First(a => a.DefinitionId == "hero");
    var (afterPick, pickEvents) = ApplyChoseEffect(s, caster, pendingEffect, selectedInstanceIds, rng, catalog);
    var allEvents = new List<BattleEvent>(pickEvents);

    // 残り effect 続行 (再 pause する場合あり)
    var (final, restEvents) = ApplyEffectsFrom(afterPick, caster, card, effects, pending.EffectIndex + 1, rng, catalog);
    allEvents.AddRange(restEvents);
    return (final, allEvents);
}
```

### 4-4. Server: 4 actions の choose detector + applier

各 ApplyXxx メソッド (現在 random fallback) を 2 つに分離:

1. **`NeedsPlayerChoice(state, effect)`**: choose かつ candidates > Amount を判定
2. **`ApplyChoseEffect(state, caster, effect, selectedIds, rng, catalog)`**: 選択済 ID で対象を operate

`discard` / `exhaustCard` / `upgrade` / `recoverFromDiscard` の各 ApplyXxx に分岐ロジック追加。auto-skip (candidates ≤ Amount) は既存 random / all 経路に流し、choose 必要時のみ pending emit。

### 4-5. Server: Endpoint

新 endpoint `POST /api/v1/runs/current/battle/resolve-card-choice`:

**Request body:**
```json
{ "selectedInstanceIds": ["card_inst_5", "card_inst_8"] }
```

**Response:** 既存 PlayCard と同じ shape (新 BattleState DTO + events)

**Server 実装:**
```csharp
[HttpPost("battle/resolve-card-choice")]
public async Task<IActionResult> ResolveCardChoice(
    [FromBody] ResolveCardChoiceRequestDto body, CancellationToken ct)
{
    // session 取得
    if (!_sessions.TryGet(accountId, out var session))
        return BadRequest("No active battle session");
    try
    {
        var (newState, events) = BattleEngine.ResolveCardChoice(
            session.State, body.SelectedInstanceIds.ToImmutableArray(),
            session.Rng, _data);
        _sessions.Update(accountId, newState);
        return Ok(MapToResponse(newState, events));
    }
    catch (InvalidOperationException ex)
    {
        return BadRequest(ex.Message);
    }
}
```

### 4-6. Client: DTO + State

`BattleStateDto` に `pendingCardPlay?: PendingCardPlayDto` field 追加:
```typescript
type PendingCardPlayDto = {
  cardInstanceId: string
  effectIndex: number
  choice: {
    action: 'discard' | 'exhaustCard' | 'upgrade' | 'recoverFromDiscard'
    pile: 'hand' | 'draw' | 'discard'
    count: number
    candidateInstanceIds: string[]
  }
}
```

### 4-7. Client: CardChoiceModal

新 component `src/Client/src/screens/battleScreen/CardChoiceModal.tsx`:

- 入力: `pendingCardPlay`, `state.Hand` / `state.DrawPile` / `state.DiscardPile` (DTO)
- pile==hand: 既存 `<Hand>` コンポーネント風の表示、選択可能カードを枠ハイライト、選択済を太枠、非選択カードを dim
- pile==draw/discard: カードリスト表示 (縦並び or グリッド、5列くらい)
- N 枚選んだら「確定」ボタン enable → クリックで `POST resolve-card-choice` → 受信 state で再描画
- modal は **closeOnEsc=false / footerAlign="center"** で blocking

### 4-8. Client: BattleScreen 統合

`BattleScreen.tsx` で `state.pendingCardPlay` を check:
- non-null → `<CardChoiceModal>` を表示
- それ以外の battle UI (手札クリック等) は disabled (modal が overlay でブロックする)

PlayCard の response が pending state を返した時、自動的に modal が出る (state を mainstate に書き戻すだけ)。

---

## 5. Auto-skip (Q5)

`NeedsPlayerChoice` 内で:
```csharp
private static bool NeedsPlayerChoice(BattleState state, CardEffect effect)
{
    if (effect.Select != "choose") return false;
    var candidates = GetCandidateCount(state, effect);
    return candidates > effect.Amount;  // candidates <= Amount → 自動全選択
}
```

`candidates > Amount` のときのみ pending を emit。逆 (≤) は既存ロジック (`select == "all"` の枠組み) で全自動適用。

### 各 action の `GetCandidateCount` ロジック

| Action | Pile | Candidate filter |
|---|---|---|
| `discard` | hand 固定 | `state.Hand.Length` |
| `exhaustCard` | hand/draw/discard | `state.<pile>.Length` |
| `upgrade` | hand/draw/discard | pile 内で `IsUpgraded == false && def.IsUpgradable` の数 |
| `recoverFromDiscard` | discard 固定 | `state.DiscardPile.Length` |

`upgrade` のみ「強化可能」フィルタが入る — auto-skip 判定でも、PendingChoice の candidates 構築でも、同じフィルタを適用。

---

## 6. Edge Cases

| ケース | 挙動 |
|---|---|
| ResolveCardChoice 呼出時 PendingCardPlay = null | 400 BadRequest |
| selectedInstanceIds.Length != Choice.Count | 400 BadRequest |
| selectedInstanceIds に candidates 外 ID | 400 BadRequest |
| PlayCard 呼出時 PendingCardPlay != null | 400 BadRequest ("既存 pending を resolve してから") |
| PendingCardPlay 中に EndTurn | 400 BadRequest (pending 残り) — UI 側で EndTurn ボタン disabled |
| pile が空 + choose | candidates=0 → auto-skip (no-op、既存挙動) |
| 複合 choose カード: 2 個目で pile 空 | 2 つ目は auto-skip、1 つ目は modal で選択完了済 |
| カード移動中 (例: cleanup phase) に PendingCardPlay 残り | bug、ResolveCardChoice 経由でしか pending クリアされない設計で防止 |

---

## 7. Migration / Schema Versioning

- `BattleState.PendingCardPlay = null` (default) で全既存 instantiation 互換
- save schema 影響なし (BattleState は in-memory のみ)
- 既存 35 枚のカード JSON 影響なし (formatter 表示は既に「選んで」で OK、engine 挙動だけ変わる)

---

## 8. Testing Strategy

| Layer | Coverage | 期待件数 |
|---|---|---|
| **Core: PlayCard pause** | 各 action × choose で pending emit、pile 種別ごとの candidates 構築 | 8〜10 |
| **Core: ResolveCardChoice** | 成功 / candidates 外 / count 不一致 / pending null / 残り effect 続行 / 複合 choose | 6〜8 |
| **Core: auto-skip** | candidates ≤ Amount で pending 出さず random/all 経路に流れる | 4 |
| **Core: regression** | 既存 random / all / non-choose effect は無変更で動く (既存テスト通過) | 0 (既存 test pass 確認のみ) |
| **Server: endpoint** | resolve-card-choice 成功 / 各 error path / 統合 (PlayCard → pending → ResolveChoice → 完了) | 5 |
| **Client: CardChoiceModal** | hand mode 表示 / list mode 表示 / N 枚選択 / 確定ボタン enable / dispatch | 6 |
| **合計** | | **~30 件** |

---

## 9. Sub-task Breakdown

| # | Task | 主な変更ファイル | 依存 |
|---|---|---|---|
| **T1** | `PendingCardPlay` / `PendingChoice` record 定義 + `BattleState.PendingCardPlay` field 追加 | `BattleState.cs`, 新 `PendingCardPlay.cs` | — |
| **T2** | `BattleEngine.PlayCard` を per-effect state machine に refactor (pause-resume 基盤、`ApplyEffectsFrom` 抽出) | `BattleEngine.PlayCard.cs` | T1 |
| **T3** | 4 ApplyXxx メソッド (`discard` / `exhaustCard` / `upgrade` / `recoverFromDiscard`) で `NeedsPlayerChoice` 判定 + pending emit | `EffectApplier.cs` | T1, T2 |
| **T4** | `BattleEngine.ResolveCardChoice` 新メソッド + `ApplyChoseEffect` per-action helper | `BattleEngine.cs`, `EffectApplier.cs` | T3 |
| **T5** | Server endpoint `POST /battle/resolve-card-choice` + DTO mapping (`PendingCardPlayDto`) | `BattleController.cs`, `BattleStateDtoMapper.cs` | T4 |
| **T6** | Client `BattleStateDto.pendingCardPlay` 型定義 + adapter | `types.ts`, `dtoAdapter.ts` | T5 |
| **T7** | Client `CardChoiceModal` Hand mode (手札選択 UI) | 新 `CardChoiceModal.tsx`, BattleScreen integration | T6 |
| **T8** | Client `CardChoiceModal` List mode (draw/discard 選択 UI) | 同上 (mode 分岐追加) | T7 |
| **T9** | 統合テスト + push + memory 更新 | tests + memory | T1〜T8 |

### 推奨実装順

`T1 → T2 → T3 (4 actions 並列可) → T4 → T5 → T6 → T7 → T8 → T9`

T3 の 4 actions は independent なので分割可能だが、同じ pattern の繰り返しなので 1 task 内で 4 つ書く方が効率的。

### 推定コスト

- 9 task 想定、推定 ~30 test 追加
- ~10〜12 時間 (Phase 10.6.B と同程度)
- touch ファイル: ~12 (Core 4 + Server 2 + Client 4 + tests)

---

## 10. Future / Out-of-Scope (将来検討)

- **複合 choose の UI 改善**: 2 つ目の modal が出る時に「先程選んだものが反映された」ことを 視覚的に示す (例: 直前の操作のアニメーション、pile の更新表示)
- **キャンセル機能の後付け**: ユーザ要望次第で B 案 (effect だけスキップ) を別 phase で
- **「up to N 枚」モード**: 「最大 N 枚選んでよい」(0〜N) — 現状は exact N。STS の Calculated Gamble 系で使う
- **Multi-player choose 同期**: Phase 9 (multiplayer) 着手時に「他プレイヤーの choose 中は待機」設計が必要
- **AI 選択シミュレーション**: 自動プレイ時の選択ロジック (スマートカードゲーム研究)
