using EarTrumpet.DataModel.AppInformation;
using EarTrumpet.Interop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace EarTrumpet.DataModel.Audio
{
    public static class ForegroundAppResolver
    {
        internal readonly struct SessionCandidate
        {
            public uint ProcessId { get; }
            public string AppId { get; }
            public SessionState State { get; }

            public SessionCandidate(uint processId, string appId, SessionState state)
            {
                ProcessId = processId;
                AppId = appId;
                State = state;
            }
        }

        public static IReadOnlyList<string> TryGetForegroundAppIds()
        {
            TryGetForegroundProcess(out _, out var foregroundAppIds);
            return foregroundAppIds;
        }

        public static IReadOnlyList<string> TryGetForegroundAppIds(IEnumerable<IAudioDeviceSession> groups)
        {
            if (!TryGetForegroundProcess(out var foregroundProcessId, out var foregroundAppIds))
            {
                return foregroundAppIds;
            }

            var foregroundAppIdSet = foregroundAppIds.ToHashSet(StringComparer.Ordinal);
            var candidates = EnumerateSessionCandidates(groups)
                .Where(candidate => !foregroundAppIdSet.Contains(candidate.AppId))
                .ToArray();
            var activeDescendantAppIds = GetActiveDescendantAppIds(
                foregroundProcessId,
                TryGetParentProcessIds(),
                candidates);

            if (activeDescendantAppIds.Count == 1)
            {
                Trace.WriteLine($"ForegroundAppResolver: Selected active descendant app {activeDescendantAppIds[0]}");
                return activeDescendantAppIds;
            }

            if (activeDescendantAppIds.Count > 1)
            {
                Trace.WriteLine($"ForegroundAppResolver: Found {activeDescendantAppIds.Count} active descendant apps; using foreground app");
            }
            else
            {
                Trace.WriteLine("ForegroundAppResolver: No active descendant app; using foreground app");
            }

            return foregroundAppIds;
        }

        private static bool TryGetForegroundProcess(out uint processId, out IReadOnlyList<string> appIds)
        {
            processId = 0;
            appIds = Array.Empty<string>();

            var hWnd = PInvoke.GetForegroundWindow();
            if (hWnd == (HWND)null)
            {
                Trace.WriteLine("ForegroundAppResolver: No Window (1)");
                return false;
            }

            var maxClassNameLength = (int)PInvoke.MAX_CLASS_NAME_LEN;
            Span<char> foregroundClassNameBuffer = stackalloc char[maxClassNameLength];
            foregroundClassNameBuffer.Clear();
            string foregroundClassName;
            unsafe
            {
                fixed (char* foregroundClassNamePtr = foregroundClassNameBuffer)
                {
                    PInvoke.GetClassName(hWnd, foregroundClassNamePtr, maxClassNameLength);
                    foregroundClassName = new PWSTR(foregroundClassNamePtr).ToString();
                }
            }

            if (foregroundClassName == "ApplicationFrameWindow")
            {
                hWnd = PInvoke.FindWindowEx(hWnd, (HWND)null, "Windows.UI.Core.CoreWindow", null);
            }

            if (hWnd == (HWND)null)
            {
                Trace.WriteLine("ForegroundAppResolver: No Window (2)");
                return false;
            }

            unsafe
            {
                uint resolvedProcessId;
                if (PInvoke.GetWindowThreadProcessId(hWnd, &resolvedProcessId) == 0 || resolvedProcessId == 0)
                {
                    Trace.WriteLine("ForegroundAppResolver: No foreground process");
                    return false;
                }

                processId = resolvedProcessId;
            }

            try
            {
                var appInfo = AppInformationFactory.CreateForProcess(processId);
                appIds = new[] { appInfo.PackageInstallPath, appInfo.AppId }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }

            return true;
        }

        private static IEnumerable<SessionCandidate> EnumerateSessionCandidates(IEnumerable<IAudioDeviceSession> groups)
        {
            if (groups == null)
            {
                yield break;
            }

            foreach (var group in groups.Where(group => group != null).ToArray())
            {
                var sessions = group.Children?.ToArray();
                if (sessions?.Length > 0)
                {
                    foreach (var session in sessions.Where(session => session != null))
                    {
                        yield return new SessionCandidate(session.ProcessId, group.AppId, session.State);
                    }
                }
                else
                {
                    yield return new SessionCandidate(group.ProcessId, group.AppId, group.State);
                }
            }
        }

        internal static IReadOnlyList<string> GetActiveDescendantAppIds(
            uint foregroundProcessId,
            IReadOnlyDictionary<uint, uint> parentProcessIds,
            IEnumerable<SessionCandidate> candidates)
        {
            if (foregroundProcessId == 0 || parentProcessIds == null || candidates == null)
            {
                return Array.Empty<string>();
            }

            return candidates
                .Where(candidate => candidate.State == SessionState.Active &&
                                    !string.IsNullOrWhiteSpace(candidate.AppId) &&
                                    IsStrictDescendant(candidate.ProcessId, foregroundProcessId, parentProcessIds))
                .Select(candidate => candidate.AppId)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(appId => appId, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsStrictDescendant(
            uint processId,
            uint ancestorProcessId,
            IReadOnlyDictionary<uint, uint> parentProcessIds)
        {
            if (processId == 0 || processId == ancestorProcessId)
            {
                return false;
            }

            var visitedProcessIds = new HashSet<uint>();
            var currentProcessId = processId;
            while (currentProcessId != 0 && visitedProcessIds.Add(currentProcessId))
            {
                if (!parentProcessIds.TryGetValue(currentProcessId, out var parentProcessId))
                {
                    return false;
                }

                if (parentProcessId == ancestorProcessId)
                {
                    return true;
                }

                currentProcessId = parentProcessId;
            }

            return false;
        }

        private static IReadOnlyDictionary<uint, uint> TryGetParentProcessIds()
        {
            const int maxAttempts = 3;
            const int bufferPadding = 64 * 1024;

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var status = Ntdll.NtQuerySystemInformationInitial(
                    Ntdll.SYSTEM_INFORMATION_CLASS.SystemProcessInformation,
                    IntPtr.Zero,
                    0,
                    out var requiredBufferLength);

                if (status != Ntdll.NTSTATUS.STATUS_INFO_LENGTH_MISMATCH || requiredBufferLength <= 0)
                {
                    Trace.WriteLine($"ForegroundAppResolver: Failed to size process snapshot ({status})");
                    return new Dictionary<uint, uint>();
                }

                var bufferLength = requiredBufferLength + bufferPadding;
                var buffer = Marshal.AllocHGlobal(bufferLength);
                try
                {
                    status = Ntdll.NtQuerySystemInformation(
                        Ntdll.SYSTEM_INFORMATION_CLASS.SystemProcessInformation,
                        buffer,
                        bufferLength,
                        IntPtr.Zero);
                    if (status == Ntdll.NTSTATUS.STATUS_INFO_LENGTH_MISMATCH)
                    {
                        continue;
                    }

                    if (status != Ntdll.NTSTATUS.SUCCESS)
                    {
                        Trace.WriteLine($"ForegroundAppResolver: Failed to read process snapshot ({status})");
                        return new Dictionary<uint, uint>();
                    }

                    var parentProcessIds = new Dictionary<uint, uint>();
                    var entryPointer = buffer;
                    Ntdll.SYSTEM_PROCESS_INFORMATION processInfo;
                    do
                    {
                        processInfo = Marshal.PtrToStructure<Ntdll.SYSTEM_PROCESS_INFORMATION>(entryPointer);
                        var currentProcessId = unchecked((uint)processInfo.UniqueProcessId);
                        var parentProcessId = unchecked((uint)processInfo.InheritedFromUniqueProcessId);
                        if (currentProcessId != 0)
                        {
                            parentProcessIds[currentProcessId] = parentProcessId;
                        }

                        entryPointer += processInfo.NextEntryOffset;
                    } while (processInfo.NextEntryOffset != 0);

                    return parentProcessIds;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            Trace.WriteLine("ForegroundAppResolver: Process snapshot kept changing; using foreground app");
            return new Dictionary<uint, uint>();
        }

        public static IAudioDeviceSession FindForegroundApp(ObservableCollection<IAudioDeviceSession> groups)
        {
            var groupSnapshot = groups?.Where(group => group != null).ToArray() ?? Array.Empty<IAudioDeviceSession>();
            var foregroundAppIds = TryGetForegroundAppIds(groupSnapshot);
            if (foregroundAppIds.Count == 0)
            {
                return null;
            }

            var group = groupSnapshot.FirstOrDefault(candidate => foregroundAppIds.Contains(candidate.AppId));
            if (group != null)
            {
                Trace.WriteLine($"ForegroundAppResolver: {group.DisplayName}");
            }
            else
            {
                Trace.WriteLine("ForegroundAppResolver: Didn't locate foreground app");
            }

            return group;
        }

    }
}
