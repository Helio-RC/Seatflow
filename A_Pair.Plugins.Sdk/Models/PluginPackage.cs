using System.IO.Compression;
using System.Text.Json;

namespace A_Pair.Plugins.Sdk.Models;

/// <summary>
/// 插件打包工具，支持将插件包目录打包为 <c>.ap-plugin</c> 文件以及反向解包。
/// 同时兼容旧 <c>.apairplugin</c> 格式。
/// </summary>
/// <remarks>
/// <para><c>.ap-plugin</c> 格式规范（新）：</para>
/// <list type="bullet">
///   <item>本质为 ZIP 文件，仅后缀名改为 <c>.ap-plugin</c></item>
///   <item>必须包含 <c>plugins-manifest.json</c>（包级清单）</item>
///   <item>每个策略子组件位于独立子目录，含 <c>manifest.json</c>（策略元数据）</item>
///   <item>可选文件：<c>data/enables.json</c>、<c>icon.png</c></item>
/// </list>
/// <para><c>.apairplugin</c> 格式规范（旧）：</para>
/// <list type="bullet">
///   <item>内部采用扁平结构，所有文件位于根目录</item>
///   <item>必须包含 <c>plugin.manifest.json</c></item>
///   <item>可选文件：<c>config.json</c>、<c>icon.png</c></item>
/// </list>
/// </remarks>
public static class PluginPackage
{
    /// <summary>
    /// 旧 .apairplugin 文件的扩展名。
    /// </summary>
    public const string OldExtension = ".apairplugin";

    /// <summary>
    /// 新 .ap-plugin 文件的扩展名。
    /// </summary>
    public const string NewExtension = ".ap-plugin";

    /// <summary>
    /// 所有受支持的插件包扩展名列表。
    /// </summary>
    public static readonly string[] SupportedExtensions = [OldExtension , NewExtension];

    /// <summary>
    /// 将插件包目录打包为 <c>.ap-plugin</c> 文件（新格式）。
    /// </summary>
    /// <param name="packageDir">插件包目录路径（须包含 <c>plugins-manifest.json</c>）。</param>
    /// <param name="outputPath">输出的 <c>.ap-plugin</c> 文件路径。</param>
    /// <exception cref="DirectoryNotFoundException">包目录不存在。</exception>
    /// <exception cref="FileNotFoundException">包清单文件不存在。</exception>
    public static void Create (string packageDir , string outputPath)
    {
        Create(packageDir , outputPath , useNewFormat: true);
    }

