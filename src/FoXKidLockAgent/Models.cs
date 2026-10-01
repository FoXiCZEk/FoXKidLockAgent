using System.IO;

namespace FoXKidLockAgent;

public sealed class AgentSettings
{
    public string ServerUrl { get; set; } = "https://example.invalid";
    public string OfflinePinHash { get; set; } = "";
    public static AgentSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "agentsettings.json");
        if (!File.Exists(path)) return new AgentSettings();
        return System.Text.Json.JsonSerializer.Deserialize<AgentSettings>(File.ReadAllText(path)) ?? new AgentSettings();
    }
}
public sealed class AgentHeartbeatRequest
{
    public string Hostname { get; init; } = Environment.MachineName;
    public string Os { get; init; } = Environment.OSVersion.VersionString;
    public string Version { get; init; } = "3.0.0-native";
    public int KilledCount { get; init; }
    public string? KilledProcess { get; init; }
}
public sealed class AgentHeartbeatResponse
{
    public bool Success { get; init; }
    public string Status { get; init; } = "locked_studying";
    public List<string> BlockedProcesses { get; init; } = [];
    public WebFilter? WebFilter { get; init; }
}
public sealed class WebFilter { public bool Enabled { get; init; } public List<string> Domains { get; init; } = []; }
