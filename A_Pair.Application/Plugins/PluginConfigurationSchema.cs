namespace A_Pair.Application.Plugins
{
    public class PluginConfigurationSchema
    {
        public List<ConfigField> Fields { get; set; } = [];
    }

    public class ConfigField
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Type { get; set; } = "string"; // string, number, boolean, select
        public object? DefaultValue { get; set; }
        public Dictionary<string , object>? Options { get; set; }
    }
}