using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Persistence;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Diagnostics;

public sealed partial class DiagnosticExportService : IDiagnosticExportService
{
    private readonly ISettingsService _settingsService;
    private readonly string _rootDirectory;
    private readonly string _logsDirectory;
    private readonly Func<DateTimeOffset> _clock;

    public DiagnosticExportService(ISettingsService settingsService)
        : this(settingsService, AppDataPaths.RootDirectory, AppDataPaths.LogsDirectory, () => DateTimeOffset.Now)
    {
    }

    internal DiagnosticExportService(
        ISettingsService settingsService,
        string rootDirectory,
        string logsDirectory,
        Func<DateTimeOffset> clock)
    {
        _settingsService = settingsService;
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _logsDirectory = Path.GetFullPath(logsDirectory);
        _clock = clock;
    }

    public async Task<DiagnosticExportResult> ExportAsync(CancellationToken cancellationToken = default)
    {
        var exportedAt = _clock();
        var exportDirectory = Path.Combine(_rootDirectory, "diagnostics");
        Directory.CreateDirectory(exportDirectory);
        var filePath = Path.Combine(exportDirectory, $"igolibrary-diagnostics-{exportedAt:yyyyMMdd-HHmmss}.zip");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        var settings = await _settingsService.LoadAsync(cancellationToken);
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Create);

        await AddTextEntryAsync(
            archive,
            "summary.txt",
            BuildSummary(exportedAt),
            cancellationToken);
        await AddTextEntryAsync(
            archive,
            "settings-redacted.json",
            Redact(JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })),
            cancellationToken);
        await AddLogEntriesAsync(archive, cancellationToken);

        return new DiagnosticExportResult(filePath, exportedAt);
    }

    private async Task AddLogEntriesAsync(ZipArchive archive, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_logsDirectory))
        {
            await AddTextEntryAsync(archive, "logs/empty.txt", "未找到日志目录。", cancellationToken);
            return;
        }

        var logs = Directory
            .EnumerateFiles(_logsDirectory, "*.log")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(5)
            .ToArray();

        if (logs.Length == 0)
        {
            await AddTextEntryAsync(archive, "logs/empty.txt", "日志目录中没有 .log 文件。", cancellationToken);
            return;
        }

        foreach (var logPath in logs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = await File.ReadAllTextAsync(logPath, cancellationToken);
            await AddTextEntryAsync(
                archive,
                $"logs/{Path.GetFileName(logPath)}",
                Redact(content),
                cancellationToken);
        }
    }

    private static async Task AddTextEntryAsync(
        ZipArchive archive,
        string entryName,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private string BuildSummary(DateTimeOffset exportedAt)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
                      ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                      ?? "unknown";
        return $$"""
        IGoLibrary-Ex 诊断包
        导出时间：{{exportedAt:yyyy-MM-dd HH:mm:ss zzz}}
        应用版本：{{version}}
        操作系统：{{System.Runtime.InteropServices.RuntimeInformation.OSDescription}}
        进程架构：{{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}}
        数据目录：{{_rootDirectory}}
        日志目录：{{_logsDirectory}}
        """;
    }

    internal static string Redact(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        var redacted = AuthorizationCookieRegex().Replace(content, "$1[REDACTED]");
        redacted = BearerTokenRegex().Replace(redacted, "$1[REDACTED]");
        redacted = SensitiveAssignmentRegex().Replace(redacted, "$1$2$3[REDACTED]$5");
        redacted = SensitiveJsonRegex().Replace(redacted, "$1[REDACTED]$3");
        return redacted;
    }

    [GeneratedRegex(@"(?i)(Authorization\s*=\s*)[^;\s]+")]
    private static partial Regex AuthorizationCookieRegex();

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]+")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex(@"(?i)\b(password|botToken|deviceKey|authorization|cookie|token)\b(\s*[:=]\s*)([""']?)([^;,\s""']+)([""']?)")]
    private static partial Regex SensitiveAssignmentRegex();

    [GeneratedRegex(@"(?i)(""(?:Password|BotToken|DeviceKey|Cookie|Token|Authorization)""\s*:\s*"")[^""]*("")")]
    private static partial Regex SensitiveJsonRegex();
}
