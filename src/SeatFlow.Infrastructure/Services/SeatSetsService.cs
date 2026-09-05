using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SeatFlow.Core.Interfaces;
using SeatFlow.Core.Models.SeatSets;
using SeatFlow.Core.Storage;
using SeatFlow.Core.Utilities;
using SeatFlow.Infrastructure.Serialization;
using SeatFlow.Infrastructure.Utils;

namespace SeatFlow.Infrastructure.Services;

/// <summary>
/// .seatsets 数据包文件的核心服务实现。
/// 负责导出（收集文件→构建存档→计算哈希→序列化）、
/// 导入（校验→解析→逐文件恢复）、校验、自动发现和探测类别。
/// </summary>
public class SeatSetsService : ISeatSetsService
{
    private readonly string? _effectiveDataPath;
    private readonly string? _settingsFilePath;
    private readonly ILocalDataStore? _store;
    private readonly ILogger<SeatSetsService> _logger;

    /// <summary>
    /// 初始化 .seatsets 服务（路径模式，桌面端）。
    /// </summary>
    /// <param name="effectiveDataPath">有效数据目录路径（Venues/Rosters/Assignments/StrategyConfig 的父目录）。</param>
    /// <param name="settingsFilePath">AppSettings.json 文件的完整路径。</param>
    /// <param name="logger">日志记录器。</param>
    public SeatSetsService (
        string effectiveDataPath ,
        string settingsFilePath ,
        ILogger<SeatSetsService>? logger = null)
    {
        _effectiveDataPath = effectiveDataPath;
        _settingsFilePath = settingsFilePath;
        _logger = logger ?? NullLogger<SeatSetsService>.Instance;
    }

    /// <summary>
    /// 初始化 .seatsets 服务（存储抽象版本，WASM/浏览器端）。
    /// </summary>
    /// <param name="store">数据存储实现（IndexedDB）。</param>
    /// <param name="logger">日志记录器。</param>
    public SeatSetsService (
        ILocalDataStore store ,
        ILogger<SeatSetsService>? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? NullLogger<SeatSetsService>.Instance;
    }

    // ──────────────────────────────── 导出 ────────────────────────────────

    /// <inheritdoc />
    public Task<int> ExportAsync (string outputPath , SeatSetsExportSelection selection ,
        CancellationToken ct = default)
    {
        return ExportCoreAsync(selection , outputPath , ct);
    }

    /// <inheritdoc />
    public async Task<byte[]?> ExportBytesAsync (SeatSetsExportSelection selection ,
        CancellationToken ct = default)
    {
        var (archive , _) = await BuildArchiveAsync(selection , ct);
        if (archive is null) return null;
        var json = JsonSerializer.Serialize(archive , JsonOptions.WriteIndentedCamelCase);
        return Encoding.UTF8.GetBytes(json);
    }

    private async Task<int> ExportCoreAsync (SeatSetsExportSelection selection , string outputPath , CancellationToken ct)
    {
        _logger.LogInformation("开始导出数据包: {Path}, 选择: {@Selection}" , outputPath , selection);

        var (archive , totalFiles) = await BuildArchiveAsync(selection , ct);
        if (archive is null)
        {
            _logger.LogWarning("没有可导出的数据");
            return 0;
        }

        // 序列化并写入文件
        var json = JsonSerializer.Serialize(archive , JsonOptions.WriteIndentedCamelCase);
        await File.WriteAllTextAsync(outputPath , json , ct);

        _logger.LogInformation("数据包导出完成: {Path}, {FileCount} 个文件, 大小 {Size} 字节" ,
            outputPath , totalFiles , new FileInfo(outputPath).Length);

        return totalFiles;
    }

