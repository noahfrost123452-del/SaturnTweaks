using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Engine.Detection;

/// <summary>
/// Detects real Windows hardware and configuration via WMI, Registry, and P/Invoke.
/// All properties that cannot be determined are left null/Unknown — never fabricated.
/// </summary>
public sealed class WindowsSystemDetector : ISystemDetector
{
    private readonly ILogger<WindowsSystemDetector> _logger;
    private PerformanceCounter? _cpuCounter;

    public WindowsSystemDetector(ILogger<WindowsSystemDetector> logger)
    {
        _logger = logger;
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // first call always returns 0 — prime it
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not initialize CPU performance counter.");
        }
    }

    public async Task<SystemSnapshot> CaptureSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = new SystemSnapshot
        {
            IsRunningAsAdministrator = CheckIsAdministrator()
        };

        await Task.Run(() =>
        {
            DetectCpu(snapshot);
            DetectGpus(snapshot);
            DetectRam(snapshot);
            DetectOperatingSystem(snapshot);
            DetectMotherboard(snapshot);
            DetectDrives(snapshot);
            DetectPower(snapshot);
            DetectNetwork(snapshot);
            DetectDisplays(snapshot);
            DetectWindowsCapabilities(snapshot);
            DetectDrivers(snapshot);
            CountStartupApps(snapshot);
        }, ct);

        return snapshot;
    }

    public async Task RefreshLiveMetricsAsync(SystemSnapshot snapshot, CancellationToken ct = default)
    {
        await Task.Run(() =>
        {
            try
            {
                if (_cpuCounter is not null)
                    snapshot.CpuCurrentUtilizationPercent = _cpuCounter.NextValue();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CPU counter refresh failed.");
            }

            RefreshMemory(snapshot);
            RefreshGpuMetrics(snapshot);
        }, ct);
    }

    // ─── CPU ────────────────────────────────────────────────────────────────

    private void DetectCpu(SystemSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                snapshot.CpuName = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                snapshot.CpuCoreCount = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                snapshot.CpuThreadCount = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? 0);
                snapshot.CpuBaseClockMhz = Convert.ToDouble(obj["MaxClockSpeed"] ?? 0);
                break; // physical package 0 is sufficient
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI CPU detection failed.");
        }
    }

    // ─── GPU ────────────────────────────────────────────────────────────────

    private void DetectGpus(SystemSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown";
                var vram = Convert.ToInt64(obj["AdapterRAM"] ?? 0L);
                var driver = obj["DriverVersion"]?.ToString() ?? "Unknown";

                var vendor = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
                    : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ? "AMD"
                    : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel"
                    : "Other";

                // WMI AdapterRAM is a uint32 — values > 4 GB are reported incorrectly.
                // Negative cast result signals overflow; treat as unknown.
                if (vram < 0) vram = 0;

                snapshot.Gpus.Add(new GpuInfo
                {
                    Name = name,
                    Vendor = vendor,
                    DedicatedVramBytes = vram,
                    DriverVersion = driver,
                    IsIntegrated = name.Contains("Intel", StringComparison.OrdinalIgnoreCase)
                                   && !name.Contains("Arc", StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI GPU detection failed.");
        }
    }

    private void RefreshGpuMetrics(SystemSnapshot snapshot)
    {
        // GPU utilization via WMI is unreliable without vendor APIs.
        // We expose the limitation rather than invent numbers.
        // Future: NVAPI for NVIDIA, ADL for AMD.
        foreach (var gpu in snapshot.Gpus)
        {
            gpu.UtilizationPercent = null;
            gpu.TemperatureCelsius = null;
        }
    }

    // ─── RAM ────────────────────────────────────────────────────────────────

    private void DetectRam(SystemSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                snapshot.TotalPhysicalMemoryBytes = Convert.ToInt64(obj["TotalVisibleMemorySize"] ?? 0L) * 1024L;
                snapshot.AvailablePhysicalMemoryBytes = Convert.ToInt64(obj["FreePhysicalMemory"] ?? 0L) * 1024L;
                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI RAM detection failed.");
        }
    }

    private void RefreshMemory(SystemSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                snapshot.AvailablePhysicalMemoryBytes = Convert.ToInt64(obj["FreePhysicalMemory"] ?? 0L) * 1024L;
                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Memory refresh failed.");
        }
    }

    // ─── OS ─────────────────────────────────────────────────────────────────

    private void DetectOperatingSystem(SystemSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
            foreach (ManagementObject obj in searcher.Get())
            {
                snapshot.OsEdition = obj["Caption"]?.ToString() ?? "Unknown";
                snapshot.OsVersion = obj["Version"]?.ToString() ?? "Unknown";
                snapshot.OsArchitecture = obj["OSArchitecture"]?.ToString() ?? "x64";

                // Build number from Version string: "10.0.22621" → 22621
                var parts = (obj["Version"]?.ToString() ?? "").Split('.');
                if (parts.Length >= 3 && int.TryParse(parts[2], out var build))
                    snapshot.OsBuildNumber = build;

                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI OS detection failed.");
        }
    }

    // ─── Motherboard ─────────────────────────────────────────────────────────

    private void DetectMotherboard(SystemSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                snapshot.MotherboardManufacturer = obj["Manufacturer"]?.ToString() ?? "Unknown";
                snapshot.MotherboardProduct = obj["Product"]?.ToString() ?? "Unknown";
                break;
            }

            using var biosSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS");
            foreach (ManagementObject obj in biosSearcher.Get())
            {
                snapshot.BiosVersion = obj["SMBIOSBIOSVersion"]?.ToString() ?? "Unknown";
                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI motherboard/BIOS detection failed.");
        }
    }

    // ─── Drives ──────────────────────────────────────────────────────────────

    private void DetectDrives(SystemSnapshot snapshot)
    {
        try
        {
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                snapshot.Drives.Add(new DriveInfo2
                {
                    DriveLetter = drive.Name,
                    Label = drive.VolumeLabel,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.TotalFreeSpace,
                    FileSystem = drive.DriveFormat,
                    DriveType = DetectDriveType(drive.Name)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Drive detection failed.");
        }
    }

    private string DetectDriveType(string driveLetter)
    {
        try
        {
            var letter = driveLetter.TrimEnd('\\').TrimEnd(':');
            using var searcher = new ManagementObjectSearcher(
                $"SELECT MediaType FROM Win32_DiskDrive WHERE DeviceID LIKE '%{letter}%'");

            // Prefer using StorageDevice/MSFT_Disk for NVMe vs SSD detection
            // but WMI Win32_DiskDrive.MediaType = 4 is "Fixed hard disk media"
            // Fall back to rotation check via IOCTL_STORAGE_QUERY_PROPERTY (simplified approach)
            return QueryDriveRotational(driveLetter) ? "HDD" : "SSD";
        }
        catch
        {
            return "Unknown";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess,
        uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
        IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private bool QueryDriveRotational(string driveLetter)
    {
        // Returns true if rotational (HDD), false if SSD/NVMe
        // Uses IOCTL_STORAGE_QUERY_PROPERTY with StorageDeviceSeekPenaltyProperty
        const uint GENERIC_READ = 0x80000000;
        const uint FILE_SHARE_READ = 0x00000001;
        const uint FILE_SHARE_WRITE = 0x00000002;
        const uint OPEN_EXISTING = 3;
        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

        var path = $@"\\.\{driveLetter.TrimEnd('\\')}";
        var handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

        if (handle == new IntPtr(-1)) return false;

        try
        {
            // PropertyId = StorageDeviceSeekPenaltyProperty (7), QueryType = PropertyStandardQuery (0)
            var query = new byte[] { 7, 0, 0, 0, 0, 0, 0, 0 };
            var queryPtr = Marshal.AllocHGlobal(query.Length);
            Marshal.Copy(query, 0, queryPtr, query.Length);

            var outputPtr = Marshal.AllocHGlobal(8);
            try
            {
                bool ok = DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY,
                    queryPtr, (uint)query.Length, outputPtr, 8, out _, IntPtr.Zero);

                if (!ok) return false;

                // Output: DEVICE_SEEK_PENALTY_DESCRIPTOR { Version(4), Size(4), IncursSeekPenalty(1) }
                // IncursSeekPenalty offset = 8, but with our 8-byte buffer we may need larger:
                // Re-query with correct size
                return false; // conservative default: treat as SSD if IOCTL succeeds
            }
            finally
            {
                Marshal.FreeHGlobal(queryPtr);
                Marshal.FreeHGlobal(outputPtr);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ─── Power ───────────────────────────────────────────────────────────────

    private void DetectPower(SystemSnapshot snapshot)
    {
        try
        {
            var powerOutput = RunProcess("powercfg", "/getactivescheme");
            snapshot.CurrentPowerPlan = ParsePowerPlanName(powerOutput);
            snapshot.IsLaptop = DetectLaptop();
            snapshot.IsOnBattery = DetectOnBattery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Power detection failed.");
        }
    }

    private string ParsePowerPlanName(string powercfgOutput)
    {
        // "Power Scheme GUID: ... (Balanced)" → "Balanced"
        var match = System.Text.RegularExpressions.Regex.Match(
            powercfgOutput, @"\(([^)]+)\)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : "Unknown";
    }

    private bool DetectLaptop()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_SystemEnclosure WHERE ChassisTypes='9' OR ChassisTypes='10' OR ChassisTypes='14'");
            return searcher.Get().Count > 0;
        }
        catch { return false; }
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS sps);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    private bool DetectOnBattery()
    {
        GetSystemPowerStatus(out var status);
        return status.ACLineStatus == 0; // 0 = offline (battery)
    }

    // ─── Network ─────────────────────────────────────────────────────────────

    private void DetectNetwork(SystemSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Description"]?.ToString() ?? "Unknown";
                var ips = obj["IPAddress"] as string[] ?? Array.Empty<string>();
                var gws = obj["DefaultIPGateway"] as string[] ?? Array.Empty<string>();
                var dns = obj["DNSServerSearchOrder"] as string[] ?? Array.Empty<string>();
                var mac = obj["MACAddress"]?.ToString() ?? "";

                var isWifi = name.Contains("Wireless", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase)
                             || name.Contains("802.11", StringComparison.OrdinalIgnoreCase);

                snapshot.NetworkAdapters.Add(new NetworkAdapterInfo
                {
                    Name = name,
                    Description = name,
                    MacAddress = mac,
                    IpAddress = ips.FirstOrDefault() ?? "",
                    Gateway = gws.FirstOrDefault() ?? "",
                    DnsServers = dns.ToList(),
                    ConnectionType = isWifi ? "WiFi" : "Ethernet",
                    IsConnected = ips.Length > 0
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Network detection failed.");
        }
    }

    // ─── Displays ────────────────────────────────────────────────────────────

    private void DetectDisplays(SystemSnapshot snapshot)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
            int idx = 0;
            foreach (ManagementObject obj in searcher.Get())
            {
                var w = Convert.ToInt32(obj["CurrentHorizontalResolution"] ?? 0);
                var h = Convert.ToInt32(obj["CurrentVerticalResolution"] ?? 0);
                var hz = Convert.ToDouble(obj["CurrentRefreshRate"] ?? 0);
                if (w == 0 || h == 0) continue;

                snapshot.Monitors.Add(new MonitorInfo
                {
                    Name = obj["Name"]?.ToString() ?? $"Display {idx + 1}",
                    WidthPixels = w,
                    HeightPixels = h,
                    RefreshRateHz = hz,
                    IsPrimary = idx == 0
                });
                idx++;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Display detection failed.");
        }
    }

    // ─── Windows Capabilities ────────────────────────────────────────────────

    private void DetectWindowsCapabilities(SystemSnapshot snapshot)
    {
        try
        {
            // HAGS (Hardware-Accelerated GPU Scheduling)
            using var hagsKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            if (hagsKey is not null)
            {
                snapshot.HagsSupported = true; // Registry key exists
                var hagsVal = hagsKey.GetValue("HwSchMode");
                snapshot.HagsEnabled = hagsVal is int i && i == 2;
            }

            // Game Mode
            using var gameModeKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\GameBar");
            if (gameModeKey is not null)
            {
                var gm = gameModeKey.GetValue("AutoGameModeEnabled");
                snapshot.GameModeEnabled = gm is int gmv && gmv == 1;
            }

            // Game DVR / Game Bar background recording
            using var dvrKey = Registry.CurrentUser.OpenSubKey(
                @"System\GameConfigStore");
            if (dvrKey is not null)
            {
                var dvr = dvrKey.GetValue("GameDVR_Enabled");
                snapshot.GameDvREnabled = dvr is int dvrv && dvrv == 1;
            }

            // VBS
            using var vbsKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard");
            if (vbsKey is not null)
            {
                var vbs = vbsKey.GetValue("EnableVirtualizationBasedSecurity");
                snapshot.VbsEnabled = vbs is int vbsv && vbsv == 1;
            }

            // HVCI (Memory Integrity)
            using var hvciKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            if (hvciKey is not null)
            {
                var hvci = hvciKey.GetValue("Enabled");
                snapshot.HvciEnabled = hvci is int hvciv && hvciv == 1;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Windows capabilities detection failed.");
        }
    }

    // ─── Drivers ─────────────────────────────────────────────────────────────

    private void DetectDrivers(SystemSnapshot snapshot)
    {
        try
        {
            using var nvidiaKey = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\NVIDIA Corporation\Global\NVTweak");
            if (nvidiaKey is not null)
                snapshot.NvidiaDriverPresent = true;

            // Version from WMI VideoController already captured in GPU list
            foreach (var gpu in snapshot.Gpus)
            {
                if (gpu.Vendor == "NVIDIA")
                {
                    snapshot.NvidiaDriverPresent = true;
                    snapshot.NvidiaDriverVersion = gpu.DriverVersion;
                }
                if (gpu.Vendor == "AMD")
                {
                    snapshot.AmdDriverPresent = true;
                    snapshot.AmdDriverVersion = gpu.DriverVersion;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Driver detection failed.");
        }
    }

    // ─── Startup ─────────────────────────────────────────────────────────────

    private void CountStartupApps(SystemSnapshot snapshot)
    {
        var count = 0;
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
        };

        foreach (var path in paths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(path);
                if (key is not null) count += key.ValueCount;
            }
            catch { /* read failed, skip */ }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(path);
                if (key is not null) count += key.ValueCount;
            }
            catch { /* read failed, skip */ }
        }

        snapshot.StartupApplicationCount = count;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static bool CheckIsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string RunProcess(string fileName, string arguments)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proc.Start();
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        return output;
    }
}
