using System;
using Microsoft.Win32;

namespace HmDesktopCalendar.DesktopIntegration;

public readonly record struct AutoStartStatus(bool IsAvailable,
    bool IsEnabled, string ErrorMessage)
{
    public static AutoStartStatus Available(bool enabled) =>
        new(true, enabled, string.Empty);
    public static AutoStartStatus Unavailable(string message) =>
        new(false, false, message);
}

public interface IAutoStartRegistrar
{
    AutoStartStatus GetStatus();
    AutoStartStatus SetEnabled(bool enabled);
}

public sealed class AutoStartRegistrar : IAutoStartRegistrar
{
    public const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string DefaultValueName = "HmDesktopCalendar";
    private readonly string _valueName;
    private readonly string? _processPath;

    public AutoStartRegistrar(string valueName = DefaultValueName,
        string? processPath = null)
    {
        _valueName = valueName;
        _processPath = processPath ?? Environment.ProcessPath;
    }

    public AutoStartStatus GetStatus()
    {
        if (!OperatingSystem.IsWindows())
            return AutoStartStatus.Unavailable(
                "Windows에서만 자동 시작을 설정할 수 있습니다.");
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                RunKeyPath, false);
            return AutoStartStatus.Available(
                key?.GetValue(_valueName) is string value &&
                !string.IsNullOrWhiteSpace(value));
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return AutoStartStatus.Unavailable(
                $"자동 시작 설정을 읽지 못했습니다: {exception.Message}");
        }
    }

    public AutoStartStatus SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            return AutoStartStatus.Unavailable(
                "Windows에서만 자동 시작을 설정할 수 있습니다.");
        if (enabled && string.IsNullOrWhiteSpace(_processPath))
            return AutoStartStatus.Unavailable(
                "실행 파일 경로를 확인할 수 없습니다.");
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(
                RunKeyPath, true);
            if (enabled)
                key.SetValue(_valueName, $"\"{_processPath}\"",
                    RegistryValueKind.String);
            else
                key.DeleteValue(_valueName, false);
            return AutoStartStatus.Available(enabled);
        }
        catch (Exception exception) when (IsRegistryFailure(exception))
        {
            return AutoStartStatus.Unavailable(
                $"자동 시작 설정을 변경하지 못했습니다: {exception.Message}");
        }
    }

    private static bool IsRegistryFailure(Exception exception) =>
        exception is UnauthorizedAccessException or System.IO.IOException or
            System.Security.SecurityException or PlatformNotSupportedException;
}
