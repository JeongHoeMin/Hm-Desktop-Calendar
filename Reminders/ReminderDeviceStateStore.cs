using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HmDesktopCalendar.Reminders;

public sealed record ReminderDeviceState(DateTimeOffset DisplayedAt,
    DateTimeOffset? AcknowledgedAt = null, DateTimeOffset? SnoozedUntil = null);

public interface IReminderDeviceStateStore
{
    Task<IReadOnlyDictionary<string, ReminderDeviceState>> ReadAsync(
        CancellationToken cancellationToken = default);
    Task WriteAsync(string key, ReminderDeviceState state,
        CancellationToken cancellationToken = default);
}

public sealed class JsonReminderDeviceStateStore : IReminderDeviceStateStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public JsonReminderDeviceStateStore(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "HmDesktopCalendar");
        _path = Path.Combine(directory, "reminders-device.json");
    }

    public async Task<IReadOnlyDictionary<string, ReminderDeviceState>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, ReminderDeviceState>();
            await using FileStream stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<Dictionary<string,
                ReminderDeviceState>>(stream, _options, cancellationToken) ?? [];
        }
        finally { _gate.Release(); }
    }

    public async Task WriteAsync(string key, ReminderDeviceState state,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Dictionary<string, ReminderDeviceState> states = [];
            if (File.Exists(_path))
            {
                await using FileStream input = File.OpenRead(_path);
                states = await JsonSerializer.DeserializeAsync<Dictionary<string,
                    ReminderDeviceState>>(input, _options, cancellationToken) ?? [];
            }
            states[key] = state;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temporary = _path + ".tmp";
            await using (FileStream output = File.Create(temporary))
                await JsonSerializer.SerializeAsync(output, states, _options,
                    cancellationToken);
            File.Move(temporary, _path, true);
        }
        finally { _gate.Release(); }
    }
}
