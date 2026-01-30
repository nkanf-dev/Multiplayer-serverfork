using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Multiplayer.Common;
using Multiplayer.Common.Networking.Packet;
using Multiplayer.Common.Util;

namespace Server;

public interface IServerCommandSource
{
    void Reply(string message);
}

public sealed class ConsoleCommandSource : IServerCommandSource
{
    public void Reply(string message) => ServerLog.Log(message);
}

public sealed class ServerCommandContext
{
    public MultiplayerServer Server { get; }
    public ServerSettings Settings { get; }
    public ServerAdminStore AdminStore { get; }
    public string SettingsFile { get; }
    public string AdminFile { get; }

    public ServerCommandContext(MultiplayerServer server, ServerSettings settings, ServerAdminStore adminStore, string settingsFile, string adminFile)
    {
        Server = server;
        Settings = settings;
        AdminStore = adminStore;
        SettingsFile = settingsFile;
        AdminFile = adminFile;
    }
}

public interface IServerCommand
{
    string Name { get; }
    string Usage { get; }
    string Description { get; }
    void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args);
}

public sealed class ServerCommandManager
{
    private readonly MultiplayerServer server;
    private readonly Dictionary<string, IServerCommand> commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly ServerCommandContext context;

    public ServerCommandManager(MultiplayerServer server, ServerSettings settings, ServerAdminStore adminStore, string settingsFile, string adminFile)
    {
        this.server = server;
        context = new ServerCommandContext(server, settings, adminStore, settingsFile, adminFile);
        RegisterDefaults();
    }

    public void ExecuteLine(IServerCommandSource source, string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var trimmed = line.Trim();
        if (trimmed.StartsWith("/"))
            trimmed = trimmed[1..];

        var parts = ParseArgs(trimmed);
        if (parts.Count == 0)
            return;

        var name = parts[0];
        var args = parts.Skip(1).ToArray();

        if (!commands.TryGetValue(name, out var cmd))
        {
            source.Reply($"Unknown command '{name}'. Type /help for help.");
            return;
        }

        server.Enqueue(() =>
        {
            try
            {
                cmd.Execute(context, source, args);
            }
            catch (Exception ex)
            {
                source.Reply($"Command '{name}' failed: {ex.Message}");
            }
        });
    }

    private void RegisterDefaults()
    {
        Register(new HelpCommand(commands));
        Register(new StatusCommand());
        Register(new ListCommand());
        Register(new StopCommand());
        Register(new SayCommand());
        Register(new JoinPointCommand());
        Register(new KickCommand());
        Register(new SetHostCommand());
        Register(new HostCommand());
        Register(new BanCommand());
        Register(new PardonCommand());
        Register(new BanListCommand());
        Register(new WhitelistCommand());
        Register(new PauseCommand());
        Register(new TimeVoteCommand());
        Register(new SaveSettingsCommand());
        Register(new SaveServerStateCommand());
        Register(new LoadSaveCommand());
    }

    private void Register(IServerCommand command) => commands[command.Name] = command;

    private static List<string> ParseArgs(string input)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}

public sealed class HelpCommand : IServerCommand
{
    private readonly IDictionary<string, IServerCommand> commands;
    public string Name => "help";
    public string Usage => "/help [command]";
    public string Description => "Show available commands or help for one command.";

    public HelpCommand(IDictionary<string, IServerCommand> commands) => this.commands = commands;

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length == 0)
        {
            source.Reply("Commands: " + string.Join(", ", commands.Keys.OrderBy(k => k)));
            source.Reply("Use /help <command> for usage.");
            return;
        }

        var name = args[0];
        if (!commands.TryGetValue(name, out var cmd))
        {
            source.Reply($"Unknown command '{name}'.");
            return;
        }

        source.Reply($"{cmd.Usage} - {cmd.Description}");
    }
}

public sealed class StatusCommand : IServerCommand
{
    public string Name => "status";
    public string Usage => "/status";
    public string Description => "Show server status.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        var server = ctx.Server;
        var host = server.hostUsername ?? "(none)";
        var players = server.JoinedPlayers.Count();
        var playing = server.PlayingPlayers.Count();
        var started = server.FullyStarted;
        source.Reply($"running={server.running} started={started} host={host} players={players} playing={playing} tick={server.gameTimer}");
    }
}

public sealed class ListCommand : IServerCommand
{
    public string Name => "list";
    public string Usage => "/list";
    public string Description => "List connected players.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        var players = ctx.Server.JoinedPlayers.ToList();
        if (players.Count == 0)
        {
            source.Reply("No players online.");
            return;
        }

        var list = players.Select(p => $"{p.Username}{(p.IsHost ? "*" : "")}");
        source.Reply("Players: " + string.Join(", ", list));
    }
}

public sealed class StopCommand : IServerCommand
{
    public string Name => "stop";
    public string Usage => "/stop";
    public string Description => "Stop the server.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        ctx.Server.running = false;
        source.Reply("Stopping server.");
    }
}