    private async Task<(SeatSetsArchive? Archive , int TotalFiles)> BuildArchiveAsync (SeatSetsExportSelection selection , CancellationToken ct)
    {
        _logger.LogInformation("开始导出数据包, 选择: {@Selection}" , selection);

        var archive = new SeatSetsArchive
        {
            Version = SeatSetsConstants.CurrentFormatVersion ,
            AppVersion = GetAppVersion() ,
            CreatedAt = DateTime.UtcNow.ToString("O")
        };

        int totalFiles = 0;

        // 逐类别收集文件
        if (selection.IncludeAppSettings)
            totalFiles += await AddAppSettingsChunkAsync(archive , ct);

        if (selection.IncludeVenues)
            totalFiles += await AddVenuesChunkAsync(archive , ct);

        if (selection.IncludeRosters)
            totalFiles += await AddRostersChunkAsync(archive , ct);

        if (selection.IncludeSnapshots)
            totalFiles += await AddSnapshotsChunkAsync(archive , ct);

        if (selection.IncludeStrategyConfig)
            totalFiles += await AddStrategyConfigChunkAsync(archive , ct);

        if (totalFiles == 0)
        {
            _logger.LogWarning("没有可导出的数据");
            return (null , 0);
        }

        // 计算每块的哈希
        foreach (var (category , chunk) in archive.Chunks)
        {
            chunk.Hash = ComputeChunkHash(chunk.Files);
            _logger.LogDebug("Chunk {Category}: {FileCount} 个文件, Hash={Hash}" ,
                category , chunk.Files.Count , chunk.Hash);
        }

        // 计算整体归档哈希
        archive.ArchiveHash = ComputeArchiveHash(archive.Chunks);
        _logger.LogInformation("归档哈希: {Hash}, 总文件数: {Total}" , archive.ArchiveHash , totalFiles);

        return (archive , totalFiles);
    }

    // ──────────────────────────────── 导入 ────────────────────────────────

    /// <inheritdoc />
    public Task<SeatSetsImportResult> ImportAsync (string filePath , SeatSetsExportSelection selection ,
        IProgress<double>? progress = null , CancellationToken ct = default)
    {
        return ImportCoreAsync(filePath , selection , progress , ct);
    }

    /// <inheritdoc />
    public Task<SeatSetsImportResult> ImportBytesAsync (byte[] content , SeatSetsExportSelection selection ,
        IProgress<double>? progress = null , CancellationToken ct = default)
    {
        return ImportCoreAsync(content , selection , progress , ct);
    }

    private async Task<SeatSetsImportResult> ImportCoreAsync (string filePath , SeatSetsExportSelection selection ,
        IProgress<double>? progress , CancellationToken ct)
    {
        // 保持原有流程：先校验（文件存在/大小/哈希），失败即返回错误集合
        var validation = await ValidateAsync(filePath , ct);
        if (!validation.IsValid)
        {
            var result = new SeatSetsImportResult();
            result.Errors.AddRange(validation.ValidationErrors);
            return result;
        }

        var json = await File.ReadAllTextAsync(filePath , ct);
        return await RestoreAsync(json , selection , progress , ct);
    }

    private async Task<SeatSetsImportResult> ImportCoreAsync (byte[] content , SeatSetsExportSelection selection ,
        IProgress<double>? progress , CancellationToken ct)
    {
        var validation = await ValidateBytesAsync(content , ct);
        if (!validation.IsValid)
        {
            var result = new SeatSetsImportResult();
            result.Errors.AddRange(validation.ValidationErrors);
            return result;
        }

        var json = Encoding.UTF8.GetString(content);
        return await RestoreAsync(json , selection , progress , ct);
    }

    private async Task<SeatSetsImportResult> RestoreAsync (string json , SeatSetsExportSelection selection ,
        IProgress<double>? progress , CancellationToken ct)
    {
        _logger.LogInformation("开始导入数据包, 选择: {@Selection}" , selection);

        var result = new SeatSetsImportResult();

        // 2. 解析存档
        SeatSetsArchive? archive;
        try
        {
            archive = JsonSerializer.Deserialize<SeatSetsArchive>(json , JsonOptions.CaseInsensitiveRead);
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"无法解析存档文件: {ex.Message}");
            return result;
        }

        if (archive?.Chunks == null)
        {
            result.Errors.Add("无法解析存档文件");
            return result;
        }

