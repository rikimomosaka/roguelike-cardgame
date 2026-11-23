using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public sealed class DevContentServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"content-workbench-{Guid.NewGuid():N}");
    private readonly DataCatalogProvider _provider;
    private readonly DevContentService _service;

    public DevContentServiceTests()
    {
        var contentRoot = Path.Combine(_root, "src", "Server");
        Directory.CreateDirectory(contentRoot);
        var environment = new TestEnvironment(contentRoot);
        _provider = new DataCatalogProvider(environment);
        _service = new DevContentService(environment, _provider);
    }

    [Fact]
    public void Save_validates_then_atomically_adds_override_only_content_to_gameplay()
    {
        using var doc = JsonDocument.Parse("""
            {
              "id": "workbench_card",
              "name": "Workbench Card",
              "customFutureField": { "kept": true },
              "activeVersion": "v1",
              "versions": [{
                "version": "v1",
                "spec": { "rarity": 1, "cardType": "Skill", "cost": 1, "effects": [] }
              }]
            }
            """);

        var saved = _service.Save("cards", "workbench_card", doc.RootElement, expectedRevision: null);

        Assert.True(saved.HasOverride);
        Assert.Contains("workbench_card", _provider.Current.Cards.Keys);
        Assert.True(saved.Document["customFutureField"]!["kept"]!.GetValue<bool>());
        var directory = Path.Combine(_provider.OverrideRoot, "cards");
        Assert.True(File.Exists(Path.Combine(directory, "workbench_card.json")));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void Invalid_candidate_never_reaches_disk()
    {
        using var doc = JsonDocument.Parse("""{ "id": "broken_card", "name": "Broken" }""");

        Assert.ThrowsAny<Exception>(() =>
            _service.Save("cards", "broken_card", doc.RootElement, expectedRevision: null));

        Assert.False(File.Exists(Path.Combine(_provider.OverrideRoot, "cards", "broken_card.json")));
        Assert.DoesNotContain("broken_card", _provider.Current.Cards.Keys);
    }

    [Fact]
    public void Stale_revision_is_rejected_before_overwrite()
    {
        using var first = JsonDocument.Parse("""
            { "id":"revision_card", "name":"First", "activeVersion":"v1",
              "versions":[{ "version":"v1", "spec":{ "rarity":1, "cardType":"Skill", "cost":1, "effects":[] } }] }
            """);
        var saved = _service.Save("cards", "revision_card", first.RootElement, null);
        using var second = JsonDocument.Parse("""
            { "id":"revision_card", "name":"Second", "activeVersion":"v1",
              "versions":[{ "version":"v1", "spec":{ "rarity":1, "cardType":"Skill", "cost":1, "effects":[] } }] }
            """);

        Assert.Throws<DevContentConflictException>(() =>
            _service.Save("cards", "revision_card", second.RootElement, "stale"));
        Assert.Equal("First", _service.Get("cards", "revision_card").Name);
        Assert.NotEqual("stale", saved.Revision);
    }

    [Fact]
    public void Active_version_can_be_rewritten_as_the_only_standard_v1_and_override_is_removed()
    {
        var baseDirectory = Path.Combine(_root, "src", "Core", "Data", "Cards");
        Directory.CreateDirectory(baseDirectory);
        var basePath = Path.Combine(baseDirectory, "final_card.json");
        File.WriteAllText(basePath, """
            { "id":"final_card", "name":"Old Base", "activeVersion":"v1",
              "versions":[{ "version":"v1", "spec":{
                "rarity":1, "cardType":"Skill", "cost":3, "effects":[]
              } }] }
            """);
        using var doc = JsonDocument.Parse("""
            {
              "id":"final_card", "name":"Final Card", "activeVersion":"v3",
              "versions":[
                { "version":"v1", "label":"old", "spec":{ "rarity":1, "cardType":"Skill", "cost":2, "effects":[] } },
                { "version":"v3", "label":"approved", "spec":{
                  "rarity":1, "cardType":"Skill", "cost":1,
                  "effects":[{ "action":"block", "scope":"self", "amount":7 }]
                } }
              ]
            }
            """);
        var overrideDocument = _service.Save("cards", "final_card", doc.RootElement, null);

        var published = _service.SaveActiveVersionAsStandardV1(
            "cards", "final_card", doc.RootElement, overrideDocument.Revision);

        Assert.True(published.HasBase);
        Assert.False(published.HasOverride);
        Assert.Equal("v1", published.Document["activeVersion"]!.GetValue<string>());
        var versions = published.Document["versions"]!.AsArray();
        Assert.Single(versions);
        Assert.Equal("v1", versions[0]!["version"]!.GetValue<string>());
        Assert.Null(versions[0]!["label"]);
        Assert.Equal(7, versions[0]!["spec"]!["effects"]![0]!["amount"]!.GetValue<int>());

        Assert.True(File.Exists(basePath));
        Assert.False(File.Exists(Path.Combine(_provider.OverrideRoot, "cards", "final_card.json")));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "data-local", "backups", "cards"), "final_card-*.json"));
        Assert.Equal(1, _provider.Current.Cards["final_card"].Cost);

        // A later rebuild must keep using the promoted source while this process still has old embedded resources.
        _provider.Rebuild();
        Assert.Equal(1, _provider.Current.Cards["final_card"].Cost);
    }

    [Fact]
    public void Repository_development_overrides_form_a_valid_authoring_catalog()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", ".."));
        var environment = new TestEnvironment(Path.Combine(repositoryRoot, "src", "Server"));

        var provider = new DataCatalogProvider(environment);

        Assert.NotEmpty(provider.Current.Cards);
        var limitedCard = provider.Current.Cards["striker_epic_form_copy"];
        Assert.Equal(1, limitedCard.UsesPerTurn);
        Assert.Equal(1, limitedCard.UpgradedUsesPerTurn);
        Assert.DoesNotContain("reward_common_04", provider.Current.Cards.Keys);
        Assert.DoesNotContain("reward_common_07", provider.Current.Cards.Keys);
        Assert.DoesNotContain("reward_epic_03", provider.Current.Cards.Keys);
        Assert.DoesNotContain("reward_rare_06", provider.Current.Cards.Keys);
        Assert.DoesNotContain("reward_rare_07", provider.Current.Cards.Keys);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Server.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
