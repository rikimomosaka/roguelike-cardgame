# Phase 0 — JSON データ定義と Core データモデル 実装計画

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** カード／レリック／ポーション／モンスターのマスター定義を JSON で管理し、Core の純粋関数で読み込める状態にする。以降のフェーズで使う「データ層の土台」を作る。

**Architecture:** Core 内に `Data/` ディレクトリを追加し、各 Definition を `record` で定義。`DataCatalog.LoadFromStrings(...)` にカード／レリック／ポーション／敵の JSON 文字列辞書を渡すと、ID をキーとした Definition 辞書が返る純粋関数として実装する。ファイル I/O は Core の外で行い、Core は「文字列 → モデル」のみ担う（Udon# 互換性担保）。

**Tech Stack:** C# .NET 10 (`System.Text.Json`), xUnit, record 型, 日本語 XML ドキュメントコメント。

**前提 — このフェーズでの範囲:**
- JSON スキーマと C# モデル、パーサ、最小限のシードデータ（初期デッキの Strike / Defend、ごく少数のレリック・ポーション・敵）を用意する。
- 効果（`effects` 配列）の「実行」は実装しない。あくまでデータとして保持できるだけで良い。
- ファイル読み込みは Server 側で後のフェーズに実装する。本フェーズでは JSON 文字列を直接渡してテストする。

---

## ファイル構成（このフェーズで新規作成／変更するもの）

**新規作成:**
- `src/Core/Cards/CardRarity.cs`
- `src/Core/Cards/CardType.cs`
- `src/Core/Cards/CardEffect.cs`
- `src/Core/Cards/CardDefinition.cs`
- `src/Core/Cards/CardJsonLoader.cs`
- `src/Core/Relics/RelicTrigger.cs`
- `src/Core/Relics/RelicDefinition.cs`
- `src/Core/Relics/RelicJsonLoader.cs`
- `src/Core/Potions/PotionDefinition.cs`
- `src/Core/Potions/PotionJsonLoader.cs`
- `src/Core/Enemy/EnemyPool.cs`
- `src/Core/Enemy/EnemyDefinition.cs`
- `src/Core/Enemy/EnemyJsonLoader.cs`
- `src/Core/Data/DataCatalog.cs`
- `src/Core/Data/Cards/strike.json`
- `src/Core/Data/Cards/defend.json`
- `src/Core/Data/Relics/burning_blood.json`（入手時効果サンプル）
- `src/Core/Data/Relics/lantern.json`（永続効果サンプル）
- `src/Core/Data/Potions/block_potion.json`
- `src/Core/Data/Potions/fire_potion.json`
- `src/Core/Data/Enemies/jaw_worm.json`（act1 弱プール）
- `src/Core/Data/Enemies/louse_red.json`（act1 弱プール）
- `src/Core/Data/Enemies/gremlin_nob.json`（act1 エリート）
- `src/Core/Data/Enemies/hexaghost.json`（act1 ボス）
- `tests/Core.Tests/Cards/CardJsonLoaderTests.cs`
- `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs`
- `tests/Core.Tests/Potions/PotionJsonLoaderTests.cs`
- `tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs`
- `tests/Core.Tests/Data/DataCatalogTests.cs`
- `tests/Core.Tests/Fixtures/JsonFixtures.cs`（テスト用 JSON 文字列）

**変更:**
- `src/Core/Core.csproj` — JSON 埋め込みリソース設定（後で使う）。本フェーズでは参照だけ追加。
- `tests/Core.Tests/Core.Tests.csproj` — `Microsoft.NET.Test.Sdk` と xUnit の参照確認。
- `tests/Core.Tests/UnitTest1.cs` — 既存のダミーテストを削除。

---

## Task 1 — リポジトリ整備 & 既存テンプレ掃除

**Files:**
- Delete: `tests/Core.Tests/UnitTest1.cs`
- Modify: `src/Core/Core.csproj`
- Modify: `tests/Core.Tests/Core.Tests.csproj`

- [ ] **Step 1: 既存のダミーテストを確認して削除**

Run: `ls tests/Core.Tests/UnitTest1.cs && cat tests/Core.Tests/UnitTest1.cs`
Expected: 中身は xUnit のテンプレ。削除して問題ないことを確認してから `git rm tests/Core.Tests/UnitTest1.cs`。

- [ ] **Step 2: Core.csproj に RootNamespace と DefaultNamespace を明示する**

`src/Core/Core.csproj` の `<PropertyGroup>` に追記:

```xml
<RootNamespace>RoguelikeCardGame.Core</RootNamespace>
<AssemblyName>RoguelikeCardGame.Core</AssemblyName>
```

これにより埋め込みリソース名が `RoguelikeCardGame.Core.Data.Cards.strike.json` 形式になり、Task 13 の EmbeddedDataLoader が期待どおり動く。.NET 10 は `System.Text.Json` を標準で含むので追加パッケージは不要。

- [ ] **Step 3: Core.Tests.csproj は既に Core 参照済み**

`tests/Core.Tests/Core.Tests.csproj` は `<ProjectReference Include="..\..\src\Core\Core.csproj" />` を持つ。`FluentAssertions` は導入せず xUnit の `Assert` のみで書く方針（Udon# 移植を想定した軽量依存）。テストアセンブリの名前空間は `RoguelikeCardGame.Core.Tests` に揃える（後続タスクのテストファイルで使用）。`Core.Tests.csproj` に以下を追加:

```xml
<RootNamespace>RoguelikeCardGame.Core.Tests</RootNamespace>
```

- [ ] **Step 4: ビルドとテストを実行しベースラインを確認**

Run: `dotnet build && dotnet test`
Expected: ビルド成功。テストはゼロ件で成功する（空のテストプロジェクト状態）。

- [ ] **Step 5: コミット**

```bash
git add tests/Core.Tests/ src/Core/Core.csproj
git commit -m "chore: set root namespaces and clean up xUnit template for Phase 0"
```

---

## Task 2 — CardRarity と CardType 列挙型

**Files:**
- Create: `src/Core/Cards/CardRarity.cs`
- Create: `src/Core/Cards/CardType.cs`
- Create: `tests/Core.Tests/Cards/CardEnumTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/CardEnumTests.cs` を新規作成:

```csharp
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardEnumTests
{
    [Fact]
    public void CardRarity_HasFiveMembers_ZeroIsPromo()
    {
        Assert.Equal(0, (int)CardRarity.Promo);
        Assert.Equal(1, (int)CardRarity.Common);
        Assert.Equal(2, (int)CardRarity.Rare);
        Assert.Equal(3, (int)CardRarity.Epic);
        Assert.Equal(4, (int)CardRarity.Legendary);
    }

    [Fact]
    public void CardType_HasUnitAttackSkillPower()
    {
        var names = System.Enum.GetNames(typeof(CardType));
        Assert.Contains("Unit", names);
        Assert.Contains("Attack", names);
        Assert.Contains("Skill", names);
        Assert.Contains("Power", names);
        Assert.Equal(4, names.Length);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~CardEnumTests"`
