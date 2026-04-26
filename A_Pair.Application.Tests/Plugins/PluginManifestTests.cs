using System.Text.Json;
using A_Pair.Application.Plugins;

namespace A_Pair.Application.Tests.Plugins;

public class PluginManifestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true ,
        PropertyNamingPolicy = null // 保持原样，因为清单使用小写属性名
    };

    [Fact]
    public void DefaultValues_ShouldBeExpected ()
    {
        var manifest = new PluginManifest();
        manifest.Id.Should().BeEmpty();
        manifest.Name.Should().BeEmpty();
        manifest.Version.Should().Be("1.0.0");
        manifest.Description.Should().BeEmpty();
        manifest.Author.Should().BeEmpty();
        manifest.Assembly.Should().BeEmpty();
        manifest.Type.Should().BeEmpty();
        manifest.Priority.Should().Be(50);
        manifest.Enabled.Should().BeTrue();
        manifest.Dependencies.Should().BeEmpty();
        manifest.ScriptFile.Should().BeNull();
        manifest.ScriptType.Should().BeNull();
    }

    [Fact]
    public void Serialize_ShouldUseLowercasePropertyNames ()
    {
        var manifest = new PluginManifest
        {
            Id = "test-plugin" ,
            Name = "Test" ,
            Priority = 10
        };
        var json = JsonSerializer.Serialize(manifest , JsonOptions);
        json.Should().Contain("\"id\":");
        json.Should().Contain("\"name\":");
        json.Should().Contain("\"priority\":");
    }

    [Fact]
    public void Deserialize_FromJson_ShouldSetProperties ()
    {
        const string json = @"
        {
            ""id"": ""plugin1"",
            ""name"": ""Sample"",
            ""priority"": 30,
            ""enabled"": false,
            ""scriptFile"": ""script.lua"",
            ""scriptType"": ""lua""
        }";
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json , JsonOptions);
        manifest.Should().NotBeNull();
        manifest!.Id.Should().Be("plugin1");
        manifest.Name.Should().Be("Sample");
        manifest.Priority.Should().Be(30);
        manifest.Enabled.Should().BeFalse();
        manifest.ScriptFile.Should().Be("script.lua");
        manifest.ScriptType.Should().Be("lua");
    }
}