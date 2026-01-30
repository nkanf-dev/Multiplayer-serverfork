using System.IO.Compression;
using System.Net;
using Multiplayer.Common;
using Multiplayer.Common.Util;
using Server;

ServerLog.detailEnabled = true;

const string settingsFile = "settings.toml";
const string adminFile = "admin.json";
const string saveFile = "save.zip";

var settings = new ServerSettings
{
    direct = true,
    lan = false
};

if (File.Exists(settingsFile))
    settings = TomlSettings.Load(settingsFile);
else
    TomlSettings.Save(settings, settingsFile); // Save default settings

if (settings.steam) ServerLog.Error("Steam is not supported in standalone server.");
if (settings.arbiter) ServerLog.Error("Arbiter is not supported in standalone server.");

var server = MultiplayerServer.instance = new MultiplayerServer(settings)
{
    running = true,
};

var consoleSource = new ConsoleCommandSource();
var adminStore = ServerAdminStore.Load(adminFile);
adminStore.ApplyTo(server);
var commandManager = new ServerCommandManager(server, settings, adminStore, settingsFile, adminFile);

if (!ServerSaveLoader.TryLoad(server, saveFile))
    ServerLog.Log($"No save found at '{saveFile}'. Server is waiting. Use /load <path> to load a save.");

if (settings.direct) {
    var badEndpoint = settings.TryParseEndpoints(out var endpoints);
    if (badEndpoint != null)
    {
        ServerLog.Error($"Failed to parse endpoint: {badEndpoint}");
        return;
    }

    if (!LiteNetManager.Create(server, endpoints, out var liteNet))
    {
        ServerLog.Error("Failed to start net manager");
        return;
    }
    server.netManagers.Add(liteNet);
}

if (settings.lan)
{
    if (!IPAddress.TryParse(settings.lanAddress, out var ipAddr))
    {
        ServerLog.Error($"Failed to parse lan address: {settings.lanAddress}");
        return;
    }

    var lan = LiteNetLanManager.Create(server, ipAddr);
    if (lan == null)
    {
        ServerLog.Error("Failed to start lan manager");
        return;
    }
    server.netManagers.Add(lan);
}

new Thread(server.Run) { Name = "Server thread" }.Start();

while (true)
{
    var cmd = Console.ReadLine();
    if (cmd != null)
        commandManager.ExecuteLine(consoleSource, cmd);

    if (!server.running)
        break;
}

