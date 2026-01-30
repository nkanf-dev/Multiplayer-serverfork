using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Multiplayer.Common;

namespace Server;

public sealed class ServerAdminStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public bool whitelistEnabled;
    public HashSet<string> whitelist = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> bans = new(StringComparer.OrdinalIgnoreCase);

    public static ServerAdminStore Load(string path)
    {
        if (!File.Exists(path))
            return new ServerAdminStore();

        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<StoreData>(json);
        if (data == null)
            return new ServerAdminStore();

        var store = new ServerAdminStore
        {
            whitelistEnabled = data.whitelistEnabled
        };
        foreach (var user in data.whitelist ?? new List<string>())
            store.whitelist.Add(user);
        foreach (var user in data.bans ?? new List<string>())
            store.bans.Add(user);

        return store;
    }

    public void Save(string path)
    {
        var data = new StoreData
        {
            whitelistEnabled = whitelistEnabled,
            whitelist = new List<string>(whitelist),
            bans = new List<string>(bans)
        };
        File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));
    }

    public void ApplyTo(MultiplayerServer server)
    {
        server.WhitelistEnabled = whitelistEnabled;
        server.WhitelistedUsers.Clear();
        server.BannedUsers.Clear();

        foreach (var user in whitelist)
            server.WhitelistedUsers.Add(user);
        foreach (var user in bans)
            server.BannedUsers.Add(user);
    }

    private sealed class StoreData
    {
        public bool whitelistEnabled { get; set; }
        public List<string>? whitelist { get; set; }
        public List<string>? bans { get; set; }
    }
}