Expected: コンパイルエラー `CardRarity, CardType が見つからない`。

- [ ] **Step 3: CardRarity.cs を実装**

`src/Core/Cards/CardRarity.cs`:

```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードのレアリティ。JSON では整数として保存する。</summary>
public enum CardRarity
{
    Promo = 0,
    Common = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
}
```

- [ ] **Step 4: CardType.cs を実装**

`src/Core/Cards/CardType.cs`:

```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードの種別。</summary>
public enum CardType
{
    Unit,
    Attack,
    Skill,
    Power,
}
```

- [ ] **Step 5: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~CardEnumTests"`
Expected: 2 件成功。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Cards/CardRarity.cs src/Core/Cards/CardType.cs tests/Core.Tests/Cards/CardEnumTests.cs
git commit -m "feat(core): add CardRarity and CardType enums"
```

---

## Task 3 — CardEffect プリミティブ（抽象基底＋最初の具象）

**Files:**
- Create: `src/Core/Cards/CardEffect.cs`
- Create: `tests/Core.Tests/Cards/CardEffectTests.cs`

**設計メモ:** `CardEffect` は抽象 `abstract record CardEffect(string Type)` とし、`DamageEffect` / `GainBlockEffect` / `UnknownEffect` の具象を持たせる。後続フェーズで効果種別が増えるため、パーサは「未知の type は UnknownEffect としてそのまま保持」する方針にする（JSON スキーマに後方互換性を持たせるため）。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/CardEffectTests.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardEffectTests
{
    [Fact]
    public void DamageEffect_HasAmount()
    {
        var e = new DamageEffect(6);
        Assert.Equal("damage", e.Type);
        Assert.Equal(6, e.Amount);
    }

    [Fact]
    public void GainBlockEffect_HasAmount()
    {
        var e = new GainBlockEffect(5);
        Assert.Equal("gainBlock", e.Type);
        Assert.Equal(5, e.Amount);
    }

    [Fact]
    public void UnknownEffect_PreservesRawType()
    {
        var e = new UnknownEffect("summonUnit");
        Assert.Equal("summonUnit", e.Type);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~CardEffectTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: CardEffect.cs を実装**

`src/Core/Cards/CardEffect.cs`:

```csharp
namespace RoguelikeCardGame.Core.Cards;

/// <summary>カード効果の基底。Type は JSON の "type" フィールドに対応。</summary>
public abstract record CardEffect(string Type);

/// <summary>ターゲットにダメージを与える。</summary>
public sealed record DamageEffect(int Amount) : CardEffect("damage");

/// <summary>自分にブロックを得る。</summary>
public sealed record GainBlockEffect(int Amount) : CardEffect("gainBlock");

