using EarTrumpet.UI.Helpers;
using EarTrumpet.UI.ViewModels;
using System;
using System.ComponentModel;

namespace EarTrumpet.UI.Notifications;

internal sealed class VolumeToastViewModel : BindableBase, IDisposable
{
    private readonly DeviceViewModel _device;
    private readonly IAppItemViewModel _app;
    private readonly INotifyPropertyChanged _source;
    private float _volume;
    private bool _isMuted;
    private bool _isDisposed;

    private VolumeToastViewModel(DeviceViewModel device, bool? isMuted)
    {
        _device = device;
        _source = device;
        _volume = NormalizeVolume(device.Volume);
        _isMuted = isMuted ?? device.IsMuted;
        _source.PropertyChanged += Source_PropertyChanged;
    }

    private VolumeToastViewModel(IAppItemViewModel app, bool? isMuted)
    {
        _app = app;
        _source = app;
        _volume = NormalizeVolume(app.Volume);
        _isMuted = isMuted ?? app.IsMuted;
        _source.PropertyChanged += Source_PropertyChanged;
    }

    public string Title => IsDevice ? _device.DisplayName : _app.DisplayName;

    public float Volume
    {
        get => _volume;
        set
        {
            var normalizedValue = NormalizeVolume(value);
            UpdateVolume(normalizedValue);

            if (IsDevice)
            {
                _device.Volume = normalizedValue;
            }
            else
            {
                _app.Volume = normalizedValue;
            }
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            UpdateIsMuted(value);

            if (IsDevice)
            {
                _device.IsMuted = value;
            }
            else
            {
                _app.IsMuted = value;
            }
        }
    }

    public bool IsDevice => _device != null;
    public bool IsApp => !IsDevice;
    public DeviceViewModel.DeviceIconKind DeviceIconKind => _device?.IconKind ?? DeviceViewModel.DeviceIconKind.Bar0;
    public IAppIconSource AppIcon => _app;
    public char AppIconText => _app?.IconText ?? default;

    public static VolumeToastViewModel FromDevice(DeviceViewModel device, bool? isMuted = null) => new(device, isMuted);
    public static VolumeToastViewModel FromApp(IAppItemViewModel app, bool? isMuted = null) => new(app, isMuted);

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _source.PropertyChanged -= Source_PropertyChanged;
            _isDisposed = true;
        }
    }

    private void Source_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Volume):
                UpdateVolume(IsDevice ? _device.Volume : _app.Volume);
                break;
            case nameof(IsMuted):
                UpdateIsMuted(IsDevice ? _device.IsMuted : _app.IsMuted);
                break;
            case nameof(DeviceViewModel.IconKind):
                RaisePropertyChanged(nameof(DeviceIconKind));
                break;
            case nameof(DeviceViewModel.DisplayName):
                RaisePropertyChanged(nameof(Title));
                RaisePropertyChanged(nameof(AppIconText));
                break;
        }
    }

    private void UpdateVolume(float value)
    {
        value = NormalizeVolume(value);

        if (_volume != value)
        {
            _volume = value;
            RaisePropertyChanged(nameof(Volume));
        }
    }

    private static float NormalizeVolume(float value)
    {
        var minimum = App.Settings.UseLogarithmicVolume ? App.Settings.LogarithmicVolumeMinDb : 0f;
        var maximum = App.Settings.UseLogarithmicVolume ? 0f : 100f;
        return float.IsFinite(value)
            ? Math.Max(minimum, Math.Min(maximum, value))
            : minimum;
    }

    private void UpdateIsMuted(bool value)
    {
        if (_isMuted != value)
        {
            _isMuted = value;
            RaisePropertyChanged(nameof(IsMuted));
        }
    }
}
