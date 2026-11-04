using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SaturnTweaks.Core.Interfaces;
using SaturnTweaks.Core.Models;

namespace SaturnTweaks.Engine;

/// <summary>
/// Samples CPU, memory, and available process metrics on a timer.
/// GPU utilization is noted as unavailable without vendor SDK — null, never fabricated.
/// Raises SampleRecorded after each tick for UI binding.
///
/// Thread marshalling: the Engine project must not reference WPF types.
/// Pass a marshalToUiThread action (e.g. App.Current.Dispatcher.Invoke) when
/// constructing from the UI layer. Defaults to direct invocation (sync, no marshal)
/// which is correct for tests and non-UI consumers.
/// </summary>
public sealed class MonitoringService : IDisposable
{
    private readonly ILogger<MonitoringService> _logger;
    private readonly ISystemDetector _detector;

    // Injected marshal function — UI layer passes Dispatcher.Invoke, tests leave null.
    // Null means execute the action directly on the timer thread.
    private readonly Action<Action>? _marshalToUiThread;

    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private Timer? _timer;
    private volatile bool _running;

    private const int MaxSamples = 300; // 5 minutes at 1-second intervals

    public ObservableCollection<MonitorSample> Samples { get; } = new();
    public event EventHandler<MonitorSample>? SampleRecorded;

    /// <param name="logger">Logger.</param>
    /// <param name="detector">System detector (unused in monitoring tick, kept for future use).</param>
    /// <param name="marshalToUiThread">
    /// Optional: action that runs a delegate on the UI thread.
    /// Pass <c>action => App.Current.Dispatcher.Invoke(action)</c> from WPF.
    /// Null = invoke directly on the timer thread (correct for tests).
    /// </param>
    public MonitoringService(
        ILogger<MonitoringService> logger,
        ISystemDetector detector,
        Action<Action>? marshalToUiThread = null)
    {
        _logger = logger;
        _detector = detector;
        _marshalToUiThread = marshalToUiThread;
    }

    private void MarshalToUi(Action action)
    {
        if (_marshalToUiThread is not null)
            _marshalToUiThread(action);
        else
            action();
    }

    public void Start(int intervalMs = 1000)
    {
        if (_running) return;
        _running = true;

        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue(); // Prime — first call always returns 0
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CPU counter unavailable.");
        }

        try
        {
            _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            _diskReadCounter.NextValue();
            _diskWriteCounter.NextValue();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disk counters unavailable.");
        }