/// <summary>未知／将来拡張の効果。Type 文字列のみ保持する。</summary>
public sealed record UnknownEffect(string TypeName) : CardEffect(TypeName);
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~CardEffectTests"`
Expected: 3 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/CardEffect.cs tests/Core.Tests/Cards/CardEffectTests.cs
git commit -m "feat(core): add CardEffect primitives (damage, gainBlock, unknown)"
```

---

## Task 4 — CardDefinition レコード

**Files:**
- Create: `src/Core/Cards/CardDefinition.cs`
- Create: `tests/Core.Tests/Cards/CardDefinitionTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Cards/CardDefinitionTests.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardDefinitionTests
{
    [Fact]
    public void Strike_BasicShape()
    {
        var def = new CardDefinition(
            Id: "strike",
            Name: "ストライク",
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Attack,
            Cost: 1,
            Effects: new List<CardEffect> { new DamageEffect(6) },
            UpgradedEffects: new List<CardEffect> { new DamageEffect(9) });

        Assert.Equal("strike", def.Id);
        Assert.Null(def.DisplayName);
        Assert.Equal(1, def.Cost);
        Assert.Single(def.Effects);
        Assert.NotNull(def.UpgradedEffects);
    }

    [Fact]
    public void UnplayableCard_HasNullCost()
    {
        var def = new CardDefinition(
            Id: "curse_doubt",
            Name: "Doubt",
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Skill,
            Cost: null,
            Effects: new List<CardEffect>(),
            UpgradedEffects: null);

        Assert.Null(def.Cost);
        Assert.Null(def.UpgradedEffects);
    }

    [Fact]
    public void DisplayName_CanOverrideName()
    {
        var def = new CardDefinition(
            Id: "strike_promo_anniversary",
            Name: "ストライク",
            DisplayName: "ストライク(周年記念)",
            Rarity: CardRarity.Promo,
            CardType: CardType.Attack,
            Cost: 1,
            Effects: new List<CardEffect> { new DamageEffect(6) },
            UpgradedEffects: null);

        Assert.Equal("ストライク(周年記念)", def.DisplayName);
        Assert.Equal("ストライク", def.Name);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~CardDefinitionTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: CardDefinition.cs を実装**

`src/Core/Cards/CardDefinition.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードのマスター定義。JSON から読み込まれる不変データ。</summary>
/// <param name="Id">一意の英数字 ID（内部参照用）。</param>
/// <param name="Name">カード名。</param>
/// <param name="DisplayName">表示名。プロモ・スキン違いで差し替える任意項目。null なら Name を表示。</param>
/// <param name="Rarity">レアリティ。</param>
/// <param name="CardType">カード種別。</param>
/// <param name="Cost">プレイコスト。null はプレイ不可（条件付き起動等を表現）。</param>
/// <param name="Effects">効果プリミティブ配列。</param>
/// <param name="UpgradedEffects">強化時の効果配列。null なら強化不可。</param>
public sealed record CardDefinition(
    string Id,
    string Name,
    string? DisplayName,
    CardRarity Rarity,
    CardType CardType,
    int? Cost,
    IReadOnlyList<CardEffect> Effects,
    IReadOnlyList<CardEffect>? UpgradedEffects);
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~CardDefinitionTests"`
Expected: 3 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Cards/CardDefinition.cs tests/Core.Tests/Cards/CardDefinitionTests.cs
git commit -m "feat(core): add CardDefinition record"
```

---

## Task 5 — CardJsonLoader（JSON 文字列 → CardDefinition）

**Files:**
- Create: `src/Core/Cards/CardJsonLoader.cs`
- Create: `tests/Core.Tests/Fixtures/JsonFixtures.cs`
- Create: `tests/Core.Tests/Cards/CardJsonLoaderTests.cs`

- [ ] **Step 1: テスト用 JSON フィクスチャを用意する**

`tests/Core.Tests/Fixtures/JsonFixtures.cs`:

```csharp
namespace RoguelikeCardGame.Core.Tests.Fixtures;

public static class JsonFixtures
{
    public const string StrikeJson = """
    {
      "id": "strike",
      "name": "ストライク",
      "rarity": 1,
      "cardType": "Attack",
      "cost": 1,
      "effects": [ { "type": "damage", "amount": 6 } ],
      "upgradedEffects": [ { "type": "damage", "amount": 9 } ]
    }
    """;

    public const string DefendJson = """
    {
      "id": "defend",
      "name": "防御",
      "rarity": 1,
      "cardType": "Skill",
      "cost": 1,
      "effects": [ { "type": "gainBlock", "amount": 5 } ],
      "upgradedEffects": [ { "type": "gainBlock", "amount": 8 } ]
    }
    """;

    public const string StrikePromoJson = """
    {
      "id": "strike_promo_anniversary",
      "name": "ストライク",
      "displayName": "ストライク(周年記念)",
      "rarity": 0,
      "cardType": "Attack",
      "cost": 1,
      "effects": [ { "type": "damage", "amount": 6 } ]
    }
    """;

    public const string UnplayableCurseJson = """
    {
      "id": "curse_doubt",
      "name": "Doubt",
      "rarity": 1,
      "cardType": "Skill",
      "cost": null,
      "effects": []
    }
    """;

    public const string UnknownEffectJson = """
    {
      "id": "mystery",
      "name": "謎の一撃",
      "rarity": 2,
      "cardType": "Attack",
      "cost": 2,
      "effects": [ { "type": "summonUnit", "unitId": "imp" } ]
    }
    """;

    public const string BrokenJson = """
    { "id": "strike", "name": "ストライク" 
    """;
}
```

- [ ] **Step 2: 失敗テストを書く**

`tests/Core.Tests/Cards/CardJsonLoaderTests.cs`:

```csharp
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardJsonLoaderTests
{
    [Fact]
    public void ParseStrike_FillsAllFields()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.StrikeJson);

        Assert.Equal("strike", def.Id);
        Assert.Equal("ストライク", def.Name);
        Assert.Null(def.DisplayName);
        Assert.Equal(CardRarity.Common, def.Rarity);
        Assert.Equal(CardType.Attack, def.CardType);
        Assert.Equal(1, def.Cost);

        var dmg = Assert.IsType<DamageEffect>(def.Effects.Single());
        Assert.Equal(6, dmg.Amount);

        Assert.NotNull(def.UpgradedEffects);
        var upDmg = Assert.IsType<DamageEffect>(def.UpgradedEffects!.Single());
        Assert.Equal(9, upDmg.Amount);
    }

    [Fact]
    public void ParseDefend_ParsesGainBlock()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.DefendJson);
        var eff = Assert.IsType<GainBlockEffect>(def.Effects.Single());
        Assert.Equal(5, eff.Amount);
    }

    [Fact]
    public void ParseDisplayName_WhenProvided()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.StrikePromoJson);
        Assert.Equal("ストライク(周年記念)", def.DisplayName);
        Assert.Equal(CardRarity.Promo, def.Rarity);
        Assert.Null(def.UpgradedEffects);
    }

    [Fact]
    public void ParseUnplayableCard_CostIsNull()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.UnplayableCurseJson);
        Assert.Null(def.Cost);
        Assert.Empty(def.Effects);
    }

    [Fact]
    public void UnknownEffectType_IsPreservedAsUnknownEffect()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.UnknownEffectJson);
        var eff = Assert.IsType<UnknownEffect>(def.Effects.Single());
        Assert.Equal("summonUnit", eff.Type);
    }

    [Fact]
    public void BrokenJson_ThrowsCardJsonException()
    {
        Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(JsonFixtures.BrokenJson));
    }
}
```

- [ ] **Step 3: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~CardJsonLoaderTests"`
Expected: コンパイルエラー（`CardJsonLoader`, `CardJsonException` が無い）。

- [ ] **Step 4: 実装**

`src/Core/Cards/CardJsonLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>カード JSON のパース失敗を表す例外。</summary>
public sealed class CardJsonException : Exception
{
    public CardJsonException(string message) : base(message) { }
    public CardJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>カード JSON 文字列を CardDefinition に変換する純粋関数群。</summary>
public static class CardJsonLoader
{
    public static CardDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new CardJsonException("カード JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            var id = GetRequiredString(root, "id");
            var name = GetRequiredString(root, "name");
            string? displayName = root.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String
                ? dn.GetString() : null;
            var rarity = (CardRarity)GetRequiredInt(root, "rarity");
            var cardType = ParseCardType(GetRequiredString(root, "cardType"));
            int? cost = root.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Number
                ? costEl.GetInt32() : (int?)null;

            var effects = ParseEffects(root, "effects");
            IReadOnlyList<CardEffect>? upgraded = root.TryGetProperty("upgradedEffects", out _)
                ? ParseEffects(root, "upgradedEffects")
                : null;

            return new CardDefinition(id, name, displayName, rarity, cardType, cost, effects, upgraded);
        }
    }

    private static IReadOnlyList<CardEffect> ParseEffects(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<CardEffect>();

        var list = new List<CardEffect>();
        foreach (var el in arr.EnumerateArray())
            list.Add(ParseEffect(el));
        return list;
    }

    private static CardEffect ParseEffect(JsonElement el)
    {
        var type = GetRequiredString(el, "type");
        return type switch
        {
            "damage" => new DamageEffect(GetRequiredInt(el, "amount")),
            "gainBlock" => new GainBlockEffect(GetRequiredInt(el, "amount")),
            _ => new UnknownEffect(type),
        };
    }

    private static CardType ParseCardType(string s) => s switch
    {
        "Unit" => CardType.Unit,
        "Attack" => CardType.Attack,
        "Skill" => CardType.Skill,
        "Power" => CardType.Power,
        _ => throw new CardJsonException($"未知の cardType: {s}"),
    };

    private static string GetRequiredString(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            throw new CardJsonException($"必須フィールド \"{key}\" (string) がありません。");
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
            throw new CardJsonException($"必須フィールド \"{key}\" (number) がありません。");
        return v.GetInt32();
    }
}
```

