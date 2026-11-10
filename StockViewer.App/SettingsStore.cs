using System.Text;
using System.Text.Json;
using StockViewer.Core;

namespace StockViewer.Desktop;

public sealed class SettingsStore
{
    private readonly string directory;
    public SettingsStore(string? directory = null, string? legacyDirectory = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this.directory = directory ?? Path.Combine(appData, "StockViewer");
        var legacy = legacyDirectory ?? (directory is null ? Path.Combine(appData, "StockViewer_avalonia") : null);
        if (legacy is null) return;
        // Preserve existing StockViewer preferences; import only the two safe,
        // missing settings files from the earlier separately named Avalonia app.
        foreach (var name in new[] { "display-name.txt", "stocks.json" })
        {
            try
            {
                var source = Path.Combine(legacy, name); var target = Path.Combine(this.directory, name);
                if (!File.Exists(source) || File.Exists(target)) continue;
                Directory.CreateDirectory(this.directory); File.Copy(source, target, false);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
    public bool LoadDisplayName()
    {
        try { var path = Path.Combine(directory, "display-name.txt"); return !File.Exists(path) || File.ReadAllText(path) != "false"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }
    public void SaveDisplayName(bool enabled) => AtomicWrite("display-name.txt", Encoding.UTF8.GetBytes(enabled ? "true" : "false"));
    public bool LoadDarkMode()
    {
        try { return File.ReadAllText(Path.Combine(directory, "theme.txt")) == "dark"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
    public void SaveDarkMode(bool enabled) => AtomicWrite("theme.txt", Encoding.UTF8.GetBytes(enabled ? "dark" : "light"));
    public IReadOnlyList<Stock> LoadCatalog()
    {
        try { return JsonSerializer.Deserialize<Stock[]>(File.ReadAllText(Path.Combine(directory, "stocks.json"))) ?? []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }
    public void SaveCatalog(IReadOnlyList<Stock> stocks) => AtomicWrite("stocks.json", JsonSerializer.SerializeToUtf8Bytes(stocks));
    public PopupLayout? LoadPopupLayout()
    {
        try
        {
            var path = Path.Combine(directory, "popup-layout.json");
            if (new FileInfo(path).Length > 512_000) return null;
            var layout = JsonSerializer.Deserialize<PopupLayout>(File.ReadAllText(path));
            return layout is { IsValid: true } ? layout : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }
    public void SavePopupLayout(PopupLayout layout)
    {
        if (!layout.IsValid) throw new ArgumentException("저장할 가격창 배치를 확인해 주세요.", nameof(layout));
        AtomicWrite("popup-layout.json", JsonSerializer.SerializeToUtf8Bytes(layout));
    }
    private void AtomicWrite(string filename, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
