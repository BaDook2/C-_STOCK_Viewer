using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using StockViewer.Core;

namespace StockViewer.App;

public sealed class SettingsStore
{
    private readonly string directory;
    public SettingsStore(string? directory = null) => this.directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockViewer");
    private string KeyPath => Path.Combine(directory, "kiwoom.protected");
    public ApiCredentials? LoadCredentials()
    {
        if (!File.Exists(KeyPath)) return null;
        var plain = Protect(File.ReadAllBytes(KeyPath), false);
        try { var value = JsonSerializer.Deserialize<ApiCredentials>(plain); value?.Validate(); return value; }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void SaveCredentials(ApiCredentials credentials)
    {
        credentials.Validate();
        var plain = JsonSerializer.SerializeToUtf8Bytes(credentials);
        try { AtomicWrite(KeyPath, Protect(plain, true)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void DeleteCredentials() => File.Delete(KeyPath);
    public bool LoadDisplayName()
    { try { return !File.Exists(Path.Combine(directory, "display-name.txt")) || File.ReadAllText(Path.Combine(directory, "display-name.txt")) != "false"; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; } }
    public void SaveDisplayName(bool enabled) => AtomicWrite(Path.Combine(directory, "display-name.txt"), System.Text.Encoding.UTF8.GetBytes(enabled ? "true" : "false"));
    public IReadOnlyList<Stock> LoadCatalog()
    { try { return JsonSerializer.Deserialize<Stock[]>(File.ReadAllText(Path.Combine(directory, "stocks.json"))) ?? []; } catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; } }
    public void SaveCatalog(IReadOnlyList<Stock> stocks) => AtomicWrite(Path.Combine(directory, "stocks.json"), JsonSerializer.SerializeToUtf8Bytes(stocks));
    private void AtomicWrite(string path, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    private static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = encrypt ? CryptProtectData(ref input, "StockViewer Kiwoom", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new InvalidOperationException("이 Windows 계정에서 키를 읽거나 저장할 수 없습니다. 다시 입력해 주세요.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (var i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            { for (var i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
}