- [ ] **Step 5: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~CardJsonLoaderTests"`
Expected: 6 件すべて成功。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Cards/CardJsonLoader.cs tests/Core.Tests/Cards/CardJsonLoaderTests.cs tests/Core.Tests/Fixtures/JsonFixtures.cs
git commit -m "feat(core): add CardJsonLoader with unknown-effect passthrough"
```

---

## Task 6 — RelicDefinition と RelicTrigger

**Files:**
- Create: `src/Core/Relics/RelicTrigger.cs`
- Create: `src/Core/Relics/RelicDefinition.cs`
- Create: `tests/Core.Tests/Relics/RelicDefinitionTests.cs`

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Relics/RelicDefinitionTests.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Relics;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicDefinitionTests
{
    [Fact]
    public void BurningBlood_IsOnPickup()
    {
        var def = new RelicDefinition(
            Id: "burning_blood",
            Name: "燃え盛る血",
            Rarity: CardRarity.Common,
            Trigger: RelicTrigger.OnBattleEnd,
            Effects: new List<CardEffect> { new UnknownEffect("healPercent") });

        Assert.Equal(RelicTrigger.OnBattleEnd, def.Trigger);
    }

    [Fact]
    public void Lantern_IsPassive()
    {
        var def = new RelicDefinition(
            Id: "lantern",
            Name: "ランタン",
            Rarity: CardRarity.Common,
            Trigger: RelicTrigger.Passive,
            Effects: new List<CardEffect>());

        Assert.Equal(RelicTrigger.Passive, def.Trigger);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~RelicDefinitionTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: 実装**

`src/Core/Relics/RelicTrigger.cs`:

```csharp
namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックの効果発動タイミング。</summary>
public enum RelicTrigger
{
    /// <summary>入手した瞬間に 1 度だけ発動し、その後は何もしない。</summary>
    OnPickup,
    /// <summary>所持している間、常に効果を発揮する。</summary>
    Passive,
    /// <summary>戦闘開始時に発動する。</summary>
    OnBattleStart,
    /// <summary>戦闘終了時に発動する。</summary>
    OnBattleEnd,
    /// <summary>マスのイベント解決後に発動する。</summary>
    OnMapTileResolved,
}
```

`src/Core/Relics/RelicDefinition.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックのマスター定義。</summary>
public sealed record RelicDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    RelicTrigger Trigger,
    IReadOnlyList<CardEffect> Effects);
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~RelicDefinitionTests"`
Expected: 2 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Relics/ tests/Core.Tests/Relics/
git commit -m "feat(core): add RelicDefinition and RelicTrigger"
```

---

## Task 7 — RelicJsonLoader

**Files:**
- Create: `src/Core/Relics/RelicJsonLoader.cs`
- Modify: `tests/Core.Tests/Fixtures/JsonFixtures.cs`
- Create: `tests/Core.Tests/Relics/RelicJsonLoaderTests.cs`

- [ ] **Step 1: フィクスチャに JSON を追加**

`tests/Core.Tests/Fixtures/JsonFixtures.cs` に追記:

```csharp
public const string BurningBloodJson = """
{
  "id": "burning_blood",
  "name": "燃え盛る血",
  "rarity": 1,
  "trigger": "OnBattleEnd",
  "effects": [ { "type": "healPercent", "amount": 6 } ]
}
""";

public const string LanternJson = """
{
  "id": "lantern",
  "name": "ランタン",
  "rarity": 1,
  "trigger": "Passive",
  "effects": []
}
""";
```

- [ ] **Step 2: 失敗テストを書く**

`tests/Core.Tests/Relics/RelicJsonLoaderTests.cs`:

```csharp
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicJsonLoaderTests
{
    [Fact]
    public void ParseBurningBlood()
    {
        var def = RelicJsonLoader.Parse(JsonFixtures.BurningBloodJson);
        Assert.Equal("burning_blood", def.Id);
        Assert.Equal(RelicTrigger.OnBattleEnd, def.Trigger);
        Assert.Single(def.Effects);
    }

    [Fact]
    public void ParseLantern_NoEffectsArray()
    {
        var def = RelicJsonLoader.Parse(JsonFixtures.LanternJson);
        Assert.Equal(RelicTrigger.Passive, def.Trigger);
        Assert.Empty(def.Effects);
    }
}
```

- [ ] **Step 3: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~RelicJsonLoaderTests"`
Expected: コンパイルエラー。

- [ ] **Step 4: 実装**

`src/Core/Relics/RelicJsonLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Relics;

public sealed class RelicJsonException : Exception
{
    public RelicJsonException(string message) : base(message) { }
    public RelicJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class RelicJsonLoader
{
    public static RelicDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new RelicJsonException("レリック JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetString() ?? throw new RelicJsonException("id が空です。");
            var name = root.GetProperty("name").GetString() ?? throw new RelicJsonException("name が空です。");
            var rarity = (CardRarity)root.GetProperty("rarity").GetInt32();
            var trigger = ParseTrigger(root.GetProperty("trigger").GetString()!);

            var effects = new List<CardEffect>();
            if (root.TryGetProperty("effects", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var type = el.GetProperty("type").GetString()!;
                    effects.Add(new UnknownEffect(type));
                }
            }

            return new RelicDefinition(id, name, rarity, trigger, effects);
        }
    }

    private static RelicTrigger ParseTrigger(string s) => s switch
    {
        "OnPickup" => RelicTrigger.OnPickup,
        "Passive" => RelicTrigger.Passive,
        "OnBattleStart" => RelicTrigger.OnBattleStart,
        "OnBattleEnd" => RelicTrigger.OnBattleEnd,
        "OnMapTileResolved" => RelicTrigger.OnMapTileResolved,
        _ => throw new RelicJsonException($"未知の trigger: {s}"),
    };
}
```

**注:** レリック固有の効果プリミティブは Phase 0 では全て `UnknownEffect` としておき、後続フェーズで必要になった種別のみ具象化する。

- [ ] **Step 5: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~RelicJsonLoaderTests"`
Expected: 2 件成功。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Relics/RelicJsonLoader.cs tests/Core.Tests/Relics/RelicJsonLoaderTests.cs tests/Core.Tests/Fixtures/JsonFixtures.cs
git commit -m "feat(core): add RelicJsonLoader"
```

---

## Task 8 — PotionDefinition と PotionJsonLoader

**Files:**
- Create: `src/Core/Potions/PotionDefinition.cs`
- Create: `src/Core/Potions/PotionJsonLoader.cs`
- Modify: `tests/Core.Tests/Fixtures/JsonFixtures.cs`
- Create: `tests/Core.Tests/Potions/PotionJsonLoaderTests.cs`

**設計メモ:** ポーションは「使い切り」「戦闘中・非戦闘中どちらでも使用可」「発動条件を満たせばいつでも使用可」。定義には `usableInBattle` / `usableOutOfBattle` の 2 つのフラグと効果配列を持たせる。

- [ ] **Step 1: フィクスチャに追加**

```csharp
public const string BlockPotionJson = """
{
  "id": "block_potion",
  "name": "ブロックポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [ { "type": "gainBlock", "amount": 12 } ]
}
""";

