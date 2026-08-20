using EarTrumpet.DataModel.Audio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FormsCursor = System.Windows.Forms.Cursor;
using FormsScreen = System.Windows.Forms.Screen;
using EarTrumpet.DataModel.WindowsAudio;
using EarTrumpet.Diagnosis;
using EarTrumpet.Extensibility;
using EarTrumpet.Extensibility.Hosting;
using EarTrumpet.Extensions;
using EarTrumpet.Interop.Helpers;
using EarTrumpet.UI.Helpers;
using EarTrumpet.UI.Notifications;
using EarTrumpet.UI.ViewModels;
using EarTrumpet.UI.Views;
using Microsoft.Win32;
using Windows.Win32;

namespace EarTrumpet;

public sealed partial class App : IDisposable
{
    public static bool IsShuttingDown
    {
        get; private set;
    }
    public static bool HasIdentity
    {
        get; private set;
    }
    public static bool HasDevIdentity
    {
        get; private set;
    }
    public static string PackageName
    {
        get; private set;
    }
    public static Version PackageVersion
    {
        get; private set;
    }
    public static TimeSpan Duration => s_appTimer.Elapsed;

    public FlyoutWindow FlyoutWindow
    {
        get; private set;
    }
    public DeviceCollectionViewModel CollectionViewModel
    {
        get; private set;
    }

    private static readonly Stopwatch s_appTimer = Stopwatch.StartNew();
    private static readonly GridLength s_linearVolumeCellWidth = new(63);
    private static readonly GridLength s_logarithmicVolumeCellWidth = new(104);
    private static readonly GridLength s_linearToastVolumeCellWidth = new(54);
    private static readonly GridLength s_logarithmicToastVolumeCellWidth = new(90);
    private FlyoutViewModel _flyoutViewModel;

    private ShellNotifyIcon _trayIcon;
    private WindowHolder _mixerWindow;
    private WindowHolder _settingsWindow;
    private ErrorReporter _errorReporter;

    public static AppSettings Settings
    {
        get; private set;
    }

    private void OnAppStartup(object sender, StartupEventArgs e)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        Exit += (_, __) =>
        {
            IsShuttingDown = true;
            VolumeToastService.Shutdown();
        };
        HasIdentity = PackageHelper.CheckHasIdentity();
        HasDevIdentity = PackageHelper.HasDevIdentity();
        PackageVersion = PackageHelper.GetVersion(HasIdentity);
        PackageName = PackageHelper.GetFamilyName(HasIdentity);

        Settings = new AppSettings();
        UpdateVolumeDisplayMetrics();
        Settings.UseLogarithmicVolumeChanged += (_, __) => UpdateVolumeDisplayMetrics();
        _errorReporter = new ErrorReporter(Settings);

