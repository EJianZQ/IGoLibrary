using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Platform;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private static TimeOnly ToTimeOnly(TimeSpan value, string fieldName)
    {
        if (!IsTimeOfDay(value))
        {
            throw new InvalidOperationException($"{fieldName}必须介于 00:00:00 和 23:59:59 之间");
        }

        return TimeOnly.FromTimeSpan(value);
    }

    private static TimeSpan NormalizeTimeOfDay(TimeSpan value, TimeSpan fallback)
    {
        return IsTimeOfDay(value) ? value : fallback;
    }

    private static bool IsTimeOfDay(TimeSpan value)
    {
        return value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
    }

    private static string FormatElapsedClock(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return $"{Math.Max(0, (int)elapsed.TotalHours):D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private static bool IsTaskActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }

    private static string GetSystemUserDisplayName()
    {
        return SystemUserDisplayNameResolver.GetCurrentDisplayName();
    }

    private static string FormatReservationRemaining(TimeSpan remaining)
    {
        if (remaining.TotalHours >= 1)
        {
            return remaining.ToString(@"hh\:mm\:ss", CultureInfo.GetCultureInfo("zh-CN"));
        }

        return remaining.ToString(@"mm\:ss", CultureInfo.GetCultureInfo("zh-CN"));
    }

    private static string TrimSentenceEnding(string message)
    {
        return string.IsNullOrEmpty(message)
            ? message
            : message.TrimEnd('。', '.');
    }

    private static string BuildExceptionDetails(Exception exception)
    {
        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null)
        {
            if (depth == 0)
            {
                builder.Append(current.Message);
            }
            else
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.Append($"内部异常 {depth}：{current.GetType().Name}: {current.Message}");
            }

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }
}