public const string FirePotionJson = """
{
  "id": "fire_potion",
  "name": "ファイアポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [ { "type": "damage", "amount": 20 } ]
}
""";
```

- [ ] **Step 2: 失敗テストを書く**

`tests/Core.Tests/Potions/PotionJsonLoaderTests.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Potions;

public class PotionJsonLoaderTests
{
    [Fact]
    public void ParseBlockPotion()
    {
        var def = PotionJsonLoader.Parse(JsonFixtures.BlockPotionJson);
        Assert.Equal("block_potion", def.Id);
        Assert.True(def.UsableInBattle);
        Assert.False(def.UsableOutOfBattle);
        Assert.IsType<GainBlockEffect>(System.Linq.Enumerable.Single(def.Effects));
    }

    [Fact]
    public void ParseFirePotion()
    {
        var def = PotionJsonLoader.Parse(JsonFixtures.FirePotionJson);
        var dmg = Assert.IsType<DamageEffect>(System.Linq.Enumerable.Single(def.Effects));
        Assert.Equal(20, dmg.Amount);
    }
}
```

- [ ] **Step 3: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~PotionJsonLoaderTests"`
Expected: コンパイルエラー。

- [ ] **Step 4: 実装**

`src/Core/Potions/PotionDefinition.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Potions;

public sealed record PotionDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    bool UsableInBattle,
    bool UsableOutOfBattle,
    IReadOnlyList<CardEffect> Effects);
```

`src/Core/Potions/PotionJsonLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Potions;

public sealed class PotionJsonException : Exception
{
    public PotionJsonException(string message) : base(message) { }
    public PotionJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class PotionJsonLoader
{
    public static PotionDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new PotionJsonException("ポーション JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetString()!;
            var name = root.GetProperty("name").GetString()!;
            var rarity = (CardRarity)root.GetProperty("rarity").GetInt32();
            var inBattle = root.GetProperty("usableInBattle").GetBoolean();
            var outOfBattle = root.GetProperty("usableOutOfBattle").GetBoolean();

            var effects = new List<CardEffect>();
            if (root.TryGetProperty("effects", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in arr.EnumerateArray())
                {
                    var type = el.GetProperty("type").GetString()!;
                    effects.Add(type switch
                    {
                        "damage" => new DamageEffect(el.GetProperty("amount").GetInt32()),
                        "gainBlock" => new GainBlockEffect(el.GetProperty("amount").GetInt32()),
                        _ => new UnknownEffect(type),
                    });
                }
            }

            return new PotionDefinition(id, name, rarity, inBattle, outOfBattle, effects);
        }
    }
}
```

- [ ] **Step 5: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~PotionJsonLoaderTests"`
Expected: 2 件成功。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Potions/ tests/Core.Tests/Potions/ tests/Core.Tests/Fixtures/JsonFixtures.cs
git commit -m "feat(core): add PotionDefinition and PotionJsonLoader"
```

---

## Task 9 — EnemyPool と EnemyDefinition

**Files:**
- Create: `src/Core/Enemy/EnemyPool.cs`
- Create: `src/Core/Enemy/EnemyDefinition.cs`
- Create: `tests/Core.Tests/Enemy/EnemyDefinitionTests.cs`

**設計メモ:** `EnemyPool` は `Act`（1〜3）と `Tier`（`Weak` / `Strong` / `Elite` / `Boss`）の組合せ。マップ生成時にこれで絞り込む。行動テーブル（`moveset`）は Phase 0 では JSON に文字列配列で保持するだけ（具体的な行動解釈は Phase 10 で実装）。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Enemy/EnemyDefinitionTests.cs`:

```csharp
using RoguelikeCardGame.Core.Enemy;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Enemy;

public class EnemyDefinitionTests
{
    [Fact]
    public void Pool_EqualsBySemantic()
    {
        var a = new EnemyPool(1, EnemyTier.Weak);
        var b = new EnemyPool(1, EnemyTier.Weak);
        Assert.Equal(a, b);
    }

    [Fact]
    public void JawWorm_IsAct1Weak()
    {
        var def = new EnemyDefinition(
            Id: "jaw_worm",
            Name: "ジョウ・ワーム",
            HpMin: 40,
            HpMax: 44,
            Pool: new EnemyPool(1, EnemyTier.Weak),
            Moveset: new[] { "chomp", "thrash", "bellow" });

        Assert.Equal(1, def.Pool.Act);
        Assert.Equal(EnemyTier.Weak, def.Pool.Tier);
        Assert.Equal(3, def.Moveset.Count);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~EnemyDefinitionTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: 実装**

`src/Core/Enemy/EnemyPool.cs`:

```csharp
namespace RoguelikeCardGame.Core.Enemy;

public enum EnemyTier
{
    Weak,
    Strong,
    Elite,
    Boss,
}

public sealed record EnemyPool(int Act, EnemyTier Tier);
```

`src/Core/Enemy/EnemyDefinition.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Enemy;

public sealed record EnemyDefinition(
    string Id,
    string Name,
    int HpMin,
    int HpMax,
    EnemyPool Pool,
    IReadOnlyList<string> Moveset);
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~EnemyDefinitionTests"`
Expected: 2 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Enemy/ tests/Core.Tests/Enemy/
git commit -m "feat(core): add EnemyDefinition and EnemyPool"
```

---

## Task 10 — EnemyJsonLoader

**Files:**
- Create: `src/Core/Enemy/EnemyJsonLoader.cs`
- Modify: `tests/Core.Tests/Fixtures/JsonFixtures.cs`
- Create: `tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs`

- [ ] **Step 1: フィクスチャに追加**

```csharp
public const string JawWormJson = """
{
  "id": "jaw_worm",
  "name": "ジョウ・ワーム",
  "hpMin": 40,
  "hpMax": 44,
  "act": 1,
  "tier": "Weak",
  "moveset": ["chomp", "thrash", "bellow"]
}
""";

public const string GremlinNobJson = """
{
  "id": "gremlin_nob",
  "name": "グレムリン・ノブ",
  "hpMin": 82,
  "hpMax": 86,
  "act": 1,
  "tier": "Elite",
  "moveset": ["bellow", "rush", "skull_bash"]
}
""";
```

- [ ] **Step 2: 失敗テストを書く**

`tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs`:

```csharp
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Enemy;

