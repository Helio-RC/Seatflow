using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace A_Pair.Application.Plugins
{
    public class PluginManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("author")]
        public string Author { get; set; } = string.Empty;

        [JsonPropertyName("assembly")]
        public string Assembly { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("priority")]
        public int Priority { get; set; } = 50;

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("dependencies")]
        public List<string> Dependencies { get; set; } = new();

        [JsonPropertyName("scriptFile")]
        public string? ScriptFile { get; set; }

        [JsonPropertyName("scriptType")]
        public string? ScriptType { get; set; } // "Lua" or "CSharp"
    }
}