public sealed class SayCommand : IServerCommand
{
    public string Name => "say";
    public string Usage => "/say <message>";
    public string Description => "Broadcast a message to all players.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length == 0)
        {
            source.Reply("Usage: /say <message>");
            return;
        }

        ctx.Server.SendChat("[Server] " + string.Join(" ", args));
    }
}

public sealed class JoinPointCommand : IServerCommand
{
    public string Name => "joinpoint";
    public string Usage => "/joinpoint";
    public string Description => "Trigger joinpoint creation.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (!ctx.Server.worldData.TryStartJoinPointCreation(true))
            source.Reply("Join point creation already in progress.");
    }
}

public sealed class KickCommand : IServerCommand
{
    public string Name => "kick";
    public string Usage => "/kick <player> [reason]";
    public string Description => "Kick a player.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length < 1)
        {
            source.Reply("Usage: /kick <player> [reason]");
            return;
        }

        var player = ctx.Server.GetPlayer(args[0]);
        if (player == null)
        {
            source.Reply("Player not found.");
            return;
        }

        if (player.IsHost)
        {
            source.Reply("Can't kick the host. Use /sethost first.");
            return;
        }

        player.Disconnect(MpDisconnectReason.Kick);
    }
}

public sealed class SetHostCommand : IServerCommand
{
    public string Name => "sethost";
    public string Usage => "/sethost <player>";
    public string Description => "Assign host to a player.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length < 1)
        {
            source.Reply("Usage: /sethost <player>");
            return;
        }

        var player = ctx.Server.GetPlayer(args[0]);
        if (player == null)
        {
            source.Reply("Player not found.");
            return;
        }

        ctx.Server.playerManager.SetHost(player);
        source.Reply($"Host set to {player.Username}.");
    }
}

public sealed class HostCommand : IServerCommand
{
    public string Name => "host";
    public string Usage => "/host";
    public string Description => "Show current host.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        source.Reply("Host: " + (ctx.Server.hostUsername ?? "(none)"));
    }
}

public sealed class BanCommand : IServerCommand
{
    public string Name => "ban";
    public string Usage => "/ban <player> [reason]";
    public string Description => "Ban a player by username.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length < 1)
        {
            source.Reply("Usage: /ban <player> [reason]");
            return;
        }

        var username = args[0];
        ctx.AdminStore.bans.Add(username);
        ctx.AdminStore.Save(ctx.AdminFile);
        ctx.AdminStore.ApplyTo(ctx.Server);

        var player = ctx.Server.GetPlayer(username);
        if (player != null && !player.IsHost)
            player.Disconnect(MpDisconnectReason.Kick);

        source.Reply($"Banned {username}.");
    }
}

public sealed class PardonCommand : IServerCommand
{
    public string Name => "pardon";
    public string Usage => "/pardon <player>";
    public string Description => "Unban a player.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length < 1)
        {
            source.Reply("Usage: /pardon <player>");
            return;
        }

        var username = args[0];
        if (!ctx.AdminStore.bans.Remove(username))
        {
            source.Reply("Player not banned.");
            return;
        }

        ctx.AdminStore.Save(ctx.AdminFile);
        ctx.AdminStore.ApplyTo(ctx.Server);
        source.Reply($"Unbanned {username}.");
    }
}

public sealed class BanListCommand : IServerCommand
{
    public string Name => "banlist";
    public string Usage => "/banlist";
    public string Description => "Show banned users.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        var list = ctx.AdminStore.bans.OrderBy(x => x).ToList();
        source.Reply(list.Count == 0 ? "No banned users." : "Banned: " + string.Join(", ", list));
    }
}

public sealed class WhitelistCommand : IServerCommand
{
    public string Name => "whitelist";
    public string Usage => "/whitelist <on|off|list|add|remove> [player]";
    public string Description => "Manage the whitelist.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length == 0)
        {
            source.Reply("Usage: /whitelist <on|off|list|add|remove> [player]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        switch (sub)
        {
            case "on":
                ctx.AdminStore.whitelistEnabled = true;
                break;
            case "off":
                ctx.AdminStore.whitelistEnabled = false;
                break;
            case "list":
                var list = ctx.AdminStore.whitelist.OrderBy(x => x).ToList();
                source.Reply(list.Count == 0 ? "Whitelist is empty." : "Whitelisted: " + string.Join(", ", list));
                return;
            case "add":
                if (args.Length < 2)
                {
                    source.Reply("Usage: /whitelist add <player>");
                    return;
                }
                ctx.AdminStore.whitelist.Add(args[1]);
                break;
            case "remove":
                if (args.Length < 2)
                {
                    source.Reply("Usage: /whitelist remove <player>");
                    return;
                }
                ctx.AdminStore.whitelist.Remove(args[1]);
                break;
            default:
                source.Reply("Usage: /whitelist <on|off|list|add|remove> [player]");
                return;
        }

        ctx.AdminStore.Save(ctx.AdminFile);
        ctx.AdminStore.ApplyTo(ctx.Server);
        source.Reply($"Whitelist {sub} ok.");
    }
}

public sealed class PauseCommand : IServerCommand
{
    public string Name => "pause";
    public string Usage => "/pause";
    public string Description => "Pause the game.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        ctx.Server.commands.PauseAll();
    }
}

