using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kuromi.Models;
using Kuromi.Services;

namespace Kuromi.ViewModels.Widgets;

/// <summary>A single quota window row (5h / 7d / per-model) for the UI.</summary>
public class QuotaRow
{
    public string Label { get; init; } = "";
    public double Percent { get; init; }
    public string PercentText => $"{Percent:0}%";
    public string ResetText { get; init; } = "";
    public string BarColor => Kuromi.Palette.ForUsage(Percent);
}

public partial class ClaudeUsageViewModel : ViewModelBase, IDisposable
{
    private readonly ClaudeUsageService _service;
    private readonly ClaudeQuotaService _quotaService;
    private readonly DispatcherTimer _timer;       // ccusage (heavy/slow)
    private readonly DispatcherTimer _quotaTimer;   // quota limits (light HTTP)

    public ObservableCollection<QuotaRow> Quotas { get; } = new();
    [ObservableProperty] private bool _hasQuota;

    [ObservableProperty] private bool _loading = true;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string? _error;

    public bool HasError => !string.IsNullOrEmpty(Error);
    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));

    [ObservableProperty] private string _activeCost = "$0.00";
    [ObservableProperty] private string _activeTokens = "0";
    [ObservableProperty] private string _projectedCost = "$0.00";
    [ObservableProperty] private string _remaining = "";
    [ObservableProperty] private string _burnRate = "";
    [ObservableProperty] private string _models = "";
    [ObservableProperty] private double _blockProgress; // 0-100 of the 5h window
    [ObservableProperty] private string _totalCost = "";

    [ObservableProperty] private IReadOnlyList<double> _dailyCosts = Array.Empty<double>();
    [ObservableProperty] private string _dailyRange = "";

    public ClaudeUsageViewModel(ClaudeUsageService service, ClaudeQuotaService quotaService)
    {
        _service = service;
        _quotaService = quotaService;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();

        // Claude limits refresh every minute (independent of the slow ccusage call).
        _quotaTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _quotaTimer.Tick += async (_, _) => await RefreshQuotaAsync();
        _quotaTimer.Start();

        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Quota is a quick HTTP call -> fetch first so limits show immediately.
        await RefreshQuotaAsync();

        Loading = true;
        var u = await _service.FetchAsync();
        Loading = false;

        if (!u.HasData)
        {
            Error = u.Error ?? "Sem dados do ccusage";
            HasData = false;
            return;
        }

        Error = null;
        HasData = true;

        ActiveCost = $"${u.ActiveCostUsd:0.00}";
        ActiveTokens = FormatTokens(u.ActiveTokens);
        ProjectedCost = $"${u.ProjectedCostUsd:0.00}";
        Remaining = u.RemainingMinutes > 0
            ? $"{u.RemainingMinutes} min restantes no bloco"
            : "bloco encerrado";
        BurnRate = u.TokensPerMinute > 0
            ? $"{FormatTokens((long)u.TokensPerMinute)}/min · ${u.CostPerHour:0.0}/h"
            : "";
        Models = string.Join(" · ", u.ActiveModels.Select(Pretty));
        TotalCost = u.TotalCostUsd > 0 ? $"${u.TotalCostUsd:0.00} no total" : "";

        if (u.BlockStart is { } start && u.BlockEnd is { } end && end > start)
        {
            var total = (end - start).TotalMinutes;
            var done = (DateTime.Now - start).TotalMinutes;
            BlockProgress = Math.Clamp(done / total * 100, 0, 100);
        }

        DailyCosts = u.Daily.Select(d => d.CostUsd).ToArray();
        if (u.Daily.Count > 0)
            DailyRange = $"{u.Daily.First().Label} → {u.Daily.Last().Label}";
    }

    private async Task RefreshQuotaAsync()
    {
        var items = await _quotaService.FetchAsync();
        Quotas.Clear();
        foreach (var q in items)
            Quotas.Add(new QuotaRow
            {
                Label = q.Label,
                Percent = Math.Clamp(q.Utilization, 0, 100),
                ResetText = FormatReset(q.ResetsAt),
            });
        HasQuota = Quotas.Count > 0;
    }

    private static string FormatReset(DateTimeOffset? resetsAt)
    {
        if (resetsAt is not { } t) return "";
        var d = t.ToLocalTime() - DateTimeOffset.Now;
        if (d <= TimeSpan.Zero) return "renovando…";
        if (d.TotalDays >= 1) return $"reseta em {(int)d.TotalDays}d {d.Hours}h";
        if (d.TotalHours >= 1) return $"reseta em {(int)d.TotalHours}h {d.Minutes}min";
        return $"reseta em {d.Minutes}min";
    }

    private static string Pretty(string model)
    {
        if (model.Contains("opus")) return "Opus";
        if (model.Contains("sonnet")) return "Sonnet";
        if (model.Contains("haiku")) return "Haiku";
        return model;
    }

    private static string FormatTokens(long t)
    {
        if (t >= 1_000_000_000) return $"{t / 1_000_000_000.0:0.0}B";
        if (t >= 1_000_000) return $"{t / 1_000_000.0:0.0}M";
        if (t >= 1_000) return $"{t / 1_000.0:0.0}K";
        return t.ToString();
    }

    public void Dispose()
    {
        _timer.Stop();
        _quotaTimer.Stop();
    }
}
