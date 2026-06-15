using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LifeDeck.Studio.Models;

namespace LifeDeck.Studio.Services;

public sealed class TabletConnectionService : IDisposable
{
    private const int Port = 24680;
    private TcpClient? _client;
    private StreamWriter? _writer;
    private CancellationTokenSource? _cts;

    public bool IsConnected => _client?.Connected == true;
    public event Action<string>? StatusChanged;
    public event Action<string>? MessageReceived;
    public event Action? PageNextRequested;
    public event Action? PagePrevRequested;
    public event Action<string, int>? ButtonPressed;

    public async Task ConnectAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        StatusChanged?.Invoke("ADB vorbereiten...");
        await RunAdbAsync("forward --remove-all");
        await RunAdbAsync($"forward tcp:{Port} tcp:{Port}");

        StatusChanged?.Invoke("Verbinde mit Tablet...");
        _client = new TcpClient(AddressFamily.InterNetwork);
        await _client.ConnectAsync(IPAddress.Parse("127.0.0.1"), Port);

        var stream = _client.GetStream();
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        StatusChanged?.Invoke("Tablet verbunden");

        _ = Task.Run(() => ReadLoopAsync(stream, _cts.Token));
    }

    public async Task SendLayoutAsync(DeckProfile profile, int pageIndex)
    {
        if (_writer == null || profile.Pages.Count == 0) return;
        if (pageIndex < 0) pageIndex = 0;
        if (pageIndex >= profile.Pages.Count) pageIndex = profile.Pages.Count - 1;

        var page = profile.Pages[pageIndex];
        var buttons = new JsonArray();
        foreach (var b in page.Buttons.Take(12))
        {
            var obj = new JsonObject
            {
                ["id"] = b.Id,
                ["title"] = string.IsNullOrWhiteSpace(b.Subtitle) ? b.Title : b.Title + "\n" + b.Subtitle,
                ["color"] = string.IsNullOrWhiteSpace(b.Color) ? "#333333" : b.Color,
                ["displayMode"] = string.IsNullOrWhiteSpace(b.DisplayMode) ? "imageText" : b.DisplayMode
            };

            var iconName = await EnsureIconOnTabletAsync(b.IconPath);
            if (!string.IsNullOrWhiteSpace(iconName)) obj["icon"] = iconName;

            buttons.Add(obj);
        }

        var msg = new JsonObject
        {
            ["type"] = "layout",
            ["page"] = page.Title,
            ["pageIndex"] = pageIndex,
            ["pageCount"] = profile.Pages.Count,
            ["buttons"] = buttons
        };

        await _writer.WriteLineAsync(msg.ToJsonString());
    }


    private async Task<string?> EnsureIconOnTabletAsync(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") return null;

            var iconName = MakeSafeIconName(path);
            await RunAdbAsync("shell mkdir -p /sdcard/LifeDeck/Assets/Icons");
            await RunAdbPushAsync(path, "/sdcard/LifeDeck/Assets/Icons/" + iconName);
            return iconName;
        }
        catch
        {
            return null;
        }
    }

    private static string MakeSafeIconName(string path)
    {
        var file = Path.GetFileName(path);
        foreach (var ch in Path.GetInvalidFileNameChars()) file = file.Replace(ch, '_');
        file = file.Replace(' ', '_');
        return file;
    }

    private async Task ReadLoopAsync(NetworkStream stream, CancellationToken token)
    {
        try
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            while (!token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                MessageReceived?.Invoke(line);
                HandleIncoming(line);
            }
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke("Verbindung verloren: " + ex.Message);
        }
        finally
        {
            StatusChanged?.Invoke("Tablet nicht verbunden");
            try { _client?.Close(); } catch { }
            _client = null;
            _writer = null;
        }
    }

    private void HandleIncoming(string line)
    {
        try
        {
            var json = JsonNode.Parse(line)?.AsObject();
            var type = json?["type"]?.GetValue<string>() ?? "";
            if (type == "pageNext") PageNextRequested?.Invoke();
            else if (type == "pagePrev") PagePrevRequested?.Invoke();
            else if (type == "press")
            {
                var id = json?["button"]?.GetValue<string>() ?? "";
                var index = json?["index"]?.GetValue<int>() ?? -1;
                ButtonPressed?.Invoke(id, index);
            }
        }
        catch { }
    }

    private static string FindAdb()
    {
        var candidates = new List<string>();
        candidates.Add(@"C:\adb\adb.exe");

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
            candidates.Add(Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe"));

        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME") ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(androidHome))
            candidates.Add(Path.Combine(androidHome, "platform-tools", "adb.exe"));

        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return "adb";
    }

    private async Task RunAdbAsync(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FindAdb(),
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (p.ExitCode != 0)
            throw new InvalidOperationException("ADB Fehler: " + stderr + stdout);
    }

    private async Task RunAdbPushAsync(string source, string target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FindAdb(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("push");
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(target);

        using var p = Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (p.ExitCode != 0)
            throw new InvalidOperationException("ADB Push Fehler: " + stderr + stdout);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _client?.Close(); } catch { }
        _cts?.Dispose();
    }
}
