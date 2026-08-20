using EarTrumpet.DataModel.Storage;
using EarTrumpet.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EarTrumpet.Tests;

internal static class VolumeHotkeySettingsTests
{
    internal static IEnumerable<(string Name, Action Test)> Cases()
    {
        yield return ("Configured shortcut steps retain defaults and normalize persisted values", NormalizesSettings);
        yield return ("Shortcut settings view model persists selected steps", PersistsSelectedSteps);
    }

    private static void NormalizesSettings()
    {
        var settings = CreateSettings(out var bag);
        Check.Equal(2, settings.LinearVolumeHotkeyStep);
        Check.Equal(0.5f, settings.LogarithmicVolumeHotkeyStepDb);
        foreach (var (input, expected) in new[] { (0, 1), (7, 7), (100, 20) })
        {
            bag.Set(nameof(settings.LinearVolumeHotkeyStep), input);
            Check.Equal(expected, settings.LinearVolumeHotkeyStep);
            settings.LinearVolumeHotkeyStep = input;
            Check.Equal(expected, bag.Get(nameof(settings.LinearVolumeHotkeyStep), -1));
        }
        foreach (var (input, expected) in new[]
        {
            (0f, 0.1f), (1.24f, 1.2f), (20f, 10f),
            (float.NaN, 0.5f), (float.PositiveInfinity, 0.5f), (float.NegativeInfinity, 0.5f),
        })
        {
            bag.Set(nameof(settings.LogarithmicVolumeHotkeyStepDb), input);
            Check.Equal(expected, settings.LogarithmicVolumeHotkeyStepDb);
            settings.LogarithmicVolumeHotkeyStepDb = input;
            Check.Equal(expected, bag.Get(nameof(settings.LogarithmicVolumeHotkeyStepDb), -1f));
        }
    }

    private static void PersistsSelectedSteps()
    {
        var settings = CreateSettings(out _);
        var viewModel = new EarTrumpetShortcutsPageViewModel(settings)
        {
            LinearVolumeHotkeyStep = 7,
            LogarithmicVolumeHotkeyStepDb = 1.3,
        };
        Check.Equal(7, settings.LinearVolumeHotkeyStep);
        Check.Equal(1.3f, settings.LogarithmicVolumeHotkeyStepDb);
        var reloaded = new EarTrumpetShortcutsPageViewModel(settings);
        Check.Equal(viewModel.LinearVolumeHotkeyStep, reloaded.LinearVolumeHotkeyStep);
        Check.Equal(viewModel.LogarithmicVolumeHotkeyStepDb, reloaded.LogarithmicVolumeHotkeyStepDb);
    }

    private static AppSettings CreateSettings(out SettingsBag bag)
    {
        var settings = (AppSettings)RuntimeHelpers.GetUninitializedObject(typeof(AppSettings));
        bag = new SettingsBag();
        typeof(AppSettings).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(settings, bag);
        return settings;
    }

    private sealed class SettingsBag : ISettingsBag
    {
        private readonly Dictionary<string, object> _values = new();
        public string Namespace => "";
        public bool HasKey(string key) => _values.ContainsKey(key);
        public T Get<T>(string key, T defaultValue) => _values.TryGetValue(key, out var value) ? (T)value : defaultValue;
        public void Set<T>(string key, T value) => _values[key] = value;
        public event EventHandler<string> SettingChanged { add { } remove { } }
    }
}