public class EnemyJsonLoaderTests
{
    [Fact]
    public void ParseJawWorm()
    {
        var def = EnemyJsonLoader.Parse(JsonFixtures.JawWormJson);
        Assert.Equal("jaw_worm", def.Id);
        Assert.Equal(40, def.HpMin);
        Assert.Equal(44, def.HpMax);
        Assert.Equal(new EnemyPool(1, EnemyTier.Weak), def.Pool);
        Assert.Equal(3, def.Moveset.Count);
    }

    [Fact]
    public void ParseGremlinNob_IsElite()
    {
        var def = EnemyJsonLoader.Parse(JsonFixtures.GremlinNobJson);
        Assert.Equal(EnemyTier.Elite, def.Pool.Tier);
    }
}
```

- [ ] **Step 3: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~EnemyJsonLoaderTests"`
Expected: コンパイルエラー。

- [ ] **Step 4: 実装**

`src/Core/Enemy/EnemyJsonLoader.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Enemy;

public sealed class EnemyJsonException : Exception
{
    public EnemyJsonException(string message) : base(message) { }
    public EnemyJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class EnemyJsonLoader
{
    public static EnemyDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EnemyJsonException("敵 JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetString()!;
            var name = root.GetProperty("name").GetString()!;
            var hpMin = root.GetProperty("hpMin").GetInt32();
            var hpMax = root.GetProperty("hpMax").GetInt32();
            var act = root.GetProperty("act").GetInt32();
            var tier = ParseTier(root.GetProperty("tier").GetString()!);

            var moves = new List<string>();
            if (root.TryGetProperty("moveset", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var m in arr.EnumerateArray()) moves.Add(m.GetString()!);

            return new EnemyDefinition(id, name, hpMin, hpMax, new EnemyPool(act, tier), moves);
        }
    }

    private static EnemyTier ParseTier(string s) => s switch
    {
        "Weak" => EnemyTier.Weak,
        "Strong" => EnemyTier.Strong,
        "Elite" => EnemyTier.Elite,
        "Boss" => EnemyTier.Boss,
        _ => throw new EnemyJsonException($"未知の tier: {s}"),
    };
}
```

- [ ] **Step 5: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~EnemyJsonLoaderTests"`
Expected: 2 件成功。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Enemy/EnemyJsonLoader.cs tests/Core.Tests/Enemy/EnemyJsonLoaderTests.cs tests/Core.Tests/Fixtures/JsonFixtures.cs
git commit -m "feat(core): add EnemyJsonLoader"
```

---

## Task 11 — DataCatalog（全カテゴリまとめて受け取る純粋関数）

**Files:**
- Create: `src/Core/Data/DataCatalog.cs`
- Create: `tests/Core.Tests/Data/DataCatalogTests.cs`

**設計メモ:** `DataCatalog.LoadFromStrings(...)` はカード／レリック／ポーション／敵の各 JSON 文字列コレクションを受け取り、ID でキーされた 4 つの `IReadOnlyDictionary` を持つ `DataCatalog` record を返す純粋関数。ID 重複は例外。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Data/DataCatalogTests.cs`:

```csharp
using System.Collections.Generic;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class DataCatalogTests
{
    [Fact]
    public void LoadFromStrings_BuildsAllFourDictionaries()
    {
        var catalog = DataCatalog.LoadFromStrings(
            cards: new[] { JsonFixtures.StrikeJson, JsonFixtures.DefendJson },
            relics: new[] { JsonFixtures.BurningBloodJson, JsonFixtures.LanternJson },
            potions: new[] { JsonFixtures.BlockPotionJson, JsonFixtures.FirePotionJson },
            enemies: new[] { JsonFixtures.JawWormJson, JsonFixtures.GremlinNobJson });

        Assert.Equal(2, catalog.Cards.Count);
        Assert.Equal(2, catalog.Relics.Count);
        Assert.Equal(2, catalog.Potions.Count);
        Assert.Equal(2, catalog.Enemies.Count);
        Assert.Equal("ストライク", catalog.Cards["strike"].Name);
        Assert.Equal("グレムリン・ノブ", catalog.Enemies["gremlin_nob"].Name);
    }

    [Fact]
    public void DuplicateCardId_Throws()
    {
        var ex = Assert.Throws<DataCatalogException>(() =>
            DataCatalog.LoadFromStrings(
                cards: new[] { JsonFixtures.StrikeJson, JsonFixtures.StrikeJson },
                relics: System.Array.Empty<string>(),
                potions: System.Array.Empty<string>(),
                enemies: System.Array.Empty<string>()));
        Assert.Contains("strike", ex.Message);
    }

    [Fact]
    public void EmptyInputs_ReturnsEmptyCatalog()
    {
        var catalog = DataCatalog.LoadFromStrings(
            cards: System.Array.Empty<string>(),
            relics: System.Array.Empty<string>(),
            potions: System.Array.Empty<string>(),
            enemies: System.Array.Empty<string>());
        Assert.Empty(catalog.Cards);
        Assert.Empty(catalog.Relics);
        Assert.Empty(catalog.Potions);
        Assert.Empty(catalog.Enemies);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~DataCatalogTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: 実装**

`src/Core/Data/DataCatalog.cs`:

```csharp
using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Data;

public sealed class DataCatalogException : Exception
{
    public DataCatalogException(string message) : base(message) { }
}

