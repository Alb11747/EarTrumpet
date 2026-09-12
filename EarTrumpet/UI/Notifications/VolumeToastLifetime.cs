using System;

namespace EarTrumpet.UI.Notifications;

internal sealed class VolumeToastLifetime
{
    private bool _hasInteracted;
    private bool _isPointerOver;
    private bool _hasInputCapture;

    internal TimeSpan? HideDelay => _isPointerOver || _hasInputCapture
        ? null
        : TimeSpan.FromSeconds(_hasInteracted ? 5 : 2);

    internal void Reset(bool isPointerOver = false, bool hasInputCapture = false)
    {
        _hasInteracted = hasInputCapture;
        _isPointerOver = isPointerOver;
        _hasInputCapture = hasInputCapture;
    }

    internal void RecordActivity(bool isInteraction) => _hasInteracted |= isInteraction;

    internal void SetPointerOver(bool isPointerOver) => _isPointerOver = isPointerOver;

    internal void SetInputCapture(bool hasInputCapture)
    {
        _hasInputCapture = hasInputCapture;
        _hasInteracted |= hasInputCapture;
    }
}
