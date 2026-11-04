namespace SaturnTweaks.Core.Models;

public sealed class SystemSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;

    // CPU
    public string CpuName { get; set; } = "Unknown";
    public int CpuCoreCount { get; set; }
    public int CpuThreadCount { get; set; }
    public double CpuCurrentUtilizationPercent { get; set; }
    public double? CpuTemperatureCelsius { get; set; }
    public double? CpuBaseClockMhz { get; set; }

    // GPU
    public List<GpuInfo> Gpus { get; set; } = new();

    // RAM
    public long TotalPhysicalMemoryBytes { get; set; }
    public long AvailablePhysicalMemoryBytes { get; set; }
    public double MemoryUsagePercent =>
        TotalPhysicalMemoryBytes > 0
            ? (1.0 - (double)AvailablePhysicalMemoryBytes / TotalPhysicalMemoryBytes) * 100.0
            : 0;

    // OS
    public string OsEdition { get; set; } = "Unknown";
    public string OsVersion { get; set; } = "Unknown";
    public int OsBuildNumber { get; set; }
    public string OsArchitecture { get; set; } = "x64";
    public bool IsWindows11 => OsBuildNumber >= 22000;

    // Motherboard
    public string MotherboardManufacturer { get; set; } = "Unknown";
    public string MotherboardProduct { get; set; } = "Unknown";
    public string BiosVersion { get; set; } = "Unknown";

    // Storage
    public List<DriveInfo2> Drives { get; set; } = new();

    // Power
    public string CurrentPowerPlan { get; set; } = "Unknown";
    public bool IsOnBattery { get; set; }
    public bool IsLaptop { get; set; }

    // Network
    public List<NetworkAdapterInfo> NetworkAdapters { get; set; } = new();

    // Display
    public List<MonitorInfo> Monitors { get; set; } = new();

    // Runtime environment
    public bool IsRunningAsAdministrator { get; set; }
    public int StartupApplicationCount { get; set; }

    // Capabilities detected
    public bool HagsSupported { get; set; }    // Hardware-Accelerated GPU Scheduling
    public bool HagsEnabled { get; set; }
    public bool GameModeEnabled { get; set; }
    public bool VbsEnabled { get; set; }       // Virtualization Based Security
    public bool HvciEnabled { get; set; }      // Memory Integrity
    public bool GameDvREnabled { get; set; }   // Game Bar / DVR
    public bool NvidiaDriverPresent { get; set; }
    public bool AmdDriverPresent { get; set; }
    public string NvidiaDriverVersion { get; set; } = "";
    public string AmdDriverVersion { get; set; } = "";
}

public sealed class GpuInfo
{
    public string Name { get; set; } = "Unknown";
    public string Vendor { get; set; } = "Unknown";   // NVIDIA, AMD, Intel, Other
    public long DedicatedVramBytes { get; set; }
    public string DriverVersion { get; set; } = "Unknown";
    public double? UtilizationPercent { get; set; }
    public double? TemperatureCelsius { get; set; }
    public bool IsIntegrated { get; set; }
    public long? CurrentVramUsageBytes { get; set; }
}

public sealed class DriveInfo2
{
    public string DriveLetter { get; set; } = "";
    public string Label { get; set; } = "";
    public long TotalBytes { get; set; }
    public long FreeBytes { get; set; }
    public double FreePercent => TotalBytes > 0 ? (double)FreeBytes / TotalBytes * 100.0 : 0;
    public string DriveType { get; set; } = "Unknown";  // SSD, HDD, NVMe, Unknown
    public string FileSystem { get; set; } = "NTFS";
    public string? SmartStatus { get; set; }
}

public sealed class NetworkAdapterInfo
{
    public string Name { get; set; } = "Unknown";
    public string Description { get; set; } = "";
    public string ConnectionType { get; set; } = "Unknown";  // Ethernet, WiFi, Other
    public string MacAddress { get; set; } = "";
    public long LinkSpeedBps { get; set; }
    public bool IsConnected { get; set; }
    public string IpAddress { get; set; } = "";
    public string Gateway { get; set; } = "";
    public List<string> DnsServers { get; set; } = new();
    public string DriverVersion { get; set; } = "Unknown";
    public string? WifiSsid { get; set; }
    public int? WifiSignalStrength { get; set; }
}

public sealed class MonitorInfo
{
    public string Name { get; set; } = "Unknown";
    public int WidthPixels { get; set; }
    public int HeightPixels { get; set; }
    public double RefreshRateHz { get; set; }
    public bool IsPrimary { get; set; }
    public bool HdrEnabled { get; set; }
    public bool VrrSupported { get; set; }
}
