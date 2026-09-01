using System.Text.Json;
using System.Text.Json.Serialization;

namespace AttendanceAgent;

public sealed record AgentConfig
{
    [JsonPropertyName("cms_base_url")] public string CmsBaseUrl { get; init; } = "";
    [JsonPropertyName("db_path")] public string DbPath { get; init; } = "";
    [JsonPropertyName("capacity_warning_pct")] public int CapacityWarningPct { get; init; } = 90;
    [JsonPropertyName("machines")] public List<MachineConfigJson> MachineEntries { get; init; } = [];
    [JsonIgnore] public IReadOnlyList<MachineConfig> Machines => MachineEntries.Select(x => new MachineConfig(x.Name, x.Ip, x.Port, x.SerialNumber)).ToList();

    public static AgentConfig Load(string path = "config.json")
    {
        if (!File.Exists(path)) throw new ConfigException($"Error: Config file not found: {path}");
        AgentConfig? config;
        try { config = JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path)); }
        catch (JsonException ex) { throw new ConfigException($"Error: Invalid JSON in {path}: {ex.Message}"); }
        if (config is null) throw new ConfigException($"Error: Invalid JSON in {path}: empty document");
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(config.CmsBaseUrl)) missing.Add("cms_base_url");
        if (string.IsNullOrWhiteSpace(config.DbPath)) missing.Add("db_path");
        if (config.MachineEntries is null) missing.Add("machines");
        if (missing.Count > 0) throw new ConfigException($"Error: Missing required config keys: {string.Join(", ", missing)}");
        var entries = config.MachineEntries!;
        if (entries.Count == 0) throw new ConfigException("Error: 'machines' must be a non-empty array");
        for (var i = 0; i < entries.Count; i++)
        {
            var machine = entries[i]; var fields = new List<string>();
            if (string.IsNullOrWhiteSpace(machine.Name)) fields.Add("name");
            if (string.IsNullOrWhiteSpace(machine.Ip)) fields.Add("ip");
            if (machine.Port <= 0) fields.Add("port");
            if (string.IsNullOrWhiteSpace(machine.SerialNumber)) fields.Add("serial_number");
            if (fields.Count > 0) throw new ConfigException($"Error: Machine[{i}] missing required keys: {string.Join(", ", fields)}");
        }
        return config;
    }

    public IReadOnlyList<MachineConfig> GetMachines(string? name = null)
    {
        if (name is null) return Machines;
        var machine = Machines.FirstOrDefault(x => x.Name == name);
        return machine is not null ? [machine] : throw new ConfigException($"Error: Machine '{name}' not found. Available: {string.Join(", ", Machines.Select(x => x.Name))}");
    }
}

public sealed record MachineConfigJson
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("ip")] public string Ip { get; init; } = "";
    [JsonPropertyName("port")] public int Port { get; init; }
    [JsonPropertyName("serial_number")] public string SerialNumber { get; init; } = "";
}

public sealed class ConfigException(string message) : Exception(message);
