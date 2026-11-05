using System.IO;
using System.Text.Json;

namespace HoverWallpaper;

// A saved scene: the normal wallpaper, every hover zone (image, fit, crop, screen
// area and optional hold key) and the fade settings. "Launch when I sign in" is an
// app preference rather than part of a scene, so it is not stored here.
public sealed class Preset
{
    /// <summary>Matches the settings window's zone cap; a hand-edited preset cannot exceed it.</summary>
    public const int MaxZones = 12;

    public string Name { get; set; } = "";
    public string Image1 { get; set; } = "";
    public FitMode Fit1 { get; set; } = FitMode.Fill;
    public CropRect Crop1 { get; set; } = new();
    public List<HoverZone> Zones { get; set; } = [];
    public bool FadeEnabled { get; set; } = true;
    public int FadeInMs { get; set; } = 250;
    public int FadeOutMs { get; set; } = 250;
    public DateTime SavedAt { get; set; }

    /// <summary>One-line description shown under the preset name in the list.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Summary
    {
        get
        {
            int zones = Zones?.Count ?? 0;
            string area = zones == 1 ? "1 hover zone" : $"{zones} hover zones";
            string saved = SavedAt == default ? "" : $" · saved {SavedAt:MMM d, yyyy}";
            return area + saved;
        }
    }

    /// <summary>Copies the current configuration, giving the preset its own instances.</summary>
    public static Preset Capture(string name, AppSettings s) => new()
    {
        Name = name,
        Image1 = s.Image1 ?? "",
        Fit1 = s.Fit1,
        Crop1 = Copy(s.Crop1),
        Zones = (s.Zones ?? []).Select(Copy).ToList(),
        FadeEnabled = s.FadeEnabled,
        FadeInMs = s.FadeInMs,
        FadeOutMs = s.FadeOutMs,
        SavedAt = DateTime.Now
    };

    /// <summary>
    /// Writes this preset into the live settings. Rectangles are copied, so later
    /// edits to the loaded scene never change the saved preset.
    /// </summary>
    public void ApplyTo(AppSettings s)
    {
        s.Image1 = Image1 ?? "";
        s.Fit1 = Fit1;
        s.Crop1 = Copy(Crop1);
        s.Zones = Zones is { Count: > 0 }
            ? Zones.Take(MaxZones).Select(Copy).ToList()
            : [new HoverZone()];
        s.FadeEnabled = FadeEnabled;
        s.FadeInMs = Math.Clamp(FadeInMs, 0, 2000);
        s.FadeOutMs = Math.Clamp(FadeOutMs, 0, 2000);
    }

    static CropRect Copy(CropRect? c) => new()
    {
        X = c?.X ?? 0,
        Y = c?.Y ?? 0,
        W = c?.W ?? 100,
        H = c?.H ?? 100
    };

    static HoverZone Copy(HoverZone z)
    {
        int x = Math.Clamp(z.X, 0, 100);
        int y = Math.Clamp(z.Y, 0, 100);
        return new HoverZone
        {
            Image = z.Image ?? "",
            X = x,
            Y = y,
            // A hand-edited preset cannot place a zone past the desktop edge.
            Width = Math.Clamp(z.Width, 1, Math.Max(1, 100 - x)),
            Height = Math.Clamp(z.Height, 1, Math.Max(1, 100 - y)),
            Fit = z.Fit,
            Crop = Copy(z.Crop),
            HoldKey = HoldKeys.TryParse(z.HoldKey, out var key) ? key.ToString() : ""
        };
    }
}

// Presets live in their own file next to settings.json so applying a scene never
// touches the saved library.
public static class PresetStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath =>
#if DEBUG
        // Dev-only: point a screenshot run at a temporary preset file so it never
        // touches the real library.
        Environment.GetEnvironmentVariable("HW_PRESETS") is { Length: > 0 } overridePath
            ? overridePath
            :