        // 3. 对每个选中的类别逐文件恢复
        var selectedCategories = selection.GetSelectedCategories();
        var filesToRestore = new List<(string Category , string RelativePath , JsonElement Content)>();

        foreach (var category in selectedCategories)
        {
            if (!archive.Chunks.TryGetValue(category , out var chunk))
                continue;

            // 验证 chunk 哈希
            var computedHash = ComputeChunkHash(chunk.Files);
            if (!string.IsNullOrEmpty(chunk.Hash) &&
                !string.Equals(computedHash , chunk.Hash , StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Chunk {Category} 哈希不匹配，跳过该块全部 {Count} 个文件" ,
                    category , chunk.Files.Count);
                result.Skipped += chunk.Files.Count;
                result.Errors.Add($"数据块 [{category}] 哈希校验失败，已跳过");
                continue;
            }

            foreach (var (relPath , content) in chunk.Files)
            {
                filesToRestore.Add((category , relPath , content));
            }
        }

        result.TotalFiles = filesToRestore.Count;
        _logger.LogInformation("共 {Count} 个文件待恢复" , result.TotalFiles);

        // 4. 逐文件恢复（尽力而为，单个失败不中断）
        int processed = 0;
        foreach (var (category , relPath , content) in filesToRestore)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var rawJson = content.GetRawText();

                if (_store is not null)
                {
                    // 存储抽象模式（WASM）：相对路径写入 store；路径穿越已在上游校验
                    if (relPath.Contains(".."))
                        throw new InvalidOperationException($"路径包含非法段: {relPath}");
                    var storePath = category == SeatSetsConstants.CategoryAppSettings
                        ? "AppSettings.json"
                        : relPath;
                    await _store.WriteTextAsync(storePath , rawJson , ct);
                }
                else
                {
                    var targetPath = ResolveTargetPath(category , relPath);

                    // 确保目标目录存在
                    var dir = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    // 原子写入：先写临时文件，再重命名
                    var tempPath = targetPath + ".tmp";
                    await File.WriteAllTextAsync(tempPath , rawJson , ct);
                    File.Move(tempPath , targetPath , overwrite: true);
                }

