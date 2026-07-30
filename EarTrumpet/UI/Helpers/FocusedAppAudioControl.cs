using EarTrumpet.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace EarTrumpet.UI.Helpers;

internal static class FocusedAppAudioControl
{
    internal static IReadOnlyList<IAppItemViewModel> ChangeVolume(IEnumerable<IAppItemViewModel> apps, float delta, float minimum, float maximum)
    {
        var targets = SnapshotTargets(apps);
        var changedApps = new List<IAppItemViewModel>();
        foreach (var (app, streams) in targets)
        {
            var changed = false;
            foreach (var stream in streams)
            {
                var volume = stream.Volume;
                var currentVolume = float.IsFinite(volume) ? volume : minimum;
                stream.Volume = Math.Clamp(currentVolume + delta, minimum, maximum);
                changed |= stream.Volume != volume;
            }

            if (changed)
            {
                changedApps.Add(app);
            }
        }

        return changedApps;
    }

    internal static (IReadOnlyList<IAppItemViewModel> ChangedApps, bool IsMuted) ToggleMute(IEnumerable<IAppItemViewModel> apps)
    {
        var targets = SnapshotTargets(apps);
        var streams = targets.SelectMany(target => target.Streams).ToArray();
        var shouldMute = streams.Any(stream => !stream.IsMuted);
        // Record changed parent groups before asynchronous mute notifications arrive.
        var changedApps = targets.Where(target => target.Streams.Any(stream => stream.IsMuted != shouldMute))
            .Select(target => target.App).ToArray();
        foreach (var stream in streams)
        {
            stream.IsMuted = shouldMute;
        }

        return (changedApps, shouldMute);
    }

    internal static IAppItemViewModel GetRepresentativeApp(IEnumerable<IAppItemViewModel> apps, string defaultDeviceId) => apps
        .OrderBy(app => app.Parent?.Id == defaultDeviceId ? 0 : 1)
        .ThenBy(app => app.Parent?.Id ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(app => app.AppId ?? string.Empty, StringComparer.Ordinal)
        .ThenBy(app => app.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    // Retain the parent for the toast: its absolute slider still controls the whole app group.
    private static (IAppItemViewModel App, IAppItemViewModel[] Streams)[] SnapshotTargets(IEnumerable<IAppItemViewModel> apps) => apps
        .Select(app => (app, EnumerateStreams(new[] { app }).ToArray())).ToArray();

    private static IEnumerable<IAppItemViewModel> EnumerateStreams(IEnumerable<IAppItemViewModel> apps)
    {
        foreach (var app in apps)
        {
            var children = app.ChildApps?.ToArray();
            if (children?.Length > 0)
            {
                // Group getters describe only the first stream, while setters affect every stream.
                // Walk all grouping levels to preserve individual volumes and inspect every mute state.
                foreach (var stream in EnumerateStreams(children))
                {
                    yield return stream;
                }
            }
            else
            {
                // Include leaf placeholders so changes survive an inactive app's device move.
                yield return app;
            }
        }
    }
}