#endif
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HoverWallpaper", "presets.json");

    /// <summary>Preset images are copied here so a preset survives the originals being deleted.</summary>
    public static string ImageRoot => Path.Combine(
        Path.GetDirectoryName(FilePath)!, "preset-images");

    // A short hash of the name keeps two names that reduce to the same slug ("a/b" and
    // "a\b") in separate folders. It depends only on the name, so it is stable.
    public static string FolderFor(string presetName)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes((presetName ?? "").ToLowerInvariant()));
        return Path.Combine(ImageRoot, Slug(presetName) + "-" + Convert.ToHexString(hash)[..8].ToLowerInvariant());
    }

    // Guards against "..", reserved characters and names Windows cannot create.
    static string Slug(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = (name ?? "").Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        string slug = new string(chars).Trim('-', ' ', '.');
        if (slug.Length == 0) slug = "preset";
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-', ' ', '.');
        string[] reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5",
                             "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4",
                             "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];
        if (reserved.Contains(slug.ToUpperInvariant())) slug += "_";
        return slug;
    }

    /// <summary>
    /// Copies every image this preset uses into its own folder, rebuilding the paths in
    /// <paramref name="preset"/> to point there. Image 1 becomes "&lt;name&gt;_1", the
    /// first hover zone "&lt;name&gt;_2", and so on, keeping each file's extension.
    /// </summary>
    public static bool CaptureImages(Preset preset, out string? error)
    {
        error = null;
        try
        {
            string folder = FolderFor(preset.Name);
            string staging = folder + ".new";

            // Build the new copies beside the old ones first: a preset that is being
            // re-saved still points at its own previous copies, so the old folder must
            // not be deleted until everything has been copied.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            string slug = Slug(preset.Name);
            string image1 = CopyImage(preset.Image1, staging, $"{slug}_1");
            var zones = new List<string>();
            for (int i = 0; i < preset.Zones.Count; i++)
                zones.Add(CopyImage(preset.Zones[i].Image, staging, $"{slug}_{i + 2}"));

            // Old extension names are dropped here, so re-saving never leaves stale files.
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.Move(staging, folder);

            // The copies were made in staging; point the preset at their new home.
            preset.Image1 = Rebase(image1, staging, folder);
            for (int i = 0; i < preset.Zones.Count; i++)
                preset.Zones[i].Image = Rebase(zones[i], staging, folder);
            return true;
        }
        catch (Exception ex)
        {
            try { if (Directory.Exists(FolderFor(preset.Name) + ".new")) Directory.Delete(FolderFor(preset.Name) + ".new", true); } catch { }
            error = ex.Message;
            return false;
        }
    }

    // Copies one source file. The number is fixed per slot, so re-saving a preset
    // always yields the same layout and replaces the old copies.
    static string CopyImage(string source, string folder, string baseName)
    {
        string path = (source ?? "").Trim('"', ' ');
        if (!File.Exists(path)) return "";

        string ext = Path.GetExtension(path);
        if (ext.Length > 10) ext = ""; // long or odd extensions are not worth keeping
        string target = Path.Combine(folder, baseName + ext);
        File.Copy(path, target, overwrite: true);
        return target;
    }

    static string Rebase(string path, string fromFolder, string toFolder) =>
        string.IsNullOrEmpty(path) ? "" : Path.Combine(toFolder, Path.GetFileName(path));

    /// <summary>Removes a preset's copied images once no other preset uses that folder.</summary>
    public static void DeleteImages(string presetName, IEnumerable<Preset> remaining)
    {
        string folder = FolderFor(presetName);
        if (!Directory.Exists(folder)) return;
        // Two names can share a slug; only delete when nothing else maps to it.
        if (remaining.Any(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase)))
            return;
        try { Directory.Delete(folder, recursive: true); } catch { }
    }

    public static List<Preset> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            var presets = (JsonSerializer.Deserialize<List<Preset?>>(File.ReadAllText(FilePath)) ?? [])
                .OfType<Preset>().ToList();
            foreach (var preset in presets)
            {
                preset.Name = string.IsNullOrWhiteSpace(preset.Name) ? "Preset" : preset.Name;
                preset.Image1 ??= "";
                preset.Crop1 ??= new();
                // A hand-edited file cannot exceed the zone cap.
                preset.Zones = (preset.Zones ?? []).OfType<HoverZone>().Take(Preset.MaxZones).ToList();
                foreach (var zone in preset.Zones)
                {
                    zone.Image ??= "";
                    zone.Crop ??= new();
                    zone.HoldKey ??= "";
                }
            }
            return presets;
        }
        catch { return []; }
    }

    /// <summary>Writes through a temporary file so an interrupted save keeps the old list.</summary>
    public static bool Save(List<Preset> presets, out string? error)
    {
        error = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(presets, JsonOptions));
            File.Move(temp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