                result.Restored++;
                _logger.LogDebug("已恢复: {Path}" , relPath);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{relPath}: {ex.Message}");
                _logger.LogError(ex , "恢复文件失败: {Path}" , relPath);
            }

            processed++;
            progress?.Report((double)processed / result.TotalFiles);
        }

        _logger.LogInformation("导入完成: 成功 {Restored}/{Total}, 跳过 {Skipped}, 错误 {ErrCount}" ,
            result.Restored , result.TotalFiles , result.Skipped , result.Errors.Count);

        return result;
    }

    // ──────────────────────────────── 校验 ────────────────────────────────

    /// <inheritdoc />
    public async Task<SeatSetsValidationResult> ValidateAsync (string filePath ,
        CancellationToken ct = default)
    {
        var result = new SeatSetsValidationResult();

        try
        {
            // 文件大小检查
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                result.ValidationErrors.Add("文件不存在");
                return result;
            }

            result.FileSize = fileInfo.Length;
            if (fileInfo.Length > SeatSetsConstants.MaxFileSizeBytes)
            {
                result.ValidationErrors.Add($"文件过大（{fileInfo.Length / (1024.0 * 1024.0):F1} MB），"
                    + $"上限为 {SeatSetsConstants.MaxFileSizeBytes / (1024 * 1024)} MB");
                return result;
            }

            var json = await File.ReadAllTextAsync(filePath , ct);
            return ValidateContentCore(json , result , filePath , ct);
        }
        catch (Exception ex)
        {
            result.ValidationErrors.Add($"校验异常: {ex.Message}");
            _logger.LogError(ex , "校验文件时发生异常: {Path}" , filePath);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<SeatSetsValidationResult> ValidateBytesAsync (byte[] content ,
        CancellationToken ct = default)
    {
        var result = new SeatSetsValidationResult();

        try
        {
            result.FileSize = content.Length;
            if (content.Length > SeatSetsConstants.MaxFileSizeBytes)
            {
                result.ValidationErrors.Add($"文件过大（{content.Length / (1024.0 * 1024.0):F1} MB），"
                    + $"上限为 {SeatSetsConstants.MaxFileSizeBytes / (1024 * 1024)} MB");
                return result;
            }

            var json = Encoding.UTF8.GetString(content);
            return ValidateContentCore(json , result , "(bytes)" , ct);
        }
        catch (Exception ex)
        {
            result.ValidationErrors.Add($"校验异常: {ex.Message}");
            _logger.LogError(ex , "校验字节内容时发生异常");
        }

        return result;
    }

    /// <summary>
    /// 内容级校验（JSON 结构、版本、哈希、路径穿越），filePath/bytes 两个入口共用。
    /// </summary>
    private SeatSetsValidationResult ValidateContentCore (string json , SeatSetsValidationResult result ,
        string source , CancellationToken ct)
    {
        SeatSetsArchive? archive;
        try
        {
            archive = JsonSerializer.Deserialize<SeatSetsArchive>(json , JsonOptions.CaseInsensitiveRead);
        }
        catch (JsonException ex)
        {
            result.ValidationErrors.Add($"JSON 格式无效: {ex.Message}");
            return result;
        }

        if (archive == null)
        {
            result.ValidationErrors.Add("存档内容为空");
            return result;
        }

        result.Version = archive.Version;
        result.AppVersion = archive.AppVersion;
        result.AvailableCategories = archive.Chunks.Keys.ToList();

        // 检查版本兼容性
        if (string.IsNullOrEmpty(archive.Version))
        {
            result.ValidationErrors.Add("缺少格式版本号");
            return result;
        }

        // 验证各块的哈希
        foreach (var (category , chunk) in archive.Chunks)
        {
            if (chunk.Files.Count == 0) continue;
            var computed = ComputeChunkHash(chunk.Files);
            if (!string.IsNullOrEmpty(chunk.Hash) &&
                !string.Equals(computed , chunk.Hash , StringComparison.OrdinalIgnoreCase))
            {
                result.ValidationErrors.Add($"数据块 [{category}] 哈希不匹配");
            }
        }

        // 路径穿越检测：所有文件路径不得包含 ".." 段
        foreach (var (category , chunk) in archive.Chunks)
        {
            foreach (var relPath in chunk.Files.Keys)
            {
                if (relPath.Contains(".."))
                {
                    result.ValidationErrors.Add(
                        $"路径穿越检测: 数据块 [{category}] 中的文件路径包含非法段 '..': {relPath}");
                }
            }
        }

        // 验证整体哈希
        if (!string.IsNullOrEmpty(archive.ArchiveHash))
        {
            var computedArchiveHash = ComputeArchiveHash(archive.Chunks);
            result.ArchiveHashValid = string.Equals(computedArchiveHash , archive.ArchiveHash ,
                StringComparison.OrdinalIgnoreCase);
            if (!result.ArchiveHashValid)
            {
                result.ValidationErrors.Add("归档哈希校验失败，文件可能已损坏");
            }
        }
        else
        {
            result.ArchiveHashValid = true; // 旧格式可能没有归档哈希
        }

        result.IsValid = result.ValidationErrors.Count == 0;
        _logger.LogInformation("校验完成: {Path}, Valid={Valid}, Size={Size}, Categories={Cats}" ,
            source , result.IsValid , result.FileSize , result.AvailableCategories.Count);
        return result;
    }

    // ──────────────────────────────── 自动发现 ────────────────────────────────

    /// <inheritdoc />
    public Task<string?> DiscoverAsync (CancellationToken ct = default)
    {
#if BROWSER
        // 浏览器端无文件系统（WASM 沙箱），不支持 exe 目录发现
        return Task.FromResult<string?>(null);
#else
        var exeDir = AppEnvironment.ExeDirectory;
        if (!Directory.Exists(exeDir))
        {
            _logger.LogDebug("可执行文件目录不存在: {Dir}" , exeDir);
            return Task.FromResult<string?>(null);
        }

        try
        {
            var files = Directory.GetFiles(exeDir , $"*{SeatSetsConstants.FileExtension}" ,
                SearchOption.TopDirectoryOnly);

            if (files.Length == 0)
            {
                _logger.LogDebug("未在目录中发现 .seatsets 文件: {Dir}" , exeDir);
                return Task.FromResult<string?>(null);
            }

            // 取最新修改的文件
            var latest = files.Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .First();

            _logger.LogInformation("自动发现数据包: {Path} ({Size} 字节, 修改时间 {Time})" ,
                latest.FullName , latest.Length , latest.LastWriteTimeUtc);

            return Task.FromResult<string?>(latest.FullName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex , "自动发现 .seatsets 文件时出错");
            return Task.FromResult<string?>(null);
        }
#endif
    }

    // ──────────────────────────────── 探测类别 ────────────────────────────────

    /// <inheritdoc />
    public Task<SeatSetsExportSelection> ProbeCategoriesAsync (string filePath ,
        CancellationToken ct = default)
    {
        return ProbeCoreAsync(async () => await File.ReadAllTextAsync(filePath , ct) , filePath);
    }

    /// <inheritdoc />
    public Task<SeatSetsExportSelection> ProbeCategoriesBytesAsync (byte[] content ,
        CancellationToken ct = default)
    {
        return ProbeCoreAsync(() => Task.FromResult(Encoding.UTF8.GetString(content)) , "(bytes)");
    }

    private async Task<SeatSetsExportSelection> ProbeCoreAsync (
        Func<Task<string>> loadJson , string source)
    {
        var selection = new SeatSetsExportSelection
        {
            IncludeAppSettings = false ,
            IncludeVenues = false ,
            IncludeRosters = false ,
            IncludeSnapshots = false ,
            IncludeStrategyConfig = false
        };

        try
        {
            var json = await loadJson();
            var archive = JsonSerializer.Deserialize<SeatSetsArchive>(json , JsonOptions.CaseInsensitiveRead);
            if (archive?.Chunks == null)
                return selection;

            foreach (var category in archive.Chunks.Keys)
            {
                switch (category)
                {
                    case SeatSetsConstants.CategoryAppSettings:
                        selection.IncludeAppSettings = true;
                        break;
                    case SeatSetsConstants.CategoryVenues:
                        selection.IncludeVenues = true;
                        break;
                    case SeatSetsConstants.CategoryRosters:
                        selection.IncludeRosters = true;
                        break;
                    case SeatSetsConstants.CategorySnapshots:
                        selection.IncludeSnapshots = true;
                        break;
                    case SeatSetsConstants.CategoryStrategyConfig:
                        selection.IncludeStrategyConfig = true;
                        break;
                }
            }

            _logger.LogDebug("探测完成: {Path}, 类别: {Cats}" ,
                source , string.Join(", " , archive.Chunks.Keys));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex , "探测类别时出错: {Path}" , source);
        }

        return selection;
    }

    // ──────────────────────────────── 私有辅助方法 ────────────────────────────────

    /// <summary>
    /// 收集 AppSettings.json 文件内容。
    /// </summary>
    private async Task<int> AddAppSettingsChunkAsync (SeatSetsArchive archive , CancellationToken ct)
    {
        try
        {
            var json = _store is not null
                ? await _store.ReadTextAsync("AppSettings.json" , ct)
                : File.Exists(_settingsFilePath!) ? File.ReadAllText(_settingsFilePath!) : null;
            if (json is null)
            {
                _logger.LogDebug("AppSettings 文件不存在");
                return 0;
            }

            using var doc = JsonDocument.Parse(json);
            var chunk = GetOrCreateChunk(archive , SeatSetsConstants.CategoryAppSettings);
            // AppSettings.json 直接位于 AppData 根目录
            chunk.Files["AppSettings.json"] = doc.RootElement.Clone();
            _logger.LogDebug("已收集 AppSettings");
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex , "读取 AppSettings 失败");
            return 0;
        }
    }

    /// <summary>
    /// 收集所有会场文件 (*.venue.json)。
    /// </summary>
    private Task<int> AddVenuesChunkAsync (SeatSetsArchive archive , CancellationToken ct)
        => CollectFilesFromDirectoryAsync(archive , SeatSetsConstants.CategoryVenues ,
            ResolveDataDir("Venues") , "*.venue.json" , "Venues" , ct);

    /// <summary>
    /// 收集所有学生名单文件 (*.roster.json)。
    /// </summary>
    private Task<int> AddRostersChunkAsync (SeatSetsArchive archive , CancellationToken ct)
        => CollectFilesFromDirectoryAsync(archive , SeatSetsConstants.CategoryRosters ,
            ResolveDataDir("Rosters") , "*.roster.json" , "Rosters" , ct);

    /// <summary>
    /// 收集所有快照文件 (Assignments/ 下的所有 .json)。
    /// </summary>
    private Task<int> AddSnapshotsChunkAsync (SeatSetsArchive archive , CancellationToken ct)
        => CollectFilesRecursiveAsync(archive , SeatSetsConstants.CategorySnapshots ,
            ResolveDataDir("Assignments") , "Assignments" , ct);

    /// <summary>
    /// 收集所有策略配置文件 (StrategyConfig/ 下的所有 .config.json)。
    /// </summary>
    private async Task<int> AddStrategyConfigChunkAsync (SeatSetsArchive archive , CancellationToken ct)
    {
        var configPath = ResolveDataDir("StrategyConfig");
        // StrategyConfig 目录结构:
        //   {strategyId}.config.json (根级)
        //   {strategyId}/*.config.json (子目录)
        var count = await CollectFilesFromDirectoryAsync(
            archive , SeatSetsConstants.CategoryStrategyConfig ,
            configPath , "*.config.json" , "StrategyConfig" , ct , recursive: false);

        // 收集子目录中的配置
        try
        {
            foreach (var subDir in await ListDirectoriesAsync(configPath , ct))
            {
                ct.ThrowIfCancellationRequested();
                var dirName = LastSegment(subDir);
                var subFiles = await ListFilesAsync(
                    _store is null ? Path.Combine(configPath , dirName) : JoinPath(configPath , dirName) ,
                    "*.config.json" , ct , recursive: false);
                foreach (var filePath in subFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var readPath = _store is null
                            ? Path.Combine(Path.Combine(configPath , dirName) , filePath)
                            : filePath;
                        var json = await ReadDataTextAsync(readPath , ct);
                        if (json is null) continue;
                        using var doc = JsonDocument.Parse(json);
                        var chunk = GetOrCreateChunk(archive , SeatSetsConstants.CategoryStrategyConfig);
                        var relPath = $"StrategyConfig/{dirName}/{LastSegment(filePath)}";
                        chunk.Files[relPath] = doc.RootElement.Clone();
                        count++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex , "读取策略配置文件失败: {Path}" , filePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex , "枚举策略配置子目录时出错");
        }

        return count;
    }

    /// <summary>
    /// 从单个目录中收集匹配的文件。
    /// </summary>
    private async Task<int> CollectFilesFromDirectoryAsync (
        SeatSetsArchive archive ,
        string category ,
        string directoryRel ,
        string searchPattern ,
        string relativePrefix ,
        CancellationToken ct ,
        bool recursive = false)
    {
        var files = await ListFilesAsync(directoryRel , searchPattern , ct , recursive);
        int count = 0;

        foreach (var fileRel in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // 路径模式：fileRel 为相对目录名 → 拼回完整路径读取
                // store 模式：fileRel 即存储根相对路径 → 直接读取
                var readPath = _store is null ? Path.Combine(directoryRel , fileRel) : fileRel;
                var json = await ReadDataTextAsync(readPath , ct);
                if (json is null) continue;
                using var doc = JsonDocument.Parse(json);
                var chunk = GetOrCreateChunk(archive , category);

                chunk.Files[$"{relativePrefix}/{fileRel}"] = doc.RootElement.Clone();
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex , "读取文件失败: {Path}" , fileRel);
            }
        }

        _logger.LogDebug("已收集 {Category}: {Count} 个文件" , category , count);
        return count;
    }

    /// <summary>
    /// 递归收集目录下所有 .json 文件（用于 Assignments 快照目录）。
    /// </summary>
    private async Task<int> CollectFilesRecursiveAsync (
        SeatSetsArchive archive ,
        string category ,
        string directoryRel ,
        string relativePrefix ,
        CancellationToken ct)
    {
        int count = 0;
        try
        {
            var files = await ListFilesAsync(directoryRel , "*.json" , ct , recursive: true);
            foreach (var fileRel in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var readPath = _store is null ? Path.Combine(directoryRel , fileRel) : fileRel;
                    var json = await ReadDataTextAsync(readPath , ct);
                    if (json is null) continue;
                    using var doc = JsonDocument.Parse(json);
                    var chunk = GetOrCreateChunk(archive , category);

                    // 相对路径已经是 "Assignments/..." 形式（dataDir 内相对）
                    chunk.Files[$"{relativePrefix}/{fileRel}"] = doc.RootElement.Clone();
                    count++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex , "读取快照文件失败: {Path}" , fileRel);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex , "枚举快照目录时出错");
        }

        _logger.LogDebug("已收集 {Category}: {Count} 个文件" , category , count);
        return count;
    }

    // ────────────── 双模式 I/O 辅助（路径模式 = 桌面文件系统；store 模式 = IndexedDB）──

    /// <summary>读取文本；路径模式允许根目录相对路径，store 模式直接用相对路径。</summary>
    private async Task<string?> ReadDataTextAsync (string relativeOrPath , CancellationToken ct)
    {
        if (_store is not null)
            return await _store.ReadTextAsync(relativeOrPath , ct);
        return File.Exists(relativeOrPath) ? File.ReadAllText(relativeOrPath) : null;
    }

    private Task<IReadOnlyList<string>> ListFilesAsync (
        string relativeOrPath , string pattern , CancellationToken ct , bool recursive = false)
    {
        if (_store is not null)
        {
            if (recursive)
            {
                // store 模式递归：枚举前缀后按目录段过滤
                return ListRecursiveAsync(relativeOrPath , pattern , ct);
            }
            return _store.ListAsync(relativeOrPath , pattern , ct);
        }
        var dir = relativeOrPath;
        if (!Directory.Exists(dir) && !Path.IsPathRooted(relativeOrPath))
            return Task.FromResult<IReadOnlyList<string>>([]);
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<string>>([]);
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var list = Directory.GetFiles(dir , pattern , option)
            .Select(f => Path.GetRelativePath(dir , f).Replace('\\' , '/'))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(list);
    }

    private async Task<IReadOnlyList<string>> ListRecursiveAsync (string relativeDir , string pattern , CancellationToken ct)
    {
        var results = new List<string>();
        var dirs = new Stack<string>();
        dirs.Push(relativeDir);
        while (dirs.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = dirs.Pop();
            foreach (var file in await _store!.ListAsync(dir , pattern , ct))
            {
                var rest = file[dir.TrimEnd('/').Length..].TrimStart('/');
                if (!rest.Contains('/'))
                    results.Add(file);
            }
            foreach (var sub in await _store.ListDirectoriesAsync(dir , ct))
                dirs.Push(JoinPath(dir , sub));
        }
        return results;
    }

    private Task<IReadOnlyList<string>> ListDirectoriesAsync (string relativeOrPath , CancellationToken ct)
    {
        if (_store is not null)
            return _store.ListDirectoriesAsync(relativeOrPath , ct);
        var dir = relativeOrPath;
        if (!Directory.Exists(dir)) return Task.FromResult<IReadOnlyList<string>>([]);
        return Task.FromResult<IReadOnlyList<string>>(
            Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)).ToList());
    }

    private static string JoinPath (string a , string b)
        => a.TrimEnd('/') + "/" + b.TrimEnd('/');

    private static string LastSegment (string path)
        => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>
    /// 解析数据子目录：路径模式返回绝对路径；store 模式返回相对目录。
    /// </summary>
    private string ResolveDataDir (string relativeName)
        => _store is null ? Path.Combine(_effectiveDataPath! , relativeName) : relativeName;

    /// <summary>
    /// 获取或创建指定类别的 Chunk。
    /// </summary>
    private static SeatSetsChunk GetOrCreateChunk (SeatSetsArchive archive , string category)
    {
        if (!archive.Chunks.TryGetValue(category , out var chunk))
        {
            chunk = new SeatSetsChunk();
            archive.Chunks[category] = chunk;
        }
        return chunk;
    }

    /// <summary>
    /// 计算单个 Chunk 的哈希（基于其 Files 字典的确定性 JSON 序列化）。
    /// </summary>
    private static string ComputeChunkHash (Dictionary<string , JsonElement> files)
    {
        if (files.Count == 0)
            return string.Empty;

        // 按 key 排序以保证确定性
        var sorted = new SortedDictionary<string , JsonElement>(files , StringComparer.Ordinal);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        foreach (var (key , value) in sorted)
        {
            writer.WritePropertyName(key);
            value.WriteTo(writer);
        }
        writer.WriteEndObject();
        writer.Flush();

        var canonical = Encoding.UTF8.GetString(stream.ToArray());
        return ContentHashHelper.ComputeSha256(canonical);
    }

    /// <summary>
    /// 计算归档级别的整体哈希（所有 chunk hash 拼接后取 SHA256）。
    /// </summary>
    private static string ComputeArchiveHash (Dictionary<string , SeatSetsChunk> chunks)
    {
        var hashInput = string.Join("" ,
            chunks.OrderBy(c => c.Key , StringComparer.Ordinal)
                  .Select(c => c.Value.Hash ?? string.Empty));

        return ContentHashHelper.ComputeSha256(hashInput);
    }

    /// <summary>
    /// 根据类别和相对路径解析导入时的目标文件路径。
    /// 内含路径穿越防护：解析后的绝对路径必须位于允许的基目录内。
    /// 仅路径模式（桌面端）使用；store 模式（WASM）不经过此处。
    /// </summary>
    /// <exception cref="InvalidOperationException">路径穿越检测时抛出。</exception>
    private string ResolveTargetPath (string category , string relPath)
    {
        if (_store is not null || _effectiveDataPath is null || _settingsFilePath is null)
            throw new InvalidOperationException("ResolveTargetPath 仅在路径模式下可用");

        // 规范化为平台路径分隔符，拒绝含 null 字节的路径
        var normalized = relPath.Replace('/' , Path.DirectorySeparatorChar);
        if (normalized.Contains('\0'))
            throw new InvalidOperationException($"文件路径包含非法字符: {relPath}");

        string fullPath;
        string allowedBase;

        if (category == SeatSetsConstants.CategoryAppSettings)
        {
            fullPath = Path.GetFullPath(_settingsFilePath);
            allowedBase = Path.GetFullPath(Path.GetDirectoryName(_settingsFilePath)!);
        }
        else
        {
            fullPath = Path.GetFullPath(Path.Combine(_effectiveDataPath , normalized));
            allowedBase = Path.GetFullPath(_effectiveDataPath);
        }

        // 路径穿越检测：解析后的绝对路径必须位于允许的基目录内
        if (!fullPath.StartsWith(allowedBase + Path.DirectorySeparatorChar , StringComparison.OrdinalIgnoreCase)
            && !string.Equals(fullPath , allowedBase , StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"路径穿越检测: '{relPath}' 解析到 '{fullPath}'，不在允许的基目录 '{allowedBase}' 内");
        }

        return fullPath;
    }

    /// <summary>
    /// 获取应用版本字符串。
    /// </summary>
    private static string GetAppVersion ()
    {
        try
        {
            var version = Assembly.GetEntryAssembly()?.GetName()?.Version;
            if (version != null)
                return $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch { /* 忽略 */ }
        return "1.0.0";
    }
}
