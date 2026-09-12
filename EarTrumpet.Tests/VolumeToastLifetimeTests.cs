using EarTrumpet.UI.Notifications;
using System;
using System.Collections.Generic;

namespace EarTrumpet.Tests;

internal static class VolumeToastLifetimeTests
{
    internal static IEnumerable<(string Name, Action Test)> Cases()
    {
        yield return ("toast uses normal timeout until an interactive control is used", NormalAndInteractionDelays);
        yield return ("toast hover pauses dismissal without extending a normal timeout", HoverPausesDismissal);
        yield return ("toast capture protects a drag outside its bounds", CapturePausesDismissal);
        yield return ("toast capture release waits for pointer exit", CaptureReleaseWhileHovered);
        yield return ("toast pointer exit waits for capture release", PointerExitWhileCaptured);
        yield return ("repeated toast display retains active capture protection", ResetWhileCaptured);
        yield return ("toast reset clears previous interaction and pause state", ResetClearsInteraction);
    }

    private static void NormalAndInteractionDelays()
    {
        var lifetime = new VolumeToastLifetime();
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(2), lifetime.HideDelay);
        lifetime.RecordActivity(false);
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(2), lifetime.HideDelay);
        lifetime.RecordActivity(true);
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(5), lifetime.HideDelay);
        lifetime.RecordActivity(false);
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(5), lifetime.HideDelay);
    }

    private static void HoverPausesDismissal()
    {
        var lifetime = new VolumeToastLifetime();
        lifetime.SetPointerOver(true);
        Check.Equal<TimeSpan?>(null, lifetime.HideDelay);
        lifetime.SetPointerOver(false);
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(2), lifetime.HideDelay);
    }

    private static void CapturePausesDismissal()
    {
        var lifetime = new VolumeToastLifetime();
        lifetime.SetInputCapture(true);
        lifetime.SetPointerOver(false);
        lifetime.RecordActivity(false);
        Check.Equal<TimeSpan?>(null, lifetime.HideDelay);
        lifetime.SetInputCapture(false);
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(5), lifetime.HideDelay);
    }

    private static void CaptureReleaseWhileHovered()
    {
        var lifetime = new VolumeToastLifetime();
        lifetime.Reset(isPointerOver: true, hasInputCapture: true);
        lifetime.SetInputCapture(false);
        Check.Equal<TimeSpan?>(null, lifetime.HideDelay);
        lifetime.SetPointerOver(false);
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(5), lifetime.HideDelay);
    }

    private static void PointerExitWhileCaptured()
    {
        var lifetime = new VolumeToastLifetime();
        lifetime.Reset(isPointerOver: true, hasInputCapture: true);
        lifetime.SetPointerOver(false);
        Check.Equal<TimeSpan?>(null, lifetime.HideDelay);
        lifetime.SetInputCapture(false);
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(5), lifetime.HideDelay);
    }

    private static void ResetWhileCaptured()
    {
        var lifetime = new VolumeToastLifetime();
        lifetime.SetInputCapture(true);
        lifetime.Reset(isPointerOver: false, hasInputCapture: true);
        Check.Equal<TimeSpan?>(null, lifetime.HideDelay);
        lifetime.SetInputCapture(false);
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(5), lifetime.HideDelay);
    }

    private static void ResetClearsInteraction()
    {
        var lifetime = new VolumeToastLifetime();
        lifetime.Reset(isPointerOver: true, hasInputCapture: true);
        lifetime.Reset();
        Check.Equal<TimeSpan?>(TimeSpan.FromSeconds(2), lifetime.HideDelay);
    }
}