    /// <summary>
    /// 将插件包目录打包为指定格式的包文件。
    /// </summary>
    /// <param name="packageDir">插件包目录路径。</param>
    /// <param name="outputPath">输出的包文件路径。</param>
    /// <param name="useNewFormat">
    /// <c>true</c> — 使用新格式（要求 <c>plugins-manifest.json</c>），输出 <c>.ap-plugin</c>；
    /// <c>false</c> — 使用旧格式（要求 <c>plugin.manifest.json</c>），输出 <c>.apairplugin</c>。
    /// </param>
    /// <exception cref="DirectoryNotFoundException">包目录不存在。</exception>
    /// <exception cref="FileNotFoundException">对应的清单文件不存在。</exception>
    public static void Create (string packageDir , string outputPath , bool useNewFormat)
    {
        if (!Directory.Exists(packageDir))
            throw new DirectoryNotFoundException($"插件包目录不存在：{packageDir}");

        var manifestFileName = useNewFormat ? "plugins-manifest.json" : "plugin.manifest.json";
        var manifestPath = Path.Combine(packageDir , manifestFileName);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"插件包清单文件不存在：{manifestPath}");

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        ZipFile.CreateFromDirectory(packageDir , outputPath , CompressionLevel.Optimal , includeBaseDirectory: false);
    }

    /// <summary>
    /// 将插件包文件解包到目标目录，自动处理嵌套单层目录。
    /// </summary>
    /// <param name="packagePath">包文件路径（<c>.ap-plugin</c> 或 <c>.apairplugin</c>）。</param>
    /// <param name="targetDir">解包目标目录（不存在则自动创建）。</param>
    /// <param name="stripSingleFolder">
    /// <c>true</c> — 启用防嵌套：若包内恰好只有 1 个目录 + 0 个文件，则剥离外层目录；
    /// <c>false</c> — 直接解包，不做剥离。默认为 <c>true</c>。
    /// </param>
    /// <exception cref="FileNotFoundException">包文件不存在。</exception>
    /// <exception cref="InvalidDataException">包内缺少清单文件。</exception>
    public static void Extract (string packagePath , string targetDir , bool stripSingleFolder = true)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException($"插件包文件不存在：{packagePath}");

        Directory.CreateDirectory(targetDir);

        if (stripSingleFolder)
        {
            // 先解压到临时目录，检查是否需要剥离外层
            var tempDir = Path.Combine(Path.GetTempPath() , $"ap_plugin_extract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                ZipFile.ExtractToDirectory(packagePath , tempDir , overwriteFiles: true);

                var entries = Directory.GetFileSystemEntries(tempDir);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    // 被嵌套在单层文件夹中，剥离外层：将内容上移
                    var innerDir = entries[0];
                    foreach (var item in Directory.GetFileSystemEntries(innerDir))
                    {
                        var dest = Path.Combine(tempDir , Path.GetFileName(item));
                        if (Directory.Exists(item))
                            Directory.Move(item , dest);
                        else
                            File.Move(item , dest);
                    }
                    Directory.Delete(innerDir);
                }

                // 将临时目录内容移动到目标目录
                foreach (var item in Directory.GetFileSystemEntries(tempDir))
                {
                    var dest = Path.Combine(targetDir , Path.GetFileName(item));
                    if (Directory.Exists(item))
                        Directory.Move(item , dest);
                    else
                        File.Move(item , dest);
                }
            }
            finally
            {
                try { Directory.Delete(tempDir , recursive: true); }
                catch { /* 忽略清理失败 */ }
            }
        }
        else
        {
            ZipFile.ExtractToDirectory(packagePath , targetDir , overwriteFiles: true);
        }

        // 验证至少存在一种清单
        var hasNewManifest = File.Exists(Path.Combine(targetDir , "plugins-manifest.json"));
        var hasOldManifest = File.Exists(Path.Combine(targetDir , "plugin.manifest.json"));
        if (!hasNewManifest && !hasOldManifest)
            throw new InvalidDataException("插件包内缺少 plugins-manifest.json 或 plugin.manifest.json 文件");
    }

    /// <summary>
    /// 验证插件包文件的结构完整性（支持新旧两种格式）。
    /// </summary>
    /// <param name="packagePath">包文件路径。</param>
    /// <returns>验证结果：成功返回空字符串，失败返回错误描述。</returns>
    public static async Task<string> ValidateAsync (string packagePath)
    {
        if (!File.Exists(packagePath))
            return "插件包文件不存在";

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);

            var hasNewManifest = archive.Entries.Any(e =>
                string.Equals(e.Name , "plugins-manifest.json" , StringComparison.OrdinalIgnoreCase));
            var hasOldManifest = archive.Entries.Any(e =>
                string.Equals(e.Name , "plugin.manifest.json" , StringComparison.OrdinalIgnoreCase));

            if (!hasNewManifest && !hasOldManifest)
                return "插件包内缺少 plugins-manifest.json 或 plugin.manifest.json 文件";

            if (hasNewManifest)
            {
                // 校验新格式
                var newManifestEntry = archive.Entries.First(e =>
                    string.Equals(e.Name , "plugins-manifest.json" , StringComparison.OrdinalIgnoreCase));
                await using var newStream = newManifestEntry.Open();

                try
                {
                    using var doc = await JsonDocument.ParseAsync(newStream);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id" , out var idElem) || idElem.GetString() is not string id || string.IsNullOrEmpty(id))
                        return "plugins-manifest.json 格式无效：缺少 id 字段";

                    if (!root.TryGetProperty("strategies" , out var strategiesElem) ||
                        strategiesElem.ValueKind != JsonValueKind.Array ||
                        strategiesElem.GetArrayLength() == 0)
                        return "plugins-manifest.json 格式无效：strategies 数组为空或缺失";
                }
                catch (JsonException ex)
                {
                    return $"plugins-manifest.json JSON 格式无效：{ex.Message}";
                }

                // 检查策略子目录中的 manifest.json
                // 注意：在 ZIP 流中遍历子目录受限，此处仅做基本校验
            }
            else
            {
                // 校验旧格式
                var oldManifestEntry = archive.Entries.First(e =>
                    string.Equals(e.Name , "plugin.manifest.json" , StringComparison.OrdinalIgnoreCase));
                await using var oldStream = oldManifestEntry.Open();

                try
                {
                    using var doc = await JsonDocument.ParseAsync(oldStream);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("id" , out var idElem) || idElem.GetString() is not string id || string.IsNullOrEmpty(id))
                        return "plugin.manifest.json 格式无效：缺少 id 字段";
                }
                catch (JsonException ex)
                {
                    return $"plugin.manifest.json JSON 格式无效：{ex.Message}";
                }
            }

            // 检查至少有一个插件文件（.dll、.lua、.csx）
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
        catch (IOException ex)
        {
            return $"读取插件包时发生 I/O 错误：{ex.Message}";
        }
    }

    /// <summary>
    /// 不解包直接读取插件包清单，用于预览包内容。
    /// 自动检测新旧格式。
    /// </summary>
    /// <param name="packagePath">包文件路径。</param>
    /// <returns>插件清单信息；如果读取失败则返回 <c>null</c>。</returns>
    public static async Task<PluginManifestStub?> GetManifestAsync (string packagePath)
    {
        if (!File.Exists(packagePath))
            return null;

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);

            // 优先查找新格式
            var newManifestEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name , "plugins-manifest.json" , StringComparison.OrdinalIgnoreCase));

            if (newManifestEntry is not null)
            {
                await using var stream = newManifestEntry.Open();
                using var doc = await JsonDocument.ParseAsync(stream);
                var root = doc.RootElement;

                var stub = new PluginManifestStub();
                if (root.TryGetProperty("id" , out var idElem))
                    stub.Id = idElem.GetString() ?? string.Empty;
                if (root.TryGetProperty("name" , out var nameElem))
                    stub.Name = nameElem.GetString() ?? string.Empty;
                if (root.TryGetProperty("version" , out var verElem))
                    stub.Version = verElem.GetString() ?? "1.0.0";
                if (root.TryGetProperty("type" , out var typeElem))
                    stub.Category = typeElem.GetString() ?? "strategy";
                if (root.TryGetProperty("description" , out var descElem))
                    stub.Description = descElem.GetString() ?? string.Empty;
                if (root.TryGetProperty("author" , out var authorElem))
                    stub.Author = authorElem.GetString() ?? string.Empty;

                return stub;
            }

            // 回退到旧格式
            var oldManifestEntry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name , "plugin.manifest.json" , StringComparison.OrdinalIgnoreCase));

            if (oldManifestEntry is null)
                return null;

            await using var oldStream = oldManifestEntry.Open();
            return await JsonSerializer.DeserializeAsync<PluginManifestStub>(oldStream ,
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
/// 兼容新旧两种格式的部分字段。
/// </summary>
public class PluginManifestStub
{
    /// <summary>插件/包唯一标识符。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>插件/包显示名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>插件/包版本号。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>功能类别。</summary>
    public string Category { get; set; } = "strategy";

    /// <summary>描述。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>作者。</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>程序集文件名（旧格式程序集插件）。</summary>
    public string Assembly { get; set; } = string.Empty;

    /// <summary>入口类型完全限定名（旧格式程序集插件）。</summary>
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
    public Dictionary<string , Dictionary<string , string>>? Messages { get; set; }

    /// <summary>脚本文件名（旧格式脚本插件）。</summary>
    public string? ScriptFile { get; set; }

    /// <summary>脚本类型（"lua" 或 "csharp"）。</summary>
    public string? ScriptType { get; set; }
}
