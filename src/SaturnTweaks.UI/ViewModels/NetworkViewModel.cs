using System.Diagnostics;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaturnTweaks.Core.Models;
using System.Collections.ObjectModel;

namespace SaturnTweaks.UI.ViewModels;

public partial class NetworkViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private ObservableCollection<NetworkAdapterInfo> _adapters = new();
    [ObservableProperty] private NetworkAdapterInfo? _selectedAdapter;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string _pingTarget = "8.8.8.8";
    [ObservableProperty] private string _pingResult = "";
    [ObservableProperty] private string _dnsResult = "";
    [ObservableProperty] private double _latencyMs;
    [ObservableProperty] private double _jitterMs;
    [ObservableProperty] private int _packetLossPercent;
    [ObservableProperty] private ObservableCollection<PingMeasurement> _pingHistory = new();

    public NetworkViewModel(MainViewModel main)
    {
        _main = main;
        _main.PropertyChanged += OnMainPropertyChanged;
        if (_main.SystemSnapshot is not null)
            LoadAdapters();
    }

    private void OnMainPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SystemSnapshot))
            LoadAdapters();
    }

    private void LoadAdapters()
    {
        Adapters.Clear();
        if (_main.SystemSnapshot is null) return;
        foreach (var a in _main.SystemSnapshot.NetworkAdapters)
            Adapters.Add(a);
        SelectedAdapter = Adapters.FirstOrDefault(a => a.IsConnected);
    }

    [RelayCommand]
    private async Task RunPingTestAsync()
    {
        IsTesting = true;
        PingHistory.Clear();
        PingResult = "Testing…";

        try
        {
            var pings = new List<long>();
            using var pingSender = new Ping();
            const int count = 10;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var reply = await pingSender.SendPingAsync(PingTarget, 2000);
                    if (reply.Status == IPStatus.Success)
                    {
                        pings.Add(reply.RoundtripTime);
                        PingHistory.Add(new PingMeasurement
                        {
                            Sequence = i + 1,
                            LatencyMs = reply.RoundtripTime,
                            Success = true
                        });
                    }
                    else
                    {
                        PingHistory.Add(new PingMeasurement
                        {
                            Sequence = i + 1,
                            LatencyMs = 0,
                            Success = false,
                            Status = reply.Status.ToString()
                        });
                    }
                }
                catch
                {
                    PingHistory.Add(new PingMeasurement
                    {
                        Sequence = i + 1,
                        Success = false,
                        Status = "Timeout"
                    });
                }

                await Task.Delay(200);
            }

            int lost = PingHistory.Count(p => !p.Success);
            PacketLossPercent = (int)Math.Round((double)lost / count * 100);

            if (pings.Count > 0)
            {
                LatencyMs = pings.Average();
                JitterMs = pings.Count > 1
                    ? pings.Zip(pings.Skip(1), (a, b) => Math.Abs(b - a)).Average()
                    : 0;
                PingResult = $"Avg {LatencyMs:F1} ms · Jitter {JitterMs:F1} ms · " +
                             $"{PacketLossPercent}% loss ({count - lost}/{count} received)";
            }
            else
            {
                PingResult = $"All {count} packets lost — host unreachable or no network.";
            }
        }
        catch (Exception ex)
        {
            PingResult = $"Test failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task RunDnsTestAsync()
    {
        IsTesting = true;
        DnsResult = "Resolving…";
        try
        {
            var sw = Stopwatch.StartNew();
            var addresses = await System.Net.Dns.GetHostAddressesAsync("www.google.com");
            sw.Stop();
            DnsResult = addresses.Length > 0
                ? $"Resolved {addresses.Length} address(es) in {sw.ElapsedMilliseconds} ms. " +
                  $"First: {addresses[0]}"
                : "No addresses returned.";
        }
        catch (Exception ex)
        {
            DnsResult = $"DNS resolution failed: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }
}

public sealed class PingMeasurement
{
    public int Sequence { get; set; }
    public long LatencyMs { get; set; }
    public bool Success { get; set; }
    public string Status { get; set; } = "Success";
}
