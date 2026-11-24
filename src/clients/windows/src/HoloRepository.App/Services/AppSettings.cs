using System.Text.Json;
using System.Text.Json.Serialization;
using HoloRepository.Core.Daemon;

namespace HoloRepository.App.Services;

/// <summary>User preferences, stored as JSON under %LOCALAPPDATA% so they work packaged and unpackaged.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoloRepository", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>"Light", "Dark" or "Default" (follow Windows).</summary>
    public string Theme { get; set; } = "Dark";

    public ComputeBackend Backend { get; set; } = ComputeBackend.Cpu;

    public string DaemonEndpoint { get; set; } = "http://localhost:50051";

    public string DisplayName { get; set; } = "Dr Local User";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Corrupt settings should never stop the app from starting.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
