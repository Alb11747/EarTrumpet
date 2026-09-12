using EarTrumpet.DataModel.Storage;
using EarTrumpet.UI.Controls;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;

namespace EarTrumpet.Tests;

internal static class VolumeSliderLifecycleTests
{
    internal static IEnumerable<(string Name, Action Test)> Cases()
    {
        yield return ("Reloaded sliders follow volume mode and minimum changes", ReloadedSlider);
        yield return ("Repeated loads do not leave a settings subscription after unload", RepeatedLoads);
        yield return ("Custom slider ranges survive settings changes and reload", CustomRange);
    }

    private static void ReloadedSlider()
    {
        using var fixture = new SliderFixture();
        AssertRange(fixture.Slider, 0, 100, 1);
        fixture.Load();
        fixture.Settings.UseLogarithmicVolume = true;
        AssertRange(fixture.Slider, -40, 0, 0.1);

        fixture.Unload();
        fixture.Settings.LogarithmicVolumeMinDb = -60;
        AssertRange(fixture.Slider, -40, 0, 0.1);
        fixture.Load();
        AssertRange(fixture.Slider, -60, 0, 0.1);
        fixture.Settings.LogarithmicVolumeMinDb = -80;
        AssertRange(fixture.Slider, -80, 0, 0.1);
        fixture.Settings.UseLogarithmicVolume = false;
        AssertRange(fixture.Slider, 0, 100, 1);
        fixture.Settings.UseLogarithmicVolume = true;
        AssertRange(fixture.Slider, -80, 0, 0.1);
    }

    private static void RepeatedLoads()
    {
        using var fixture = new SliderFixture();
        fixture.Settings.UseLogarithmicVolume = true;
        AssertRange(fixture.Slider, 0, 100, 1);
        fixture.Load();
        fixture.Load();
        AssertRange(fixture.Slider, -40, 0, 0.1);
        fixture.Unload();
        fixture.Unload();
        fixture.Settings.UseLogarithmicVolume = false;
        AssertRange(fixture.Slider, -40, 0, 0.1);
        fixture.Load();
        AssertRange(fixture.Slider, 0, 100, 1);
        fixture.Settings.UseLogarithmicVolume = true;
        AssertRange(fixture.Slider, -40, 0, 0.1);
    }

    private static void CustomRange()
    {
        using var fixture = new SliderFixture();
        fixture.Slider.UseCustomRange = true;
        fixture.Slider.Minimum = 1;
        fixture.Slider.Maximum = 20;
        fixture.Slider.TickFrequency = 0.5;
        fixture.Load();
        fixture.Settings.UseLogarithmicVolume = true;
        fixture.Settings.LogarithmicVolumeMinDb = -60;
        AssertRange(fixture.Slider, 1, 20, 0.5);
        fixture.Unload();
        fixture.Settings.UseLogarithmicVolume = false;
        fixture.Load();
        AssertRange(fixture.Slider, 1, 20, 0.5);
    }

    private static void AssertRange(VolumeSlider slider, double minimum, double maximum, double tickFrequency)
    {
        Check.Equal(minimum, slider.Minimum);
        Check.Equal(maximum, slider.Maximum);
        Check.Equal(tickFrequency, slider.TickFrequency);
    }

    private sealed class SliderFixture : IDisposable
    {
        private readonly AppSettings _previousSettings = App.Settings;
        internal AppSettings Settings { get; }
        internal VolumeSlider Slider { get; }

        internal SliderFixture()
        {
            // Avoid initializing persistent storage; these lifecycle tests need no
            // application window, audio endpoint, or user settings.
            Settings = (AppSettings)RuntimeHelpers.GetUninitializedObject(typeof(AppSettings));
            typeof(AppSettings).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(Settings, new MemorySettings());
            typeof(App).GetProperty(nameof(App.Settings)).SetValue(null, Settings);
            Slider = new VolumeSlider();
        }

        internal void Load() => Slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        internal void Unload() => Slider.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

        public void Dispose()
        {
            Unload();
            typeof(App).GetProperty(nameof(App.Settings)).SetValue(null, _previousSettings);
        }
    }

    private sealed class MemorySettings : ISettingsBag
    {
        private readonly Dictionary<string, object> _values = new();
        public string Namespace => "";
        public bool HasKey(string key) => _values.ContainsKey(key);
        public T Get<T>(string key, T defaultValue) => _values.TryGetValue(key, out var value) ? (T)value : defaultValue;
        public void Set<T>(string key, T value) => _values[key] = value;
        public event EventHandler<string> SettingChanged { add { } remove { } }
    }
}
