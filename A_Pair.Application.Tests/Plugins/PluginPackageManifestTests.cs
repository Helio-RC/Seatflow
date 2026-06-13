using System.Text.Json;

namespace A_Pair.Application.Tests.Plugins;

public class PluginPackageManifestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void DefaultValues_ShouldBeExpected ()
    {
        var manifest = new PluginPackageManifest();
        manifest.Name.Should().BeEmpty();
        manifest.Id.Should().BeEmpty();
        manifest.Version.Should().Be("1.0.0");
        manifest.Author.Should().BeEmpty();
        manifest.Description.Should().BeEmpty();
        manifest.Type.Should().Be("strategy");
        manifest.Strategies.Should().BeEmpty();
        manifest.Repository.Should().BeNull();
        manifest.Website.Should().BeNull();
    }

    [Fact]
    public void Serialize_ShouldUseCamelCasePropertyNames ()
    {
        var manifest = new PluginPackageManifest
        {
            Id = "test-pkg" ,
            Name = "Test Package" ,
            Version = "1.0.0" ,
            Strategies =
            [
                new PluginStrategyEntry
                {
                    Path = "my_strategy" ,
                    Manifest = "my_strategy/manifest.json" ,
                    Assembly = "MyStrategy.dll" ,
                    EntryType = "MyPlugin.MyStrategy"
                }
            ]
        };

        var json = JsonSerializer.Serialize(manifest , JsonOptions);

        json.Should().Contain("\"id\":");
        json.Should().Contain("\"name\":");
        json.Should().Contain("\"strategies\":");
        json.Should().Contain("\"path\":");
        json.Should().Contain("\"manifest\":");
        json.Should().Contain("\"assembly\":");
        json.Should().Contain("\"entryType\":");
    }

    [Fact]
    public void Deserialize_FromJson_ShouldSetProperties ()
    {
        const string json = """
        {
            "id": "my-pkg",
            "name": "My Package",
            "version": "2.0.0",
            "author": "Author",
            "description": "A test package",
            "type": "strategy",
            "strategies": [
                {
                    "path": "strat1",
                    "manifest": "strat1/manifest.json",
                    "assembly": "Strat1.dll",
                    "entryType": "MyPlugin.Strategy1"
                },
                {
                    "path": "strat2",
                    "manifest": "strat2/manifest.json",
                    "scriptFile": "script.lua",
                    "scriptType": "lua"
                }
            ]
        }
        """;

        var manifest = JsonSerializer.Deserialize<PluginPackageManifest>(json , JsonOptions);
        manifest.Should().NotBeNull();
        manifest!.Id.Should().Be("my-pkg");
        manifest.Name.Should().Be("My Package");
        manifest.Version.Should().Be("2.0.0");
        manifest.Author.Should().Be("Author");
        manifest.Description.Should().Be("A test package");
        manifest.Type.Should().Be("strategy");
        manifest.Strategies.Should().HaveCount(2);

        manifest.Strategies[0].Path.Should().Be("strat1");
        manifest.Strategies[0].Assembly.Should().Be("Strat1.dll");
        manifest.Strategies[0].EntryType.Should().Be("MyPlugin.Strategy1");

        manifest.Strategies[1].Path.Should().Be("strat2");
        manifest.Strategies[1].ScriptFile.Should().Be("script.lua");
        manifest.Strategies[1].ScriptType.Should().Be("lua");
    }

    [Fact]
    public void PluginStrategyEntry_Defaults_ShouldBeEmpty ()
    {
        var entry = new PluginStrategyEntry();
        entry.Path.Should().BeEmpty();
        entry.Manifest.Should().BeEmpty();
        entry.Assembly.Should().BeNull();
        entry.EntryType.Should().BeNull();
        entry.ScriptFile.Should().BeNull();
        entry.ScriptType.Should().BeNull();
    }

    [Fact]
    public void PluginEnables_Defaults_ShouldBeExpected ()
    {
        var enables = new PluginEnables();
        enables.Enabled.Should().BeTrue();
        enables.Type.Should().Be("strategy");
        enables.Strategies.Should().BeEmpty();
    }

    [Fact]
    public void PluginEnables_SerializeDeserialize_RoundTrip ()
    {
        var enables = new PluginEnables
        {
            Enabled = false ,
            Type = "strategy" ,
            Strategies = new Dictionary<string , bool>
            {
                ["strat-a"] = true ,
                ["strat-b"] = false
            }
        };

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true ,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(enables , options);
        var deserialized = JsonSerializer.Deserialize<PluginEnables>(json , options);

        deserialized.Should().NotBeNull();
        deserialized!.Enabled.Should().BeFalse();
        deserialized.Strategies["strat-a"].Should().BeTrue();
        deserialized.Strategies["strat-b"].Should().BeFalse();
    }
}
