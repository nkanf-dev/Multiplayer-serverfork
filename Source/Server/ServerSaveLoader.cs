using System;
using System.IO;
using System.IO.Compression;
using Multiplayer.Common;
using Multiplayer.Common.Util;

namespace Server;

public static class ServerSaveLoader
{
    public static bool TryLoad(MultiplayerServer server, string path)
    {
        if (!File.Exists(path))
            return false;

        using var zip = ZipFile.OpenRead(path);

        var replayInfo = ReplayInfo.Read(zip.GetBytes("info"));
        ServerLog.Detail($"Loading {path} saved in RW {replayInfo.rwVersion} with {replayInfo.modNames.Count} mods");
        server.replayInfo = replayInfo;

        server.settings.gameName = replayInfo.name;
        server.worldData.hostFactionId = replayInfo.playerFaction;
        var spectatorFaction = replayInfo.spectatorFaction;
        if (server.settings.multifaction && spectatorFaction == 0)
            ServerLog.Error("Multifaction is enabled but the save doesn't contain spectator faction id.");
        server.worldData.spectatorFactionId = spectatorFaction;

        if (replayInfo.sections.Count > 0)
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

        server.worldData.savedGame = Compress(zip.GetBytes("world/000_save"));

        foreach (var entry in zip.GetEntries("maps/*_cmds"))
        {
            var parts = entry.FullName.Split('_');
            if (parts.Length == 3)
            {
                int mapNumber = int.Parse(parts[1]);
                server.worldData.mapCmds[mapNumber] = ScheduledCommand.DeserializeCmds(zip.GetBytes(entry.FullName))
                    .Select(ScheduledCommand.Serialize).ToList();
            }
        }

        foreach (var entry in zip.GetEntries("maps/*_save"))
        {
            var parts = entry.FullName.Split('_');
            if (parts.Length == 3)
            {
                int mapNumber = int.Parse(parts[1]);
                server.worldData.mapData[mapNumber] = Compress(zip.GetBytes(entry.FullName));
            }
        }

        server.worldData.mapCmds[-1] = ScheduledCommand.DeserializeCmds(zip.GetBytes("world/000_cmds"))
            .Select(ScheduledCommand.Serialize).ToList();
        server.worldData.sessionData = Array.Empty<byte>();

        return true;
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
