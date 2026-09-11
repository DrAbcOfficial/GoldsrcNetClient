using System.Text.Json;

namespace GoldsrcNetClient.Tui.Services;

/// <summary>
/// Base for JSON-persisted settings files stored under the app's
/// local-application-data folder. Handles path resolution, locking,
/// corrupt-file fallback, and directory creation.
/// </summary>
/// <param name="path">Explicit file path; null resolves to the default folder.</param>
/// <param name="fileName">Default file name inside the app data folder.</param>
public abstract class JsonStore<T>(string? path, string fileName) where T : class
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _lock = new();

    /// <summary>Full path of the backing JSON file.</summary>
    protected string FilePath { get; } = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GoldsrcNetClient",
        fileName);

    /// <summary>The current value, guarded by the store lock.</summary>
    protected T Value { get; private set; } = null!;

    /// <summary>The value used when no file exists yet or it cannot be parsed.</summary>
    protected abstract T InitialValue { get; }

    /// <summary>Reads the file, falling back to <see cref="InitialValue"/> when missing or corrupt.</summary>
    public void Load()
    {
        lock (_lock)
        {
            if (!File.Exists(FilePath))
            {
                Value = InitialValue;
                return;
            }
            try
            {
                Value = JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath)) ?? InitialValue;
            }
            catch
            {
                Value = InitialValue;
            }
        }
    }

    /// <summary>Writes the current value to the file, creating the directory as needed.</summary>
    public void Save()
    {
        lock (_lock)
        {
            string? dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(Value, JsonOptions));
        }
    }

    /// <summary>Returns a reference to the current value; callers must not mutate it directly.</summary>
    protected T Read()
    {
        lock (_lock) return Value;
    }

    /// <summary>Replaces the current value under the store lock.</summary>
    protected void Assign(T value)
    {
        lock (_lock) Value = value;
    }

    /// <summary>Applies a mutation to the current value under the store lock.</summary>
    protected void Update(Action<T> mutate)
    {
        lock (_lock) mutate(Value);
    }
}
