using GoldsrcNetClient.Core.Query;
using GoldsrcNetClient.Tui.Models;

namespace GoldsrcNetClient.Tui.Services;

/// <summary>A saved server plus the latest A2S info observed for it.</summary>
public sealed class ServerEntry
{
    public required ServerConfig Config { get; init; }

    /// <summary>Info from the last successful A2S query; null until queried or when the query failed.</summary>
    public A2SInfo? Info { get; set; }

    /// <summary>Whether the last A2S query failed (server down, timeout, ...).</summary>
    public bool QueryFailed { get; set; }

    /// <summary>True while an A2S query is in flight.</summary>
    public bool Querying { get; set; }

    /// <summary>Name shown in the browser: A2S name when known, else the user's label, else host:port.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Info?.ServerName))
                return Info.ServerName;
            if (!string.IsNullOrWhiteSpace(Config.Name))
                return Config.Name;
            return $"{Config.Host}:{Config.Port}";
        }
    }

    /// <summary>Single line rendered in the browser list.</summary>
    public string Row
    {
        get
        {
            string name = DisplayName;
            if (name.Length > 34)
                name = name[..33] + "…";

            string suffix = Querying ? "…"
                : Info != null ? $"{Info.Players}/{Info.MaxPlayers}  {Info.PingMs}ms"
                : QueryFailed ? "offline"
                : $"{Config.Host}:{Config.Port}";

            return $"{name,-35} {suffix}";
        }
    }
}

/// <summary>
/// Keeps A2S info for each saved server up to date and exposes a stable row list
/// for the browser UI. Queries run concurrently in the background; the UI polls
/// <see cref="Version"/> to notice changes.
/// </summary>
public sealed class ServerBrowser(ServerConfigStore configStore)
{
    private readonly Lock _lock = new();
    private List<ServerEntry> _entries = [];
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Incremented whenever entries or their A2S data change.</summary>
    public int Version { get; private set; }

    /// <summary>A snapshot of the current entries.</summary>
    public IReadOnlyList<ServerEntry> Entries
    {
        get { lock (_lock) return [.. _entries]; }
    }

    public void Reload()
    {
        IReadOnlyList<ServerEntry> old = Entries;
        IReadOnlyList<ServerConfig> configs = configStore.Configs;
        lock (_lock)
        {
            _entries = configs.Select(c =>
            {
                ServerEntry? previous = old.FirstOrDefault(e =>
                    e.Config.Host == c.Host && e.Config.Port == c.Port && e.Config.AppId == c.AppId);
                return new ServerEntry { Config = c, Info = previous?.Info, QueryFailed = previous?.QueryFailed ?? false };
            }).ToList();
            Version++;
        }
        RefreshAll();
    }

    /// <summary>Queues an A2S query for every entry.</summary>
    public void RefreshAll()
    {
        IReadOnlyList<ServerEntry> snapshot = Entries;
        for (int i = 0; i < snapshot.Count; i++)
            Refresh(i);
    }

    /// <summary>Queues an A2S query for one entry (by index in <see cref="Entries"/>).</summary>
    public void Refresh(int index)
    {
        ServerEntry? entry = Entries.ElementAtOrDefault(index);
        if (entry == null || entry.Querying)
            return;

        entry.Querying = true;
        Bump();

        ServerConfig config = entry.Config;
        CancellationToken ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            A2SInfo? info = null;
            try
            {
                info = await A2SQuerier.QueryInfoAsync(config.Host, config.Port, ct);
            }
            catch { }

            ServerEntry? target = Entries.FirstOrDefault(e =>
                ReferenceEquals(e.Config, config) ||
                (e.Config.Host == config.Host && e.Config.Port == config.Port && e.Config.AppId == config.AppId));
            if (target != null)
            {
                target.Querying = false;
                target.Info = info;
                target.QueryFailed = info == null;
            }
            Bump();
        }, ct);
    }

    private void Bump()
    {
        lock (_lock) Version++;
    }
}
