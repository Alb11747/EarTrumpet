using EarTrumpet.UI.Notifications;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using TestApp = EarTrumpet.Tests.FocusedAppAudioControlTests.TestApp;

namespace EarTrumpet.Tests;

internal static class VolumeToastViewModelTests
{
    internal static IEnumerable<(string Name, Action Test)> Cases()
    {
        yield return ("Repeated toasts for the same target refresh the displayed view model", RefreshesSameTarget);
        yield return ("Toasts for another target or after disposal replace the view model", RejectsOtherTargets);
    }

    private static void RefreshesSameTarget()
    {
        using var settings = new SettingsScope();
        var app = new TestApp(40);
        using var shown = VolumeToastViewModel.FromApp(app);

        // The test double raises no change notifications, so only a refresh can update the toast.
        app.Volume = 55;
        app.IsMuted = true;
        using var next = VolumeToastViewModel.FromApp(app, isMuted: false);
        Check.True(shown.TryRefreshFrom(next));
        Check.Equal(55f, shown.Volume);
        Check.True(!shown.IsMuted);
    }

    private static void RejectsOtherTargets()
    {
        using var settings = new SettingsScope();
        var app = new TestApp(10);
        var shown = VolumeToastViewModel.FromApp(app);
        using var other = VolumeToastViewModel.FromApp(new TestApp(20));
        Check.True(!shown.TryRefreshFrom(other));
        Check.Equal(10f, shown.Volume);

        shown.Dispose();
        using var same = VolumeToastViewModel.FromApp(app);
        Check.True(!shown.TryRefreshFrom(same));
    }

    private sealed class SettingsScope : IDisposable
    {
        private readonly AppSettings _previousSettings = App.Settings;

        internal SettingsScope()
        {
            // Toast volume normalization reads the volume mode; avoid persistent storage.
            var settings = (AppSettings)RuntimeHelpers.GetUninitializedObject(typeof(AppSettings));
            typeof(AppSettings).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(settings, new VolumeSliderLifecycleTests.MemorySettings());
            typeof(App).GetProperty(nameof(App.Settings)).SetValue(null, settings);
        }

        public void Dispose() => typeof(App).GetProperty(nameof(App.Settings)).SetValue(null, _previousSettings);
    }
}