        if (SingleInstanceAppMutex.TakeExclusivity())
        {
            Exit += (_, __) => SingleInstanceAppMutex.ReleaseExclusivity();

            try
            {
                NotifyOnMissingStartupPolicies();
                ContinueStartup();
            }
            catch (Exception ex) when (IsCriticalFontLoadFailure(ex))
            {
                ErrorReporter.LogWarning(ex);
                OnCriticalFontLoadFailure();
            }
        }
        else
        {
            Shutdown();
        }
    }

    private void ContinueStartup()
    {
        ((UI.Themes.Manager)Resources["ThemeManager"]).Load();

        var deviceManager = WindowsAudioFactory.Create(AudioDeviceKind.Playback);
        deviceManager.Loaded += (_, __) => CompleteStartup();
        CollectionViewModel = new DeviceCollectionViewModel(deviceManager, Settings);

        _trayIcon = new ShellNotifyIcon(new TaskbarIconSource(CollectionViewModel, Settings), Settings.TrayIconIdentity);
        Exit += (_, __) => _trayIcon.IsVisible = false;
        CollectionViewModel.TrayPropertyChanged += () => UpdateTrayTooltip();

        _flyoutViewModel = new FlyoutViewModel(CollectionViewModel, () => _trayIcon.SetFocus(), Settings);
        FlyoutWindow = new FlyoutWindow(_flyoutViewModel);
        // Initialize the FlyoutWindow last because its Show/Hide cycle will pump messages, causing UI frames
        // to be executed, breaking the assumption that startup is complete.
        FlyoutWindow.Initialize();

        // listen for user session change
        // When user come back after user switch, do some workaround for issue of losing audio sessions
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        Exit += (_, __) => SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        Trace.WriteLine($"Detected User Session Switch: {e.Reason}");
        if (e.Reason == SessionSwitchReason.ConsoleConnect)
        {
            var devManager = WindowsAudioFactory.Create(AudioDeviceKind.Playback);
            devManager.RefreshAllDevices();
        }
    }

    private void CompleteStartup()
    {
        AddonManager.Load(shouldLoadInternalAddons: HasDevIdentity);
        Exit += (_, __) => AddonManager.Shutdown();
#if DEBUG
        DebugHelpers.Add();
#endif
        _mixerWindow = new WindowHolder(CreateMixerExperience);
        _settingsWindow = new WindowHolder(CreateSettingsExperience);

        Settings.FlyoutHotkeyTyped += () => _flyoutViewModel.OpenFlyout(InputType.Keyboard);
        Settings.MixerHotkeyTyped += () => _mixerWindow.OpenOrClose();
        Settings.SettingsHotkeyTyped += () => _settingsWindow.OpenOrBringToFront();
        Settings.AbsoluteVolumeUpHotkeyTyped += AbsoluteVolumeIncrement;
        Settings.AbsoluteVolumeDownHotkeyTyped += AbsoluteVolumeDecrement;
        Settings.FocusedAppVolumeUpHotkeyTyped += FocusedAppVolumeIncrement;
        Settings.FocusedAppVolumeDownHotkeyTyped += FocusedAppVolumeDecrement;
        Settings.FocusedAppToggleMuteHotkeyTyped += FocusedAppToggleMute;
        Settings.RegisterHotkeys();
        Settings.UseLogarithmicVolumeChanged += (_, __) => UpdateTrayTooltip();

        _trayIcon.PrimaryInvoke += (_, type) => _flyoutViewModel.OpenFlyout(type);
        _trayIcon.SecondaryInvoke += (_, args) => _trayIcon.ShowContextMenu(GetTrayContextMenuItems(), args.Point);
        _trayIcon.TertiaryInvoke += (_, __) => ToggleDefaultDeviceMuteFromTray();
        _trayIcon.Scrolled += TrayIconScrolled;
        _trayIcon.SetTooltip(CollectionViewModel.GetTrayToolTip());
        _trayIcon.IsVisible = true;

        DisplayFirstRunExperience();
        ShowFullMixerWindowIfConfigured();
    }

    private void ShowFullMixerWindowIfConfigured()
    {
        if (Settings.ShowFullMixerWindowOnStartup)
        {
            _mixerWindow.OpenOrBringToFront();
        }
    }

    private void UpdateTrayTooltip()
    {
        _trayIcon.SetTooltip(CollectionViewModel.GetTrayToolTip());

        var hWndTray = WindowsTaskbar.GetTrayToolbarWindowHwnd();
        unsafe
        {
            var hWndTooltip = PInvoke.SendMessage(new HWND(hWndTray.ToPointer()), PInvoke.TB_GETTOOLTIPS, default, default);
            PInvoke.SendMessage(new HWND(hWndTooltip.Value.ToPointer()), PInvoke.TTM_POPUP, default, default);
        }
    }

    private void TrayIconScrolled(object _, int wheelDelta)
    {
        if (Settings.UseScrollWheelInTray && (!Settings.UseGlobalMouseWheelHook || _flyoutViewModel.State == FlyoutViewState.Hidden))
        {
            var device = CollectionViewModel.Default;
            if (device != null)
            {
                var oldVolume = device.Volume;
                device.IncrementVolume(Math.Sign(wheelDelta) * (Settings.UseLogarithmicVolume ? 0.2f : 2.0f));
                if (device.Volume != oldVolume)
                {
                    VolumeToastService.ShowDevice(device, GetPointerScreen());
                }
            }
        }
    }

    private void UpdateVolumeDisplayMetrics()
    {
        Resources["Mutable_VolumeCellWidth"] = Settings.UseLogarithmicVolume
            ? s_logarithmicVolumeCellWidth
            : s_linearVolumeCellWidth;
        Resources["Mutable_ToastVolumeCellWidth"] = Settings.UseLogarithmicVolume
            ? s_logarithmicToastVolumeCellWidth
            : s_linearToastVolumeCellWidth;
    }

    private void ToggleDefaultDeviceMuteFromTray()
    {
        var device = CollectionViewModel.Default;
        if (device == null)
        {
            return;
        }

        var isMuted = !device.IsMuted;
        device.IsMuted = isMuted;
        VolumeToastService.ShowDevice(device, GetPointerScreen(), isMuted);
    }

    private static void DisplayFirstRunExperience()
    {
        if (!Settings.HasShownFirstRun
#if DEBUG
            || Keyboard.IsKeyDown(Key.LeftCtrl)
#endif
            )
        {
            Trace.WriteLine($"App DisplayFirstRunExperience Showing welcome dialog");
            Settings.HasShownFirstRun = true;

            var dialog = new DialogWindow { DataContext = new WelcomeViewModel(Settings) };
            dialog.Show();
            dialog.RaiseWindow();
        }
    }

    private static bool IsCriticalFontLoadFailure(Exception ex)
    {
        return ex.StackTrace.Contains("MS.Internal.Text.TextInterface.FontFamily.GetFirstMatchingFont") ||
               ex.StackTrace.Contains("MS.Internal.Text.Line.Format");
    }

    private static void OnCriticalFontLoadFailure()
    {
        Trace.WriteLine($"App OnCriticalFontLoadFailure");

        new Thread(() =>
        {
            if (MessageBox.Show(
                EarTrumpet.Properties.Resources.CriticalFailureFontLookupHelpText,
                EarTrumpet.Properties.Resources.CriticalFailureDialogHeaderText,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Error,
                MessageBoxResult.OK) == MessageBoxResult.OK)
            {
                Trace.WriteLine($"App OnCriticalFontLoadFailure OK");
                ProcessHelper.StartNoThrow("https://eartrumpet.app/jmp/fixfonts");
            }
            Environment.Exit(0);
        }).Start();

        // Stop execution because callbacks to the UI thread will likely cause another cascading font error.
        new AutoResetEvent(false).WaitOne();
    }

    private static bool IsAnyStartupPolicyMissing()
    {
        Trace.WriteLine($"App IsAnyStartupPolicyMissing");

        try
        {
            var registryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
            using var key = Registry.LocalMachine.OpenSubKey(registryPath);
            if (key == null)
            {
                return true;
            }

            var dwords = new[] {
                "EnableFullTrustStartupTasks",
                "EnableUwpStartupTasks",
                "SupportFullTrustStartupTasks",
                "SupportUwpStartupTasks"
            };

            foreach (var dword in dwords)
            {
                // Warning: RegistryKey.GetValue returns int for DWORDs

                var value = key.GetValue(dword);
                if (value == null || value.GetType() != typeof(int))
                {
                    Trace.WriteLine($"Missing or invalid: {dword}");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Exception: {ex}");
        }

        return false;
    }

    private static void NotifyOnMissingStartupPolicies()
    {
        if (!IsAnyStartupPolicyMissing())
        {
            return;
        }

        new Thread(() =>
        {
            if (MessageBox.Show(
                EarTrumpet.Properties.Resources.MissingPoliciesHelpText,
                EarTrumpet.Properties.Resources.MissingPoliciesDialogHeaderText,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.OK) == MessageBoxResult.OK)
            {
                Trace.WriteLine($"App NotifyOnMissingStartupPolicies OK");
                ProcessHelper.StartNoThrow("https://eartrumpet.app/jmp/fixstartup");
            }
        }).Start();
    }

    private List<ContextMenuItem> GetTrayContextMenuItems()
    {
        var ret = new List<ContextMenuItem>(CollectionViewModel.AllDevices.OrderBy(x => x.DisplayName).Select(dev => new ContextMenuItem
        {
            DisplayName = dev.DisplayName,
            IsChecked = dev.Id == CollectionViewModel.Default?.Id,
            Command = new RelayCommand(() => dev.MakeDefaultDevice()),
        }));

        if (ret.Count == 0)
        {
            ret.Add(new ContextMenuItem
            {
                DisplayName = EarTrumpet.Properties.Resources.ContextMenuNoDevices,
                IsEnabled = false,
            });
        }

        ret.AddRange(
            [
                new ContextMenuSeparator(),
                new ContextMenuItem
                {
                    DisplayName = EarTrumpet.Properties.Resources.WindowsLegacyMenuText,
                    Children =
                    [
                        new() { DisplayName = EarTrumpet.Properties.Resources.LegacyVolumeMixerText, Command =  new RelayCommand(LegacyControlPanelHelper.StartLegacyAudioMixer) },
                        new() { DisplayName = EarTrumpet.Properties.Resources.PlaybackDevicesText, Command = new RelayCommand(() => LegacyControlPanelHelper.Open("playback")) },
                        new() { DisplayName = EarTrumpet.Properties.Resources.RecordingDevicesText, Command = new RelayCommand(() => LegacyControlPanelHelper.Open("recording")) },
                        new() { DisplayName = EarTrumpet.Properties.Resources.SoundsControlPanelText, Command = new RelayCommand(() => LegacyControlPanelHelper.Open("sounds")) },
                        new() { DisplayName = EarTrumpet.Properties.Resources.OpenSoundSettingsText, Command = new RelayCommand(() => SettingsPageHelper.Open("sound")) },
                        new() {
                            DisplayName = Environment.OSVersion.IsAtLeast(OSVersions.Windows11) ?
                                EarTrumpet.Properties.Resources.OpenAppsVolume_Windows11_Text
                                : EarTrumpet.Properties.Resources.OpenAppsVolume_Windows10_Text, Command = new RelayCommand(() => SettingsPageHelper.Open("apps-volume")) },
                    ],
                },
                new ContextMenuSeparator(),
            ]);

        var addonItems = AddonManager.Host.TrayContextMenuItems?.OrderBy(x => x.NotificationAreaContextMenuItems.FirstOrDefault()?.DisplayName).SelectMany(ext => ext.NotificationAreaContextMenuItems);
        if (addonItems != null && addonItems.Any())
        {
            ret.AddRange(addonItems);
            ret.Add(new ContextMenuSeparator());
        }

        ret.AddRange(
            [
                new() { DisplayName = EarTrumpet.Properties.Resources.FullWindowTitleText, Command = new RelayCommand(_mixerWindow.OpenOrBringToFront) },
                new() { DisplayName = EarTrumpet.Properties.Resources.SettingsWindowText, Command = new RelayCommand(_settingsWindow.OpenOrBringToFront) },
                new() { DisplayName = EarTrumpet.Properties.Resources.ContextMenuExitTitle, Command = new RelayCommand(Shutdown) },
            ]);
        return ret;
    }

    private Window CreateSettingsExperience()
    {
        var defaultCategory = new SettingsCategoryViewModel(
            EarTrumpet.Properties.Resources.SettingsCategoryTitle,
            "\xE71D",
            EarTrumpet.Properties.Resources.SettingsDescriptionText,
            null,
            [
                new EarTrumpetShortcutsPageViewModel(Settings),
                new EarTrumpetMouseSettingsPageViewModel(Settings),
                new EarTrumpetCommunitySettingsPageViewModel(Settings),
                new EarTrumpetLegacySettingsPageViewModel(Settings),
                new EarTrumpetAboutPageViewModel(_errorReporter.DisplayDiagnosticData, Settings)
            ]);

        var allCategories = new List<SettingsCategoryViewModel>
        {
            defaultCategory
        };

        if (AddonManager.Host.SettingsItems != null)
        {
            allCategories.AddRange(AddonManager.Host.SettingsItems.Select(CreateAddonSettingsPage));
        }

        var viewModel = new SettingsViewModel(EarTrumpet.Properties.Resources.SettingsWindowText, allCategories);
        return new SettingsWindow { DataContext = viewModel };
    }

    private static SettingsCategoryViewModel CreateAddonSettingsPage(IEarTrumpetAddonSettingsPage addonSettingsPage)
    {
        var addon = (EarTrumpetAddon)addonSettingsPage;
        var category = addonSettingsPage.GetSettingsCategory();

        if (!addon.IsInternal())
        {
            category.Pages.Add(new AddonAboutPageViewModel(addon));
        }
        return category;
    }

    private Window CreateMixerExperience() => new FullWindow { DataContext = new FullWindowViewModel(CollectionViewModel) };

    public void Dispose()
    {
        _errorReporter.Dispose();
        _trayIcon.Dispose();
    }

    private void AbsoluteVolumeIncrement()
    {
        var step = GetVolumeHotkeyStep();
        var changedDevices = new List<DeviceViewModel>();
        foreach (var device in CollectionViewModel.AllDevices.Where(d => !d.IsMuted || d.IsAbsMuted).ToArray())
        {
            var oldVolume = device.Volume;
            device.IsAbsMuted = false;
            device.IncrementVolume(step);
            if (device.Volume != oldVolume)
            {
                changedDevices.Add(device);
            }
        }

        var representative = GetRepresentativeDevice(changedDevices);
        if (representative != null)
        {
            VolumeToastService.ShowDevice(representative, GetForegroundScreen());
        }
    }

    private void AbsoluteVolumeDecrement()
    {
        var step = GetVolumeHotkeyStep();
        var changedDevices = new List<DeviceViewModel>();
        var minimum = Settings.UseLogarithmicVolume ? Settings.LogarithmicVolumeMinDb : 0f;
        foreach (var device in CollectionViewModel.AllDevices.Where(d => !d.IsMuted).ToArray())
        {
            var wasMuted = device.IsMuted;
            var oldVolume = device.Volume;
            device.Volume -= step;

            if (!wasMuted == (device.Volume <= minimum))
            {
                device.IsAbsMuted = true;
            }

            if (device.Volume != oldVolume)
            {
                changedDevices.Add(device);
            }
        }

        var representative = GetRepresentativeDevice(changedDevices);
        if (representative != null)
        {
            VolumeToastService.ShowDevice(representative, GetForegroundScreen());
        }
    }

    private void FocusedAppVolumeIncrement()
    {
        ChangeFocusedAppVolume(GetVolumeHotkeyStep());
    }

    private void FocusedAppVolumeDecrement()
    {
        ChangeFocusedAppVolume(-GetVolumeHotkeyStep());
    }

    private static float GetVolumeHotkeyStep() => Settings.UseLogarithmicVolume
        ? Settings.LogarithmicVolumeHotkeyStepDb
        : Settings.LinearVolumeHotkeyStep;

    private void ChangeFocusedAppVolume(float delta)
    {
        var changedApps = new List<IAppItemViewModel>();
        var minimum = Settings.UseLogarithmicVolume ? Settings.LogarithmicVolumeMinDb : 0f;
        var maximum = Settings.UseLogarithmicVolume ? 0f : 100f;

        foreach (var app in GetFocusedApps())
        {
            var oldVolume = app.Volume;
            var currentVolume = float.IsFinite(oldVolume) ? oldVolume : minimum;
            app.Volume = Math.Clamp(currentVolume + delta, minimum, maximum);
            if (app.Volume != oldVolume)
            {
                changedApps.Add(app);
            }
        }

        var representative = GetRepresentativeApp(changedApps);
        if (representative != null)
        {
            VolumeToastService.ShowApp(representative, GetForegroundScreen());
        }
    }

    private void FocusedAppToggleMute()
    {
        var apps = GetFocusedApps().ToArray();
        if (!apps.Any())
        {
            return;
        }

        var shouldMute = apps.Any(app => !app.IsMuted);
        var changedApps = apps.Where(app => app.IsMuted != shouldMute).ToArray();
        foreach (var app in apps)
        {
            app.IsMuted = shouldMute;
        }

        var representative = GetRepresentativeApp(changedApps);
        if (representative != null)
        {
            VolumeToastService.ShowApp(representative, GetForegroundScreen(), shouldMute);
        }
    }

    private DeviceViewModel GetRepresentativeDevice(IReadOnlyCollection<DeviceViewModel> devices)
    {
        // Absolute hotkeys can affect every device. Prefer the default device, then use a
        // stable identifier so collection ordering never determines the visible toast.
        return devices.FirstOrDefault(device => ReferenceEquals(device, CollectionViewModel.Default))
            ?? devices.OrderBy(device => device.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    private IAppItemViewModel GetRepresentativeApp(IReadOnlyCollection<IAppItemViewModel> apps)
    {
        // The foreground app can have sessions on several devices. Prefer its default-device
        // session, then use stable identifiers instead of whichever session was enumerated last.
        var defaultDeviceId = CollectionViewModel.Default?.Id;
        return apps
            .OrderBy(app => app.Parent?.Id == defaultDeviceId ? 0 : 1)
            .ThenBy(app => app.Parent?.Id ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(app => app.AppId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(app => app.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static FormsScreen GetPointerScreen() => FormsScreen.FromPoint(FormsCursor.Position);

    private static unsafe FormsScreen GetForegroundScreen() => FormsScreen.FromHandle((IntPtr)PInvoke.GetForegroundWindow().Value);

    private IEnumerable<IAppItemViewModel> GetFocusedApps()
    {
        var foregroundAppIds = ForegroundAppResolver.TryGetForegroundAppIds(
            CollectionViewModel.AllDevices.SelectMany(device => device.AudioSessions));
        if (foregroundAppIds.Count == 0)
        {
            return Enumerable.Empty<IAppItemViewModel>();
        }

        return CollectionViewModel.AllDevices
            .SelectMany(device => device.Apps)
            .Where(app => foregroundAppIds.Contains(app.AppId))
            .ToArray();
    }
}
