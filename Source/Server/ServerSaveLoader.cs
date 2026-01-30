using System;
using System.IO;
using System.IO.Compression;
using Multiplayer.Common;
using Multiplayer.Common.Util;

namespace Server;

public static class ServerSaveLoader
{
    public static bool TryLoad(MultiplayerServer server, string path, out string? error)
    {
        error = null;

        if (!File.Exists(path))
        {
            error = $"File not found: {path}";
            return false;
        }

        try
        {
            using var zip = ZipFile.OpenRead(path);

            var infoEntry = zip.GetEntry("info");
            if (infoEntry == null)
            {
                error = "Missing info entry (not a multiplayer save zip).";
                return false;
            }

            ReplayInfo replayInfo;
            try
            {
                replayInfo = ReplayInfo.Read(infoEntry.GetBytes());
            }
            catch (Exception ex)
            {
                error = $"Failed to read info: {ex.Message}";
                return false;
            }

            var modCount = replayInfo.modNames?.Count ?? 0;
            ServerLog.Detail($"Loading {path} saved in RW {replayInfo.rwVersion} with {modCount} mods");
            server.replayInfo = replayInfo;

            server.settings.gameName = replayInfo.name;
            server.worldData.hostFactionId = replayInfo.playerFaction;
            var spectatorFaction = replayInfo.spectatorFaction;
            if (server.settings.multifaction && spectatorFaction == 0)
                ServerLog.Error("Multifaction is enabled but the save doesn't contain spectator faction id.");
            server.worldData.spectatorFactionId = spectatorFaction;

            if (replayInfo.sections != null && replayInfo.sections.Count > 0)
            {
                server.gameTimer = replayInfo.sections[0].start;
                server.startingTimer = replayInfo.sections[0].start;
            }
            else
            {
                server.gameTimer = 0;
                server.startingTimer = 0;
            }

            server.worldData.mapCmds.Clear();
            server.worldData.mapData.Clear();
            server.worldData.syncInfos.Clear();
            server.worldData.tmpMapCmds = null;
            server.worldData.lastJoinPointAtWorkTicks = -1;

            var worldSaveEntry = zip.GetEntry("world/000_save");
            if (worldSaveEntry == null)
            {
                error = "Missing world/000_save entry (not a valid multiplayer save zip).";
                return false;
            }
            server.worldData.savedGame = Compress(worldSaveEntry.GetBytes());

            foreach (var entry in zip.GetEntries("maps/*_cmds"))
            {
                var parts = entry.FullName.Split('_');
                if (parts.Length == 3)
                {
                    int mapNumber = int.Parse(parts[1]);
                    server.worldData.mapCmds[mapNumber] = ScheduledCommand.DeserializeCmds(entry.GetBytes())
                        .Select(ScheduledCommand.Serialize).ToList();
                }
            }

            foreach (var entry in zip.GetEntries("maps/*_save"))
            {
                var parts = entry.FullName.Split('_');
                if (parts.Length == 3)
                {
                    int mapNumber = int.Parse(parts[1]);
                    server.worldData.mapData[mapNumber] = Compress(entry.GetBytes());
                }
            }

            var worldCmdsEntry = zip.GetEntry("world/000_cmds");
            server.worldData.mapCmds[-1] = worldCmdsEntry != null
                ? ScheduledCommand.DeserializeCmds(worldCmdsEntry.GetBytes())
                    .Select(ScheduledCommand.Serialize).ToList()
                : new List<byte[]>();

            server.worldData.sessionData = Array.Empty<byte>();
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load save: {ex.Message}";
            return false;
        }
    }

    public static bool TryLoad(MultiplayerServer server, string path)
    {
        return TryLoad(server, path, out _);
    }

    private static byte[] Compress(byte[] input)
    {
        using var result = new MemoryStream();
        using (var compressionStream = new GZipStream(result, CompressionMode.Compress))
        {
            compressionStream.Write(input, 0, input.Length);
            compressionStream.Flush();
        }
        return result.ToArray();
    }
}