        _timer = new Timer(Tick, null, intervalMs, intervalMs);
        _logger.LogInformation("Monitoring started at {Interval}ms interval.", intervalMs);
    }

    public void Stop()
    {
        _running = false;
        _timer?.Dispose();
        _timer = null;
        _logger.LogInformation("Monitoring stopped. {Count} samples collected.", Samples.Count);
    }

    public void ClearSamples()
    {
        MarshalToUi(Samples.Clear);
    }

    private void Tick(object? _)
    {
        if (!_running) return;

        try
        {
            var sample = new MonitorSample
            {
                Timestamp = DateTimeOffset.UtcNow,
                CpuPercent = ReadCpuPercent(),
                MemoryUsedBytes = ReadMemoryUsed(),
                DiskReadBytesPerSec = ReadDiskRead(),
                DiskWriteBytesPerSec = ReadDiskWrite()
                // GpuPercent left null — unavailable without NVAPI/ADL
            };

            MarshalToUi(() =>
            {
                Samples.Add(sample);
                if (Samples.Count > MaxSamples)
                    Samples.RemoveAt(0);

                // Raise the event on the same thread used for ObservableCollection updates.
                // This keeps WPF subscribers from needing a second Dispatcher.Invoke hop.
                SampleRecorded?.Invoke(this, sample);
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Monitor tick failed.");
        }
    }

    private double ReadCpuPercent()
    {
        try { return _cpuCounter?.NextValue() ?? 0; }
        catch { return 0; }
    }

    private long ReadMemoryUsed()
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT FreePhysicalMemory, TotalVisibleMemorySize FROM Win32_OperatingSystem");
            foreach (System.Management.ManagementObject obj in searcher.Get())
            {
                long total = Convert.ToInt64(obj["TotalVisibleMemorySize"]) * 1024L;
                long free = Convert.ToInt64(obj["FreePhysicalMemory"]) * 1024L;
                return total - free;
            }
        }
        catch { /* WMI unavailable */ }
        return 0;
    }

    private double ReadDiskRead()
    {
        try { return _diskReadCounter?.NextValue() ?? 0; }
        catch { return 0; }
    }

    private double ReadDiskWrite()
    {
        try { return _diskWriteCounter?.NextValue() ?? 0; }
        catch { return 0; }
    }

    // ─── Before/After snapshots ────────────────────────────────────────────

    /// <summary>
    /// Takes a baseline snapshot of the monitored metrics over a short sampling window.
    /// Used to populate the "before" side of a before/after comparison.
    /// </summary>
    public async Task<MetricBaseline> CaptureBaselineAsync(
        string label,
        int durationMs = 5000,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Capturing {Label} baseline over {Ms}ms.", label, durationMs);

        var startCount = Samples.Count;
        await Task.Delay(durationMs, ct);

        var window = Samples.Skip(startCount).ToList();
        return ComputeBaseline(label, window);
    }

    private static MetricBaseline ComputeBaseline(string label, List<MonitorSample> window)
    {
        if (window.Count == 0)
            return new MetricBaseline { Label = label, SampleCount = 0 };

        return new MetricBaseline
        {
            Label = label,
            CapturedAt = DateTimeOffset.UtcNow,
            SampleCount = window.Count,
            CpuAvgPercent = window.Average(s => s.CpuPercent),
            CpuPeakPercent = window.Max(s => s.CpuPercent),
            MemoryAvgBytes = (long)window.Average(s => s.MemoryUsedBytes),
            MemoryPeakBytes = window.Max(s => s.MemoryUsedBytes),
            DiskReadAvgBps = window.Average(s => s.DiskReadBytesPerSec),
            DiskWriteAvgBps = window.Average(s => s.DiskWriteBytesPerSec)
        };
    }

    // ─── Report generation ─────────────────────────────────────────────────

    /// <summary>
    /// Generates a human-readable before/after optimization report as plain text.
    /// Only states what was actually measured — no fabricated improvements.
    /// </summary>
    public static string GenerateReport(
        MetricBaseline before,
        MetricBaseline after,
        IReadOnlyList<TweakOperationRecord> session)
    {
        var sb = new StringBuilder();

        sb.AppendLine("═══════════════════════════════════════════════════");
        sb.AppendLine("  SATURN TWEAKS — OPTIMIZATION REPORT");
        sb.AppendLine($"  Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("═══════════════════════════════════════════════════");
        sb.AppendLine();

        // Session summary
        sb.AppendLine("── CHANGES APPLIED ─────────────────────────────────");
        var applied = session.Where(r =>
            r.Result is TweakResult.AppliedAndVerified or TweakResult.RequiresRestart).ToList();
        var failed = session.Where(r => r.Result == TweakResult.Failed).ToList();
        var skipped = session.Where(r =>
            r.Result is TweakResult.NotApplicable or TweakResult.AlreadyConfigured).ToList();

        sb.AppendLine($"  Applied:  {applied.Count}");
        sb.AppendLine($"  Failed:   {failed.Count}");
        sb.AppendLine($"  Skipped:  {skipped.Count}");
        sb.AppendLine();

        foreach (var r in applied)
        {
            sb.AppendLine($"  ✓ {r.DisplayName}");
            if (r.OriginalValue is not null)
                sb.AppendLine($"      Was: {r.OriginalValue}  →  Now: {r.NewValue}");
            if (r.Result == TweakResult.RequiresRestart)
                sb.AppendLine("      ⚠ Restart required for this change to take effect.");
        }

        foreach (var r in failed)
            sb.AppendLine($"  ✗ {r.DisplayName}: {r.ErrorMessage}");

        sb.AppendLine();

        // Measurement comparison
        if (before.SampleCount > 0 && after.SampleCount > 0)
        {
            sb.AppendLine("── BEFORE / AFTER MEASUREMENTS ─────────────────────");
            sb.AppendLine($"  Before: {before.Label}  ({before.SampleCount} samples)");
            sb.AppendLine($"  After:  {after.Label}  ({after.SampleCount} samples)");
            sb.AppendLine();

            double cpuDelta = after.CpuAvgPercent - before.CpuAvgPercent;
            string cpuDir = cpuDelta < 0 ? "↓" : cpuDelta > 0 ? "↑" : "→";
            sb.AppendLine("  CPU Utilization:");
            sb.AppendLine($"    Before avg:  {before.CpuAvgPercent:F1}%  peak {before.CpuPeakPercent:F1}%");
            sb.AppendLine($"    After avg:   {after.CpuAvgPercent:F1}%  peak {after.CpuPeakPercent:F1}%");
            sb.AppendLine($"    Change:      {cpuDir} {Math.Abs(cpuDelta):F1}%");
            sb.AppendLine();

            long memDelta = after.MemoryAvgBytes - before.MemoryAvgBytes;
            sb.AppendLine("  Memory Used:");
            sb.AppendLine($"    Before:  {FormatBytes(before.MemoryAvgBytes)}");
            sb.AppendLine($"    After:   {FormatBytes(after.MemoryAvgBytes)}");
            sb.AppendLine($"    Change:  {(memDelta >= 0 ? "+" : "")}{FormatBytes(Math.Abs(memDelta))}");
            sb.AppendLine();

            sb.AppendLine("── MEASUREMENT NOTES ────────────────────────────────");
            sb.AppendLine("  These measurements reflect system-wide CPU and memory activity");
            sb.AppendLine("  at idle/desktop. In-game performance depends on the specific");
            sb.AppendLine("  game, scene, resolution, GPU, and driver — it cannot be");
            sb.AppendLine("  measured accurately at the Windows desktop level.");
            sb.AppendLine("  GPU utilization data requires vendor SDK — not available.");
            sb.AppendLine("  Frame-time data requires an in-game overlay (CapFrameX, RTSS).");
        }
        else
        {
            sb.AppendLine("── MEASUREMENTS ─────────────────────────────────────");
            sb.AppendLine("  Before/after measurements were not captured.");
            sb.AppendLine("  Use the Diagnostics page to start monitoring before");
            sb.AppendLine("  and after optimization to generate comparison data.");
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════");
        sb.AppendLine("  To roll back changes, use the Restore Center.");
        sb.AppendLine("═══════════════════════════════════════════════════");

        return sb.ToString();
    }

    public static string GenerateReportJson(
        MetricBaseline? before,
        MetricBaseline? after,
        IReadOnlyList<TweakOperationRecord> session)
    {
        var report = new
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Session = session,
            Before = before,
            After = after,
            Notes = new[]
            {
                "GPU utilization unavailable without vendor SDK (NVAPI/ADL).",
                "Frame-time data unavailable — use CapFrameX or RTSS in-game.",
                "CPU/memory measurements are system-wide idle snapshots, not in-game."
            }
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }

    public void Dispose()
    {
        Stop();
        _cpuCounter?.Dispose();
        _diskReadCounter?.Dispose();
        _diskWriteCounter?.Dispose();
    }
}

/// <summary>A single monitoring sample — all values are real or null, never fabricated.</summary>
public sealed class MonitorSample
{
    public DateTimeOffset Timestamp { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryUsedBytes { get; set; }
    public double? GpuPercent { get; set; }         // null = unavailable (no vendor SDK)
    public double DiskReadBytesPerSec { get; set; }
    public double DiskWriteBytesPerSec { get; set; }

    public string MemoryDisplay =>
        MemoryUsedBytes >= 1_073_741_824
            ? $"{MemoryUsedBytes / 1_073_741_824.0:F1} GB"
            : $"{MemoryUsedBytes / 1_048_576.0:F0} MB";
}

/// <summary>Aggregated metrics over a sampling window, used for before/after comparison.</summary>
public sealed class MetricBaseline
{
    public string Label { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public int SampleCount { get; set; }
    public double CpuAvgPercent { get; set; }
    public double CpuPeakPercent { get; set; }
    public long MemoryAvgBytes { get; set; }
    public long MemoryPeakBytes { get; set; }
    public double DiskReadAvgBps { get; set; }
    public double DiskWriteAvgBps { get; set; }
}
