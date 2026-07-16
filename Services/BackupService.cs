using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HmDesktopCalendar.Services;

public sealed record BackupResult(bool Created, DateTimeOffset? LastBackupAt,
    string? BackupPath, string ErrorMessage)
{
    public bool Succeeded => string.IsNullOrEmpty(ErrorMessage);
}

public sealed class BackupService
{
    public const int RetainedGenerationCount = 10;
    private readonly string _dataRoot;
    private readonly string _backupRoot;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<string, string, CancellationToken, Task> _copyFile;

    public BackupService(string? dataRoot = null,
        Func<DateTimeOffset>? now = null,
        Func<string, string, CancellationToken, Task>? copyFile = null)
    {
        _dataRoot = Path.GetFullPath(dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HmDesktopCalendar"));
        _backupRoot = Path.Combine(_dataRoot, "backups");
        _now = now ?? (() => DateTimeOffset.Now);
        _copyFile = copyFile ?? CopySharedAsync;
    }

    public string BackupRoot => _backupRoot;

    public async Task<BackupResult> CreateBackupIfDueAsync(
        CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _now();
        DateTimeOffset? last = GetLastBackupAt();
        try { Directory.CreateDirectory(_backupRoot); }
        catch (Exception exception) when (exception is IOException or
                   UnauthorizedAccessException)
        {
            return new BackupResult(false, last, null, exception.Message);
        }
        if (last?.LocalDateTime.Date == now.LocalDateTime.Date)
            return new BackupResult(false, last, null, string.Empty);

        string name = now.ToString("yyyyMMdd-HHmmss");
        string finalPath = GetUniqueFinalPath(name);
        string temporaryPath = Path.Combine(_backupRoot,
            $".{name}-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(temporaryPath);
            foreach ((string source, string relative) in EnumerateDataFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = Path.Combine(temporaryPath, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await _copyFile(source, destination, cancellationToken);
            }
            Directory.Move(temporaryPath, finalPath);
            PruneOldGenerations();
            return new BackupResult(true, now, finalPath, string.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            TryDeleteDirectory(temporaryPath);
            return new BackupResult(false, last, null, exception.Message);
        }
    }

    public DateTimeOffset? GetLastBackupAt()
    {
        try
        {
            return GetGenerationDirectories()
                .Select(path => ParseTimestamp(Path.GetFileName(path)))
                .Where(value => value.HasValue)
                .DefaultIfEmpty(null).Max();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void EnsureBackupRoot() => Directory.CreateDirectory(_backupRoot);

    private IEnumerable<(string Source, string Relative)> EnumerateDataFiles()
    {
        if (!Directory.Exists(_dataRoot)) yield break;
        foreach (string file in Directory.EnumerateFiles(_dataRoot, "*.json"))
            if (!string.Equals(Path.GetFileName(file), "settings.json",
                    StringComparison.OrdinalIgnoreCase))
                yield return (file, Path.GetFileName(file));
        string accounts = Path.Combine(_dataRoot, "accounts");
        if (!Directory.Exists(accounts)) yield break;
        foreach (string file in Directory.EnumerateFiles(accounts, "*",
                     SearchOption.AllDirectories))
            yield return (file, Path.GetRelativePath(_dataRoot, file));
    }

    private IEnumerable<string> GetGenerationDirectories() =>
        Directory.Exists(_backupRoot)
            ? Directory.EnumerateDirectories(_backupRoot)
                .Where(path => !Path.GetFileName(path).StartsWith('.'))
            : [];

    private void PruneOldGenerations()
    {
        foreach (string path in GetGenerationDirectories()
                     .OrderByDescending(path => Path.GetFileName(path))
                     .Skip(RetainedGenerationCount))
            TryDeleteDirectory(path);
    }

    private string GetUniqueFinalPath(string name)
    {
        string path = Path.Combine(_backupRoot, name);
        for (int suffix = 1; Directory.Exists(path); suffix++)
            path = Path.Combine(_backupRoot, $"{name}-{suffix}");
        return path;
    }

    private static DateTimeOffset? ParseTimestamp(string name) =>
        DateTime.TryParseExact(name[..Math.Min(name.Length, 15)],
            "yyyyMMdd-HHmmss", null,
            System.Globalization.DateTimeStyles.AssumeLocal, out DateTime value)
            ? new DateTimeOffset(value) : null;

    private static async Task CopySharedAsync(string source, string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using var output = new FileStream(destination, FileMode.CreateNew,
            FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