/// <summary>ゲーム全体のマスターデータ。ラン状態やマップ生成から参照される読み取り専用の辞書束。</summary>
public sealed record DataCatalog(
    IReadOnlyDictionary<string, CardDefinition> Cards,
    IReadOnlyDictionary<string, RelicDefinition> Relics,
    IReadOnlyDictionary<string, PotionDefinition> Potions,
    IReadOnlyDictionary<string, EnemyDefinition> Enemies)
{
    public static DataCatalog LoadFromStrings(
        IEnumerable<string> cards,
        IEnumerable<string> relics,
        IEnumerable<string> potions,
        IEnumerable<string> enemies)
    {
        var cardMap = new Dictionary<string, CardDefinition>();
        foreach (var json in cards)
        {
            var def = CardJsonLoader.Parse(json);
            if (!cardMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"カード ID が重複: {def.Id}");
        }

        var relicMap = new Dictionary<string, RelicDefinition>();
        foreach (var json in relics)
        {
            var def = RelicJsonLoader.Parse(json);
            if (!relicMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"レリック ID が重複: {def.Id}");
        }

        var potionMap = new Dictionary<string, PotionDefinition>();
        foreach (var json in potions)
        {
            var def = PotionJsonLoader.Parse(json);
            if (!potionMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"ポーション ID が重複: {def.Id}");
        }

        var enemyMap = new Dictionary<string, EnemyDefinition>();
        foreach (var json in enemies)
        {
            var def = EnemyJsonLoader.Parse(json);
            if (!enemyMap.TryAdd(def.Id, def))
                throw new DataCatalogException($"敵 ID が重複: {def.Id}");
        }

        return new DataCatalog(cardMap, relicMap, potionMap, enemyMap);
    }
}
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~DataCatalogTests"`
Expected: 3 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Data/DataCatalog.cs tests/Core.Tests/Data/DataCatalogTests.cs
git commit -m "feat(core): add DataCatalog aggregator"
```

---

## Task 12 — シード JSON ファイル（初期デッキ＋最小限のレリック／ポーション／敵）

**Files:**
- Create: `src/Core/Data/Cards/strike.json`
- Create: `src/Core/Data/Cards/defend.json`
- Create: `src/Core/Data/Relics/burning_blood.json`
- Create: `src/Core/Data/Relics/lantern.json`
- Create: `src/Core/Data/Potions/block_potion.json`
- Create: `src/Core/Data/Potions/fire_potion.json`
- Create: `src/Core/Data/Enemies/jaw_worm.json`
- Create: `src/Core/Data/Enemies/louse_red.json`
- Create: `src/Core/Data/Enemies/gremlin_nob.json`
- Create: `src/Core/Data/Enemies/hexaghost.json`
- Modify: `src/Core/Core.csproj`（JSON を埋め込みリソースに指定）

- [ ] **Step 1: strike.json を作成**

`src/Core/Data/Cards/strike.json`:

```json
{
  "id": "strike",
  "name": "ストライク",
  "rarity": 1,
  "cardType": "Attack",
  "cost": 1,
  "effects": [{ "type": "damage", "amount": 6 }],
  "upgradedEffects": [{ "type": "damage", "amount": 9 }]
}
```

- [ ] **Step 2: defend.json を作成**

`src/Core/Data/Cards/defend.json`:

```json
{
  "id": "defend",
  "name": "防御",
  "rarity": 1,
  "cardType": "Skill",
  "cost": 1,
  "effects": [{ "type": "gainBlock", "amount": 5 }],
  "upgradedEffects": [{ "type": "gainBlock", "amount": 8 }]
}
```

- [ ] **Step 3: 2 種のレリック JSON を作成**

`src/Core/Data/Relics/burning_blood.json`:

```json
{
  "id": "burning_blood",
  "name": "燃え盛る血",
  "rarity": 1,
  "trigger": "OnBattleEnd",
  "effects": [{ "type": "healPercent", "amount": 6 }]
}
```

`src/Core/Data/Relics/lantern.json`:

```json
{
  "id": "lantern",
  "name": "ランタン",
  "rarity": 1,
  "trigger": "Passive",
  "effects": [{ "type": "extraEnergyOnFirstTurn", "amount": 1 }]
}
```

- [ ] **Step 4: 2 種のポーション JSON を作成**

`src/Core/Data/Potions/block_potion.json`:

```json
{
  "id": "block_potion",
  "name": "ブロックポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [{ "type": "gainBlock", "amount": 12 }]
}
```

`src/Core/Data/Potions/fire_potion.json`:

```json
{
  "id": "fire_potion",
  "name": "ファイアポーション",
  "rarity": 1,
  "usableInBattle": true,
  "usableOutOfBattle": false,
  "effects": [{ "type": "damage", "amount": 20 }]
}
```

- [ ] **Step 5: 4 種の敵 JSON を作成（act1 弱 2／エリート 1／ボス 1）**

`src/Core/Data/Enemies/jaw_worm.json`:

```json
{
  "id": "jaw_worm",
  "name": "ジョウ・ワーム",
  "hpMin": 40,
  "hpMax": 44,
  "act": 1,
  "tier": "Weak",
  "moveset": ["chomp", "thrash", "bellow"]
}
```

`src/Core/Data/Enemies/louse_red.json`:

```json
{
  "id": "louse_red",
  "name": "レッドルース",
  "hpMin": 10,
  "hpMax": 15,
  "act": 1,
  "tier": "Weak",
  "moveset": ["bite", "grow"]
}
```

`src/Core/Data/Enemies/gremlin_nob.json`:

```json
{
  "id": "gremlin_nob",
  "name": "グレムリン・ノブ",
  "hpMin": 82,
  "hpMax": 86,
  "act": 1,
  "tier": "Elite",
  "moveset": ["bellow", "rush", "skull_bash"]
}
```

`src/Core/Data/Enemies/hexaghost.json`:

```json
{
  "id": "hexaghost",
  "name": "ヘキサゴースト",
  "hpMin": 250,
  "hpMax": 250,
  "act": 1,
  "tier": "Boss",
  "moveset": ["activate", "divider", "inferno", "sear", "tackle", "inflame"]
}
```

- [ ] **Step 6: Core.csproj に JSON を埋め込みリソース指定**

`src/Core/Core.csproj` に追記（`</Project>` の直前）:

```xml
<ItemGroup>
  <EmbeddedResource Include="Data\Cards\*.json" />
  <EmbeddedResource Include="Data\Relics\*.json" />
  <EmbeddedResource Include="Data\Potions\*.json" />
  <EmbeddedResource Include="Data\Enemies\*.json" />
</ItemGroup>
```

- [ ] **Step 7: ビルド確認**

Run: `dotnet build`
Expected: ビルド成功。

- [ ] **Step 8: コミット**

```bash
git add src/Core/Data/ src/Core/Core.csproj
git commit -m "feat(core): add seed JSON for initial deck, relics, potions, enemies"
```

---

## Task 13 — 埋め込みリソースからシードを読み込むヘルパーと統合テスト

**Files:**
- Create: `src/Core/Data/EmbeddedDataLoader.cs`
- Create: `tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs`

**設計メモ:** Core は I/O を持たない原則だが、「自身のアセンブリに埋め込まれたリソースを読む」ことは Udon# でも同等手段があるので許容する（リフレクションではなく `Assembly.GetManifestResourceStream` のみ）。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs`:

```csharp
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class EmbeddedDataLoaderTests
{
    [Fact]
    public void LoadEmbeddedCatalog_ContainsStrikeAndDefend()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Contains("strike", catalog.Cards.Keys);
        Assert.Contains("defend", catalog.Cards.Keys);
        Assert.Equal(6, ((DamageEffect)catalog.Cards["strike"].Effects[0]).Amount);
        Assert.Equal(9, ((DamageEffect)catalog.Cards["strike"].UpgradedEffects![0]).Amount);
    }

    [Fact]
    public void LoadEmbeddedCatalog_ContainsBossEnemy()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Contains("hexaghost", catalog.Enemies.Keys);
        Assert.Equal(EnemyTier.Boss, catalog.Enemies["hexaghost"].Pool.Tier);
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~EmbeddedDataLoaderTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: 実装**

`src/Core/Data/EmbeddedDataLoader.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace RoguelikeCardGame.Core.Data;

