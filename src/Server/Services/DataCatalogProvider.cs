using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// Owns the current immutable authoring snapshot. Development overrides are parsed as one
/// candidate catalog before the snapshot is swapped, so gameplay never observes a partially
/// written set of definitions.
/// </summary>
public sealed class DataCatalogProvider
{
    private readonly IWebHostEnvironment _env;
    private readonly string _overrideRoot;
    private readonly ConcurrentDictionary<(string Kind, string Id), string> _authoringBases = new();
    private volatile Snapshot _current;

    public DataCatalogProvider(IWebHostEnvironment env)
    {
        _env = env;
        _overrideRoot = Path.Combine(env.ContentRootPath, "..", "..", "data-local", "dev-overrides");
        _current = BuildSnapshot();
    }

    public DataCatalog Current => _current.Catalog;
    public MapGenerationConfig CurrentMapConfig => _current.MapConfig;
    public string OverrideRoot => Path.GetFullPath(_overrideRoot);

    public void Rebuild() => _current = BuildSnapshot();

    /// <summary>
    /// Keeps a source JSON promoted by the workbench active in the current development process.
    /// Embedded resources are fixed until the next build, so this bridge prevents a later catalog
    /// rebuild from temporarily restoring the old compiled definition.
    /// </summary>
    public void RegisterAuthoringBase(string kind, string id, string json)
    {
        if (!_env.IsDevelopment())
            throw new InvalidOperationException("Authoring base documents are development-only.");
        _authoringBases[(kind, id)] = json;
        _current = BuildSnapshot();
    }

    /// <summary>Validates a prospective full override document without changing disk or runtime state.</summary>
    public void ValidateCandidate(string kind, string id, string? json)
        => _ = BuildSnapshot(kind, id, json);

    private Snapshot BuildSnapshot(string? candidateKind = null, string? candidateId = null, string? candidateJson = null)
    {
        if (!_env.IsDevelopment())
        {
            var embeddedMap = MapGenerationConfigLoader.LoadAct1();
            return new Snapshot(EmbeddedDataLoader.LoadCatalog(), embeddedMap);
        }

        Dictionary<string, string> Load(string subDir)
            => new(DevOverrideLoader.Load(_overrideRoot, subDir), StringComparer.Ordinal);

        var cards = Load("cards");
        var relics = Load("relics");
        var potions = Load("potions");
        var enemies = Load("enemies");
        var units = Load("units");
        var encounters = Load("encounters");
        var rewardTables = Load("reward-tables");
        var characters = Load("characters");
        var events = Load("events");
        var actStartPools = Load("act-start-relic-pools");
        var merchantPrices = Load("merchant-prices");
        var mapConfigs = Load("map-configs");

        foreach (var candidate in _authoringBases)
        {
            var target = SelectTarget(candidate.Key.Kind);
            // A newer disk override is intentional and must win over the promoted source base.
            target.TryAdd(candidate.Key.Id, candidate.Value);
        }

        if (candidateKind is not null && candidateId is not null)
        {
            var target = SelectTarget(candidateKind);
            if (candidateJson is null)
            {
                if (_authoringBases.TryGetValue((candidateKind, candidateId), out var authoringBase))
                    target[candidateId] = authoringBase;
                else
                    target.Remove(candidateId);
            }
            else target[candidateId] = candidateJson;
        }

        Dictionary<string, string> SelectTarget(string kind) => kind switch
        {
            "cards" => cards,
            "relics" => relics,
            "potions" => potions,
            "enemies" => enemies,
            "units" => units,
            "encounters" => encounters,
            "rewardTables" => rewardTables,
            "characters" => characters,
            "events" => events,
            "actStartRelicPools" => actStartPools,
            "merchantPrices" => merchantPrices,
            "mapConfigs" => mapConfigs,
            _ => throw new ArgumentException($"Unknown content kind '{kind}'.", nameof(kind)),
        };

        var map = mapConfigs.TryGetValue("map-act1", out var mapJson)
            ? MapGenerationConfigLoader.Parse(mapJson)
            : MapGenerationConfigLoader.LoadAct1();
        merchantPrices.TryGetValue("merchant-prices", out var merchantJson);

        var catalog = EmbeddedDataLoader.LoadCatalogWithOverrides(
            cards,
            relics,
            potions,
            enemies,
            units,
            encounters,
            rewardTables,
            characters,
            events,
            actStartPools,
            merchantJson,
            map);
        DataCatalogIntegrityValidator.Validate(catalog);
        return new Snapshot(catalog, map);
    }

    private sealed record Snapshot(DataCatalog Catalog, MapGenerationConfig MapConfig);
}