public sealed class TimeVoteCommand : IServerCommand
{
    public string Name => "time";
    public string Usage => "/time <paused|normal|fast|superfast|ultrafast>";
    public string Description => "Set time via a global time vote.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length < 1)
        {
            source.Reply("Usage: /time <paused|normal|fast|superfast|ultrafast>");
            return;
        }

        if (!TryParseVote(args[0], out var vote))
        {
            source.Reply("Invalid time value.");
            return;
        }

        var data = ByteWriter.GetBytes(vote, ScheduledCommand.Global);
        ctx.Server.commands.Send(CommandType.TimeSpeedVote, ScheduledCommand.NoFaction, ScheduledCommand.Global, data);
    }

    private static bool TryParseVote(string input, out TimeVote vote)
    {
        switch (input.ToLowerInvariant())
        {
            case "paused":
            case "pause":
                vote = TimeVote.Paused;
                return true;
            case "normal":
                vote = TimeVote.Normal;
                return true;
            case "fast":
                vote = TimeVote.Fast;
                return true;
            case "superfast":
            case "super":
                vote = TimeVote.Superfast;
                return true;
            case "ultrafast":
            case "ultra":
                vote = TimeVote.Ultrafast;
                return true;
            default:
                vote = TimeVote.Normal;
                return false;
        }
    }
}

public sealed class SaveSettingsCommand : IServerCommand
{
    public string Name => "saveconfig";
    public string Usage => "/saveconfig";
    public string Description => "Save server settings to settings.toml.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        TomlSettings.Save(ctx.Settings, ctx.SettingsFile);
        source.Reply("Settings saved.");
    }
}

public sealed class SaveServerStateCommand : IServerCommand
{
    public string Name => "save";
    public string Usage => "/save [path]";
    public string Description => "Save current server world data to a zip.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (ctx.Server.worldData.savedGame == null)
        {
            source.Reply("No world data to save yet.");
            return;
        }

        var path = args.Length > 0 ? args[0] : "save.zip";
        SaveZip(ctx.Server, path);
        source.Reply($"Saved to {path}");
    }

    private static void SaveZip(MultiplayerServer server, string path)
    {
        var info = server.replayInfo;
        if (info == null)
            throw new InvalidOperationException("Replay info not available for saving.");

        info.name = server.settings.gameName ?? info.name;
        info.playerFaction = server.worldData.hostFactionId;
        info.spectatorFaction = server.worldData.spectatorFactionId;

        if (info.sections.Count == 0)
            info.sections.Add(new ReplaySection(0, server.gameTimer));
        else
            info.sections[0].end = server.gameTimer;

        if (File.Exists(path))
            File.Delete(path);

        using var zip = MpZipFile.Open(path, ZipArchiveMode.Create);

        foreach (var (mapId, mapData) in server.worldData.mapData)
            zip.AddEntry($"maps/000_{mapId}_save", Decompress(mapData));

        foreach (var (mapId, mapCmdsRaw) in server.worldData.mapCmds)
        {
            if (mapId < 0) continue;
            var cmds = mapCmdsRaw.Select(b => ScheduledCommand.Deserialize(new ByteReader(b))).ToList();
            zip.AddEntry($"maps/000_{mapId}_cmds", ScheduledCommand.SerializeCmds(cmds));
        }

        if (server.worldData.mapCmds.TryGetValue(ScheduledCommand.Global, out var worldCmdsRaw))
        {
            var cmds = worldCmdsRaw.Select(b => ScheduledCommand.Deserialize(new ByteReader(b))).ToList();
            zip.AddEntry("world/000_cmds", ScheduledCommand.SerializeCmds(cmds));
        }

        zip.AddEntry("world/000_save", Decompress(server.worldData.savedGame));
        zip.AddEntry("info", ReplayInfo.Write(info));
    }

    private static byte[] Decompress(byte[] input)
    {
        using var source = new MemoryStream(input);
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var result = new MemoryStream();
        gzip.CopyTo(result);
        return result.ToArray();
    }
}

public sealed class LoadSaveCommand : IServerCommand
{
    public string Name => "load";
    public string Usage => "/load <path>";
    public string Description => "Load a save zip and start the server world.";

    public void Execute(ServerCommandContext ctx, IServerCommandSource source, string[] args)
    {
        if (args.Length < 1)
        {
            source.Reply("Usage: /load <path>");
            return;
        }

        var path = args[0];
        if (!ServerSaveLoader.TryLoad(ctx.Server, path))
        {
            source.Reply($"Failed to load save from '{path}'.");
            return;
        }

        source.Reply($"Loaded save from '{path}'.");
    }
}
