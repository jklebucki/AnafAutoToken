using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnafAutoToken.Manager.Configuration;

/// <summary>
/// Thin wrapper over the raw <c>appsettings.json</c> tree. The file is edited in place as a
/// <see cref="JsonNode"/> so that keys the manager does not know about survive a round trip.
/// </summary>
internal sealed class AppSettingsDocument
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private AppSettingsDocument(JsonObject root)
    {
        Root = root;
    }

    public JsonObject Root { get; private set; }

    public static AppSettingsDocument CreateEmpty() => new([]);

    public static AppSettingsDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        return Parse(json);
    }

    public static AppSettingsDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CreateEmpty();
        }

        var node = JsonNode.Parse(json, nodeOptions: null, ParseOptions)
            ?? throw new InvalidDataException("Plik nie zawiera obiektu JSON.");

        if (node is not JsonObject root)
        {
            throw new InvalidDataException("Główny element pliku musi być obiektem JSON.");
        }

        return new AppSettingsDocument(root);
    }

    public string ToJson() => Root.ToJsonString(WriteOptions);

    /// <summary>Saves the file, first copying the previous content next to it.</summary>
    public string? Save(string path, bool createBackup)
    {
        string? backupPath = null;

        if (createBackup && File.Exists(path))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
            var name = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);
            backupPath = Path.Combine(directory, $"{name}.bak_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
            File.Copy(path, backupPath, overwrite: false);
        }

        File.WriteAllText(path, ToJson());
        return backupPath;
    }

    public string? GetString(params string[] path) => FindValue(path)?.GetValue<string?>();

    public int? GetInt(params string[] path)
    {
        var value = FindValue(path);

        if (value is null)
        {
            return null;
        }

        return value.TryGetValue(out int number) ? number : null;
    }

    public bool? GetBool(params string[] path)
    {
        var value = FindValue(path);

        if (value is null)
        {
            return null;
        }

        return value.TryGetValue(out bool flag) ? flag : null;
    }

    public IReadOnlyList<string> GetStringArray(params string[] path)
    {
        if (FindNode(path) is not JsonArray array)
        {
            return [];
        }

        return array
            .Select(item => item?.GetValue<string?>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToList();
    }

    public void SetString(string? value, params string[] path) =>
        SetNode(path, string.IsNullOrEmpty(value) ? null : JsonValue.Create(value));

    public void SetInt(int? value, params string[] path) =>
        SetNode(path, value.HasValue ? JsonValue.Create(value.Value) : null);

    public void SetBool(bool? value, params string[] path) =>
        SetNode(path, value.HasValue ? JsonValue.Create(value.Value) : null);

    public void SetStringArray(IEnumerable<string> values, params string[] path)
    {
        var array = new JsonArray();

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            array.Add(JsonValue.Create(value.Trim()));
        }

        SetNode(path, array.Count == 0 ? null : array);
    }

    private JsonValue? FindValue(string[] path) => FindNode(path) as JsonValue;

    private JsonNode? FindNode(string[] path)
    {
        JsonNode? current = Root;

        foreach (var segment in path)
        {
            if (current is not JsonObject currentObject || !currentObject.TryGetPropertyValue(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private void SetNode(string[] path, JsonNode? value)
    {
        if (path.Length == 0)
        {
            throw new ArgumentException("Ścieżka klucza nie może być pusta.", nameof(path));
        }

        var parent = Root;

        for (var i = 0; i < path.Length - 1; i++)
        {
            if (parent.TryGetPropertyValue(path[i], out var existing) && existing is JsonObject existingObject)
            {
                parent = existingObject;
                continue;
            }

            // A missing (or non-object) intermediate section is replaced by a fresh one so
            // that a partially filled settings file can still be completed from the UI.
            var created = new JsonObject();
            parent[path[i]] = created;
            parent = created;
        }

        var leaf = path[^1];

        if (value is null)
        {
            parent.Remove(leaf);
            return;
        }

        parent[leaf] = value;
    }
}
