using System.IO.Compression;
using System.Text.Json;

namespace A_Pair.Plugins.Sdk.Models;

/// <summary>
/// 插件打包工具，支持将插件目录打包为 <c>.apairplugin</c> 文件以及反向解包。
/// </summary>
/// <remarks>
/// <para><c>.apairplugin</c> 格式规范：</para>
/// <list type="bullet">
///   <item>本质为 ZIP 文件，仅后缀名改为 <c>.apairplugin</c></item>
///   <item>内部采用扁平结构，所有文件位于根目录</item>
///   <item>必须包含 <c>plugin.manifest.json</c></item>
///   <item>可包含 <c>.dll</c>、<c>.lua</c>、<c>.csx</c> 等插件文件</item>
///   <item>可选文件：<c>config.json</c>、<c>icon.png</c></item>
/// </list>
/// <para>清单中的 <c>assembly</c> 和 <c>scriptFile</c> 字段应仅使用文件名（不含路径）。</para>
/// </remarks>
public static class PluginPackage
{
    /// <summary>
    /// .apairplugin 文件的扩展名。
    /// </summary>
    public const string Extension = ".apairplugin";

    /// <summary>
    /// 将插件目录打包为 <c>.apairplugin</c> 文件。
    /// </summary>
    /// <param name="pluginDir">插件目录路径（须包含 <c>plugin.manifest.json</c>）。</param>
    /// <param name="outputPath">输出的 <c>.apairplugin</c> 文件路径。</param>
    /// <exception cref="FileNotFoundException">清单文件不存在。</exception>
    public static void Create (string pluginDir , string outputPath)
    {
        if (!Directory.Exists(pluginDir))
            throw new DirectoryNotFoundException($"插件目录不存在：{pluginDir}");

        var manifestPath = Path.Combine(pluginDir , "plugin.manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"插件清单文件不存在：{manifestPath}");

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        ZipFile.CreateFromDirectory(pluginDir , outputPath , CompressionLevel.Optimal , includeBaseDirectory: false);
    }

    /// <summary>
    /// 将 <c>.apairplugin</c> 文件解包到目标目录。
    /// </summary>
    /// <param name="packagePath"><c>.apairplugin</c> 文件路径。</param>
    /// <param name="targetDir">解包目标目录（不存在则自动创建）。</param>
    /// <exception cref="FileNotFoundException">包文件不存在。</exception>
    /// <exception cref="InvalidDataException">包内缺少 <c>plugin.manifest.json</c>。</exception>
    public static void Extract (string packagePath , string targetDir)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException($"插件包文件不存在：{packagePath}");

        Directory.CreateDirectory(targetDir);
        ZipFile.ExtractToDirectory(packagePath , targetDir , overwriteFiles: true);

        var manifestPath = Path.Combine(targetDir , "plugin.manifest.json");
        if (!File.Exists(manifestPath))
            throw new InvalidDataException("插件包内缺少 plugin.manifest.json 文件");
    }

    /// <summary>
    /// 验证 <c>.apairplugin</c> 文件的结构完整性。
    /// </summary>
    /// <param name="packagePath"><c>.apairplugin</c> 文件路径。</param>
    /// <returns>验证结果：成功返回空字符串，失败返回错误描述。</returns>
    public static async Task<string> ValidateAsync (string packagePath)
    {
        if (!File.Exists(packagePath))
            return "插件包文件不存在";

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);

            var hasManifest = archive.Entries.Any(e =>
                string.Equals(e.Name , "plugin.manifest.json" , StringComparison.OrdinalIgnoreCase));

            if (!hasManifest)
                return "插件包内缺少 plugin.manifest.json 文件";

            var manifestEntry = archive.Entries.First(e =>
                string.Equals(e.Name , "plugin.manifest.json" , StringComparison.OrdinalIgnoreCase));

            await using var stream = manifestEntry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<PluginManifestStub>(stream);

            if (manifest is null || string.IsNullOrEmpty(manifest.Id))
                return "plugin.manifest.json 格式无效：缺少 id 字段";

            var hasPluginFile = archive.Entries.Any(e =>
            {
                var ext = Path.GetExtension(e.Name).ToLowerInvariant();
                return ext is ".dll" or ".lua" or ".csx";
            });

            if (!hasPluginFile)
                return "插件包内未找到任何插件文件（.dll、.lua 或 .csx）";

            return string.Empty;
        }
        catch (InvalidDataException)
        {
            return "插件包文件格式无效（非有效的 ZIP 文件）";
        }
        catch (JsonException ex)
        {
            return $"plugin.manifest.json 格式无效：{ex.Message}";
        }
        catch (IOException ex)
        {
            return $"读取插件包时发生 I/O 错误：{ex.Message}";
        }
    }

    /// <summary>
    /// 不解包直接读取插件清单，用于预览包内容。
    /// </summary>
    /// <param name="packagePath"><c>.apairplugin</c> 文件路径。</param>
    /// <returns>插件清单信息；如果读取失败则返回 <c>null</c>。</returns>
    public static async Task<PluginManifestStub?> GetManifestAsync (string packagePath)
    {
        if (!File.Exists(packagePath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);

            var manifestEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name , "plugin.manifest.json" , StringComparison.OrdinalIgnoreCase));

            if (manifestEntry is null)
                return null;

            await using var stream = manifestEntry.Open();
            return await JsonSerializer.DeserializeAsync<PluginManifestStub>(stream ,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 插件清单的轻量级存根，用于解包时快速读取关键字段。
/// 字段定义与 <see cref="A_Pair.Application.Plugins.PluginManifest"/> 保持一致。
/// 修改 PluginManifest 时请同步更新此类。
/// </summary>
public class PluginManifestStub
{
    /// <summary>插件唯一标识符。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>插件显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>插件版本号。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>插件功能类别。</summary>
    public string Category { get; set; } = "strategy";

    /// <summary>插件描述。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>插件作者。</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>程序集文件名（程序集插件）。</summary>
    public string Assembly { get; set; } = string.Empty;

    /// <summary>入口类型完全限定名（程序集插件）。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>执行优先级。</summary>
    public int Priority { get; set; } = 50;

    /// <summary>是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>是否可见（默认 true）。false 时从 UI 和执行管道中完全排除。</summary>
    public bool Visible { get; set; } = true;

    /// <summary>策略级全局参数声明（可选）。使用 JsonElement 保留原始 JSON，避免 SDK 依赖 Core 类型。</summary>
    public System.Text.Json.JsonElement? Parameters { get; set; }

    /// <summary>按数据集/会场的配置块声明（可选）。</summary>
    public System.Text.Json.JsonElement? CodeBlocks { get; set; }

    /// <summary>策略执行消息的多语言模板（可选）。</summary>
    public Dictionary<string, Dictionary<string, string>>? Messages { get; set; }

    /// <summary>脚本文件名（脚本插件）。</summary>
    public string? ScriptFile { get; set; }

    /// <summary>脚本类型（"lua" 或 "csharp"）。</summary>
    public string? ScriptType { get; set; }
}
