namespace HoloRepository.App.Services;

/// <summary>
/// Local folder served to the embedded NiiVue viewer as https://holo.niivue/. It holds the bundled NiiVue
/// files and one scene folder at a time (the exported scan, labels and meshes), so everything loads from a
/// single local origin and no data leaves the computer. Old scenes are deleted as soon as a new one is written.
/// </summary>
public static class NiiVueHost
{
    public const string HostName = "holo.niivue";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoloRepository", "niivue");

    public static string UserDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HoloRepository", "WebView2");

    public static Uri PageUri { get; } = new($"https://{HostName}/index.html");

    private static bool _leftoversRemoved;

    /// <summary>Copies the bundled NiiVue page and scripts next to the scene folders.</summary>
    public static void PrepareAssets()
    {
        Directory.CreateDirectory(Root);
        string source = Path.Combine(AppContext.BaseDirectory, "NiiVue");
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(Root, Path.GetFileName(file)), overwrite: true);
    }

    /// <summary>Creates an empty folder for a new scene and returns its path and the URL prefix NiiVue should use.</summary>
    public static (string Folder, string UrlPrefix) NewScene()
    {
        // Scenes left over from an earlier run are removed once, before the first new scene is written.
        if (!_leftoversRemoved)
        {
            DeleteScenes(keep: null);
            _leftoversRemoved = true;
        }
        string name = "scene-" + Guid.NewGuid().ToString("N")[..12];
        string folder = Path.Combine(Root, name);
        Directory.CreateDirectory(folder);
        return (folder, $"https://{HostName}/{name}/");
    }

    /// <summary>Deletes every scene folder except <paramref name="keep"/> (scan data is not left lying around).</summary>
    public static void DeleteScenes(string? keep)
    {
        if (!Directory.Exists(Root)) return;
        foreach (var dir in Directory.EnumerateDirectories(Root, "scene-*"))
        {
            if (keep is not null && string.Equals(dir, keep, StringComparison.OrdinalIgnoreCase)) continue;
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