/// <summary>Core アセンブリに埋め込まれた JSON リソースから DataCatalog を構築する。</summary>
public static class EmbeddedDataLoader
{
    private const string CardsPrefix = "RoguelikeCardGame.Core.Data.Cards.";
    private const string RelicsPrefix = "RoguelikeCardGame.Core.Data.Relics.";
    private const string PotionsPrefix = "RoguelikeCardGame.Core.Data.Potions.";
    private const string EnemiesPrefix = "RoguelikeCardGame.Core.Data.Enemies.";

    public static DataCatalog LoadCatalog()
    {
        var asm = typeof(EmbeddedDataLoader).Assembly;
        return DataCatalog.LoadFromStrings(
            cards: ReadAllWithPrefix(asm, CardsPrefix),
            relics: ReadAllWithPrefix(asm, RelicsPrefix),
            potions: ReadAllWithPrefix(asm, PotionsPrefix),
            enemies: ReadAllWithPrefix(asm, EnemiesPrefix));
    }

    private static IEnumerable<string> ReadAllWithPrefix(Assembly asm, string prefix)
    {
        var names = asm.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix) && n.EndsWith(".json"))
            .OrderBy(n => n);
        foreach (var name in names)
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            yield return reader.ReadToEnd();
        }
    }
}
```

**注:** 埋め込みリソース名は `{RootNamespace}.{Folder}.{File}` になる。`RootNamespace` は Task 1 Step 2 で `RoguelikeCardGame.Core` に設定済み。

- [ ] **Step 4: 埋め込みリソース名を目視で確認**

Run: `dotnet build src/Core/Core.csproj -v:n 2>&1 | grep -i "Data\.Cards\.strike"`
Expected: リソース名 `RoguelikeCardGame.Core.Data.Cards.strike.json` が出力に現れる。現れない場合は `Core.csproj` の `<EmbeddedResource>` 設定か `<RootNamespace>` を見直す。

- [ ] **Step 5: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~EmbeddedDataLoaderTests"`
Expected: 2 件成功。

- [ ] **Step 6: コミット**

```bash
git add src/Core/Data/EmbeddedDataLoader.cs tests/Core.Tests/Data/EmbeddedDataLoaderTests.cs src/Core/Core.csproj
git commit -m "feat(core): add EmbeddedDataLoader for packaged JSON seeds"
```

---

## Task 14 — 初期デッキ構成（Strike x5 + Defend x5）

**Files:**
- Create: `src/Core/Player/StarterDeck.cs`
- Create: `tests/Core.Tests/Player/StarterDeckTests.cs`

**設計メモ:** 初期デッキは「カード ID の配列」で表現し、ラン開始時にこの並びでデッキに投入する。カードの実体は `DataCatalog.Cards` から引く。

- [ ] **Step 1: 失敗テストを書く**

`tests/Core.Tests/Player/StarterDeckTests.cs`:

```csharp
using System.Linq;
using RoguelikeCardGame.Core.Player;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Player;

public class StarterDeckTests
{
    [Fact]
    public void DefaultDeck_HasFiveStrikesAndFiveDefends()
    {
        var ids = StarterDeck.DefaultCardIds;
        Assert.Equal(10, ids.Count);
        Assert.Equal(5, ids.Count(i => i == "strike"));
        Assert.Equal(5, ids.Count(i => i == "defend"));
    }
}
```

- [ ] **Step 2: テスト実行して失敗を確認**

Run: `dotnet test --filter "FullyQualifiedName~StarterDeckTests"`
Expected: コンパイルエラー。

- [ ] **Step 3: 実装**

`src/Core/Player/StarterDeck.cs`:

```csharp
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Player;

/// <summary>初期デッキ（固定 10 枚）。</summary>
public static class StarterDeck
{
    public static readonly IReadOnlyList<string> DefaultCardIds = new[]
    {
        "strike", "strike", "strike", "strike", "strike",
        "defend", "defend", "defend", "defend", "defend",
    };
}
```

- [ ] **Step 4: テスト実行して通過を確認**

Run: `dotnet test --filter "FullyQualifiedName~StarterDeckTests"`
Expected: 1 件成功。

- [ ] **Step 5: コミット**

```bash
git add src/Core/Player/ tests/Core.Tests/Player/
git commit -m "feat(core): define starter deck (5 Strike + 5 Defend)"
```

---

## Task 15 — フェーズ全体のリグレッション確認

- [ ] **Step 1: 全テストを通し、Phase 0 の Done 判定を確認**

Run: `dotnet test`
Expected: すべて成功。件数は概算で Card 系 12 件前後、Relic 4 件、Potion 2 件、Enemy 4 件、DataCatalog 3 件、EmbeddedDataLoader 2 件、StarterDeck 1 件。

- [ ] **Step 2: ビルド警告の確認**

Run: `dotnet build /warnaserror`
Expected: 警告ゼロ。出た場合は個別に対応。

- [ ] **Step 3: Phase 0 終了タグを打つ**

```bash
git tag phase0-complete
git log --oneline | head -20
```

Phase 0 完了。Phase 1（ラン状態とセーブシステム）の詳細計画に進める状態。

---

## Self-Review（このフェーズでの仕様カバレッジ）

| 仕様項目 | 対応タスク |
|----------|-----------|
| カード JSON 定義（id / name / displayName / rarity / cardType / cost / effects / upgradedEffects） | Task 4 / Task 5 |
| レアリティ 0〜4（プロモ／コモン／レア／エピック／レジェンダリー） | Task 2 |
| カード種別 Unit / Attack / Skill / Power | Task 2 |
| Cost null 可（プレイ不可表現） | Task 4 / Task 5 |
| 初期デッキ Strike×5 + Defend×5、強化値 9 / 8 | Task 12 / Task 14 |
| レリック JSON（入手時効果型・永続型） | Task 6 / Task 7 / Task 12 |
| ポーション JSON（戦闘中・非戦闘中フラグ、効果） | Task 8 / Task 12 |
| モンスター JSON（HP レンジ、act、tier、moveset） | Task 9 / Task 10 / Task 12 |
| 全データの統合ロード | Task 11 / Task 13 |
| Core の I/O 非依存（文字列→モデルのみ） | Task 5-11 で担保、Task 13 は埋め込みリソースのみ許容 |
