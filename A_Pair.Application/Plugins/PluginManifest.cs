using System.Text.Json.Serialization;

namespace A_Pair.Application.Plugins
{
    public class PluginManifest
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("assembly")]
        public string Assembly { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }
}
