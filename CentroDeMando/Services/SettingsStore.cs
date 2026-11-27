using System.Text.Json;
using System.Text.Json.Nodes;

namespace CentroDeMando.Services;

/// <summary>Ajustes persistentes en %APPDATA%\CentroDeMando\settings.json (un objeto JSON libre).</summary>
public sealed class SettingsStore
{
    readonly string _file;
    readonly object _lock = new();
    JsonObject _data;

    public SettingsStore()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CentroDeMando");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "settings.json");
        _data = Load();
        Defaults();
    }

    JsonObject Load()
    {
        try
        {
            if (File.Exists(_file) && JsonNode.Parse(File.ReadAllText(_file)) is JsonObject o) return o;
        }
        catch { }
        return new JsonObject();
    }

    void Defaults()
    {
        // Cuentas atrás: lista libre { name, date }. Versiones anteriores guardaban solo "gtaDate".
        if (_data["countdowns"] is not JsonArray)
        {
            var date = _data["gtaDate"]?.GetValue<string>() ?? "2026-11-19T00:00:00";
            _data["countdowns"] = new JsonArray(new JsonObject { ["name"] = "GTA VI", ["date"] = date });
            _data["countdownIndex"] = 0;
        }
        _data.Remove("gtaDate");
        _data["countdownIndex"] ??= 0;
        // Sin ubicación por defecto: la interfaz pide al usuario que elija la suya.
        _data["cleanList"] ??= new JsonArray();
        _data["ocAccepted"] ??= false;
        _data["fans"] ??= new JsonArray();
    }

    void Save()
    {
        try { File.WriteAllText(_file, _data.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    /// <summary>Copia de un ajuste concreto (o null si no existe).</summary>
    public JsonNode? Get(string name)
    {
        lock (_lock) return _data[name] is JsonNode n ? JsonNode.Parse(n.ToJsonString()) : null;
    }

    public JsonNode Snapshot()
    {
        lock (_lock) return JsonNode.Parse(_data.ToJsonString())!;
    }

    public JsonNode Patch(JsonElement patch)
    {
        lock (_lock)
        {
            if (patch.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in patch.EnumerateObject())
                {
                    if (p.NameEquals("fans")) continue;                 // los ventiladores se guardan por su propio comando
                    _data[p.Name] = JsonNode.Parse(p.Value.GetRawText());
                }
                Save();
            }
            return JsonNode.Parse(_data.ToJsonString())!;
        }
    }

    public List<FanConfig> GetFans()
    {
        lock (_lock)
        {
            try { return _data["fans"].Deserialize<List<FanConfig>>(Bridge.Json) ?? new(); } catch { return new(); }
        }
    }

    public void SetFans(List<FanConfig> fans)
    {
        lock (_lock)
        {
            _data["fans"] = JsonSerializer.SerializeToNode(fans, Bridge.Json);
            Save();
        }
    }
}
