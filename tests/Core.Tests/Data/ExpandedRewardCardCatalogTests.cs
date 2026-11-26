using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public sealed class ExpandedRewardCardCatalogTests
{
    private static readonly string[] ApprovedBasicIds =
    [
        "striker_basic_attack",
        "striker_basic_defense",
        "guardian_basic_attack",
        "guardian_basic_defense",
        "tactician_basic_attack",
        "tactician_basic_defense",
        "enhancer_basic_attack",
        "enhancer_basic_defense",
        "striker_basic_quickdraw",
        "striker_basic_step",
        "striker_basic_finishing_slash",
        "guardian_basic_foothold",
        "guardian_basic_defensive_form",
        "guardian_basic_cover_line",
        "tactician_basic_disrupting_shot",
        "tactician_basic_counterplan",
        "tactician_basic_recursive_plan",
        "enhancer_basic_preparation",
        "enhancer_basic_self_attunement",
        "enhancer_basic_afterglow_hymn",
    ];

    // 2026-11-26: 位相（静・動）を使うストライカーの報酬カード。各クラス・レア度 4 枚の枠とは別に足している。
    private static readonly string[] PhaseRewardIds =
    [
        "striker_phase_calm_form",
        "striker_phase_motion_form",
        "striker_phase_calm_blade",
        "striker_phase_blazing_step",
        "striker_phase_flowing_stance",
        "striker_phase_return_edge",
        "striker_phase_honing",
        "striker_phase_quelling_shout",
    ];

    private static readonly string[] ApprovedRewardIds =
    [
        "striker_common_step_in",
        "striker_common_chain_fang",
        "striker_common_rising_break",
        "striker_common_offensive_breath",
        "striker_rare_rising_flurry",
        "striker_rare_sunder",
        "striker_rare_converging_slash",
        "striker_rare_composure",
        "striker_epic_flash_finale",
        "striker_epic_dance_finale",
        "striker_epic_battle_breath",
        "striker_epic_form_copy",

        "guardian_common_parry",
        "guardian_common_defensive_stance",
        "guardian_common_shield_bash",
        "guardian_common_support_posture",
        "guardian_rare_endure",
        "guardian_rare_shield_line",
        "guardian_rare_counteroffensive",
        "guardian_rare_covering_hand",
        "guardian_epic_guarding_zone",
        "guardian_epic_fortification",
        "guardian_epic_fortress_breaker",
        "guardian_epic_unfallen",

        "tactician_common_smokescreen",
        "tactician_common_foresight",
        "tactician_common_prepared_shield",
        "tactician_common_hidden_blade",
        "tactician_rare_signal",
        "tactician_rare_chain_plan",
        "tactician_rare_virulent_shot",
        "tactician_rare_dawn_scheme",
        "tactician_epic_layered_defense",
        "tactician_epic_venomous_finale",
        "tactician_epic_bind_time",
        "tactician_epic_recover_scheme",

        "enhancer_common_first_aid",
        "enhancer_common_vitality_infusion",
        "enhancer_common_afterglow_guard",
        "enhancer_common_tuning",
        "enhancer_rare_regeneration_treatment",
        "enhancer_rare_afterglow_barrier",
        "enhancer_rare_excitation_wave",
        "enhancer_rare_resonation",
        "enhancer_epic_lifeline",
        "enhancer_epic_immortal_afterglow",
        "enhancer_epic_resonance_chamber",
        "enhancer_epic_grand_resonance",

        "universal_common_precise_strike",
        "universal_common_rebrace",
        "universal_common_immediate_response",
        "universal_common_heavy_strike",
        "universal_rare_deterrence",
        "universal_rare_sweep",
        "universal_rare_equipment_check",
        "universal_rare_preparatory_motion",
        "universal_epic_free_link",
        "universal_epic_breakthrough",
        "universal_epic_total_defense",
        "universal_epic_reconstruction",
    ];

    [Fact]
    public void Embedded_catalog_contains_exact_approved_basic_and_reward_sets()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var basicIds = catalog.Cards.Values
            .Where(CardPoolRules.IsBasic)
            .Select(card => card.Id)
            .OrderBy(id => id, StringComparer.Ordinal);
        var rewardIds = catalog.Cards.Values
            .Where(CardPoolRules.IsReward)
            .Select(card => card.Id)
            .OrderBy(id => id, StringComparer.Ordinal);

        Assert.Equal(ApprovedBasicIds.OrderBy(id => id, StringComparer.Ordinal), basicIds);
        Assert.Equal(ApprovedRewardIds.Concat(PhaseRewardIds).OrderBy(id => id, StringComparer.Ordinal), rewardIds);

        var approved = ApprovedBasicIds.Concat(ApprovedRewardIds).Concat(PhaseRewardIds)
            .Select(id => catalog.Cards[id])
            .ToArray();
        Assert.Equal(88, approved.Length);
        Assert.Equal(88, approved.Select(card => card.Id).Distinct(StringComparer.Ordinal).Count());
        // 攻撃の基本・防御の基本はクラスごとに同名のカードがある（4 クラス × 2 種）。それ以外は名前が重複しない。
        Assert.Equal(88 - 6, approved.Select(card => card.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(approved, card =>
        {
            Assert.NotNull(card.UpgradedEffects);
            Assert.True(card.IsUpgradable, $"{card.Id} must define an upgraded form.");
        });

        DataCatalogIntegrityValidator.Validate(catalog);
    }

    [Fact]
    public void Reward_pool_has_four_cards_per_class_and_rarity()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rewardCards = catalog.Cards.Values.Where(CardPoolRules.IsReward).ToArray();

        foreach (var poolClass in Enum.GetValues<CardPoolClass>())
        {
            foreach (var rarity in new[]
                     {
                         CardRarity.Common,
                         CardRarity.Rare,
                         CardRarity.Epic,
                     })
            {
                Assert.Equal(4, rewardCards.Count(card =>
                    card.PoolClass == poolClass && card.Rarity == rarity
                    && !PhaseRewardIds.Contains(card.Id)));
            }
        }

        foreach (var poolClass in new[]
                 {
                     CardPoolClass.Striker,
                     CardPoolClass.Guardian,
                     CardPoolClass.Tactician,
                     CardPoolClass.Enhancer,
                 })
        {
            Assert.Equal(3, rewardCards.Count(card =>
                card.PoolClass == poolClass &&
                card.EffectiveEligibleCharacterIds.Count == 1));
        }
        Assert.DoesNotContain(rewardCards, card =>
            card.PoolClass == CardPoolClass.Universal &&
            card.EffectiveEligibleCharacterIds.Count > 0);
    }

    [Fact]
    public void Immediate_draw_attacks_and_skills_are_limited_in_both_versions()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        foreach (var id in ApprovedBasicIds.Concat(ApprovedRewardIds))
        {
            var card = catalog.Cards[id];
            if (card.CardType is not (CardType.Attack or CardType.Skill))
                continue;

            if (HasImmediateDraw(card.Effects))
                Assert.Equal(1, card.EffectiveUsesPerTurn(upgraded: false));
            if (HasImmediateDraw(card.UpgradedEffects!))
                Assert.Equal(1, card.EffectiveUsesPerTurn(upgraded: true));
        }
    }

    [Fact]
    public void Virulent_shot_applies_two_separate_legal_virulent_stacks()
    {
        var card = EmbeddedDataLoader.LoadCatalog().Cards["tactician_rare_virulent_shot"];

        AssertVirulentPair(card.Effects);
        AssertVirulentPair(card.UpgradedEffects!);
        Assert.Contains(
            "[S:virulentPoison][N:2]",
            CardTextFormatter.Format(card, upgraded: false));
        Assert.Contains(
            "[S:virulentPoison][N:2]",
            CardTextFormatter.Format(card, upgraded: true));
        Assert.DoesNotContain(
            "[S:virulentPoison][N:1]",
            CardTextFormatter.Format(card, upgraded: false));
    }

    [Fact]
    public void Player_relic_catalog_does_not_grant_lifesteal()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.DoesNotContain(
            catalog.Relics.Values.SelectMany(relic => relic.Effects),
            effect => effect.Action == "buff"
                && effect.Name == BattleStatusIds.Lifesteal);
    }

    [Fact]
    public void Enhancer_self_attunement_groups_each_power_buff_application()
    {
        var card = EmbeddedDataLoader.LoadCatalog().Cards["enhancer_basic_self_attunement"];

        var normal = Assert.Single(card.Effects);
        Assert.Equal(new[] { BattleStatusIds.Vigor, BattleStatusIds.Strength },
            normal.EffectiveStatusChanges().Select(change => change.Name));

        var upgraded = Assert.Single(card.UpgradedEffects!);
        Assert.Equal(
            new[]
            {
                BattleStatusIds.Vigor,
                BattleStatusIds.Strength,
                BattleStatusIds.Dexterity,
            },
            upgraded.EffectiveStatusChanges().Select(change => change.Name));
    }

    [Fact]
    public void Only_guardian_cards_grant_block_to_other_allies()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var normalCards = ApprovedBasicIds.Concat(ApprovedRewardIds)
            .Select(id => catalog.Cards[id]);

        Assert.DoesNotContain(normalCards, card =>
            card.PoolClass != CardPoolClass.Guardian &&
            card.Effects.Concat(card.UpgradedEffects!)
                .Any(effect =>
                    effect.Action == "block" &&
                    effect.Scope != EffectScope.Self &&
                    effect.Side == EffectSide.Ally));
    }

    [Fact]
    public void Every_character_has_approved_resources_deck_and_wild_basic()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.Equal(4, catalog.Characters.Count);
        foreach (var character in catalog.Characters.Values)
        {
            Assert.Equal(3, character.EnergyPerTurn);
            Assert.Equal(5, character.DrawPerTurn);
            Assert.Equal(10, character.Deck.Count);
            Assert.NotNull(character.Trait);

            var ownedCards = character.Deck.Select(id => catalog.Cards[id]).ToArray();
            Assert.Contains(ownedCards, card =>
                card.EffectiveKeywords(upgraded: false)?.Contains("wild") == true &&
                card.EffectiveKeywords(upgraded: true)?.Contains("superwild") == true);
        }

        var preparation = catalog.Cards["enhancer_basic_preparation"];
        Assert.DoesNotContain("wild", preparation.EffectiveKeywords(false) ?? []);
        Assert.DoesNotContain("superwild", preparation.EffectiveKeywords(true) ?? []);

        var hymn = catalog.Cards["enhancer_basic_afterglow_hymn"];
        Assert.Equal(3, hymn.Cost);
        Assert.Null(hymn.UpgradedCost);
        Assert.Contains("wild", hymn.EffectiveKeywords(false) ?? []);
        Assert.Contains("superwild", hymn.EffectiveKeywords(true) ?? []);
    }

    [Fact]
    public void Obsolete_trials_and_wisp_content_stay_outside_normal_pools()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        foreach (var id in new[]
                 {
                     "strike",
                     "defend",
                     "starter_block_0",
                     "starter_strike_2",
                     "starter_summon_3",
                     "wisp_spark",
                     "wisp_aegis",
                     "wisp_scatterflame",
                     "wisp_will_o_wisp",
                 })
        {
            Assert.Empty(catalog.Cards[id].EffectivePoolKinds);
        }

        Assert.Equal("ウィスプ", catalog.Cards["starter_summon_3"].Name);
    }

    private static bool HasImmediateDraw(IEnumerable<CardEffect> effects) =>
        effects.Any(effect =>
            (effect.Action is "draw" or "drawCards") &&
            string.IsNullOrEmpty(effect.Trigger));

    private static void AssertVirulentPair(IReadOnlyList<CardEffect> effects)
    {
        var applications = effects
            .Where(effect => effect.Action == "debuff")
            .SelectMany(effect => effect.EffectiveStatusChanges())
            .Where(change => change.Name == BattleStatusIds.VirulentPoison)
            .ToArray();

        Assert.Equal(2, applications.Length);
        Assert.All(applications, change => Assert.Equal(1, change.Amount));
    }
}
