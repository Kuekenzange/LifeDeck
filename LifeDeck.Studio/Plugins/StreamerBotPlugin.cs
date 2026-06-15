using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LifeDeck.Studio.Plugins;

public class StreamerBotPlugin : IActionPlugin, IEventStateProvider
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private bool _connected;
    private string _lastError = "";
    private string _lastAction = "";

    public string Id => "streamerbot";
    public string DisplayName => "Streamer.bot";

    public string Host { get; set; } = "127.0.0.1";
    public string Port { get; set; } = "8080";
    public string Endpoint { get; set; } = "/";
    public string Password { get; set; } = "";
    public string DefaultArgsJson { get; set; } = "{}";

    public IReadOnlyList<PluginSettingDefinition> Settings { get; } = new[]
    {
        new PluginSettingDefinition { Key = "host", DisplayName = "Host", Hint = "Standard: 127.0.0.1", SuggestedValues = new[] { "127.0.0.1", "localhost" } },
        new PluginSettingDefinition { Key = "port", DisplayName = "Port", Hint = "Standard-WebSocket-Port von Streamer.bot ist meist 8080.", SuggestedValues = new[] { "8080", "8081" } },
        new PluginSettingDefinition { Key = "endpoint", DisplayName = "Endpoint", Hint = "Meist /", SuggestedValues = new[] { "/" } },
        new PluginSettingDefinition { Key = "password", DisplayName = "Passwort", Hint = "Nur nötig, wenn im Streamer.bot WebSocket Server Auth aktiviert ist.", Type = "password" },
        new PluginSettingDefinition { Key = "defaultArgs", DisplayName = "Standard-Argumente JSON", Hint = "Optional, z. B. {\"source\":\"LifeDeck\"}", SuggestedValues = new[] { "{}" } }
    };

    public IReadOnlyList<PluginActionDefinition> Actions { get; } = new[]
    {
        new PluginActionDefinition
        {
            Id = "streamerbot.connect",
            DisplayName = "Verbinden",
            Hint = "Verbindet LifeDeck mit dem Streamer.bot WebSocket Server."
        },
        new PluginActionDefinition
        {
            Id = "streamerbot.disconnect",
            DisplayName = "Trennen",
            Hint = "Trennt die WebSocket-Verbindung."
        },
        new PluginActionDefinition
        {
            Id = "streamerbot.doActionName",
            DisplayName = "Action ausführen (Name)",
            Hint = "Führt eine Streamer.bot Action anhand ihres Namens aus.",
            SuggestedValues = new[] { "Benutzerdefiniert" }
        },
        new PluginActionDefinition
        {
            Id = "streamerbot.doActionId",
            DisplayName = "Action ausführen (ID)",
            Hint = "Führt eine Streamer.bot Action anhand ihrer GUID aus.",
            SuggestedValues = new[] { "Benutzerdefiniert" }
        },
        new PluginActionDefinition
        {
            Id = "streamerbot.doActionRaw",
            DisplayName = "Action ausführen (JSON)",
            Hint = "Erwartet JSON, z. B. {\"action\":{\"name\":\"Meine Action\"},\"args\":{\"key\":\"value\"}}",
            SuggestedValues = new[] { "Benutzerdefiniert" }
        }
    };

    public IReadOnlyList<PluginEventDefinition> Events { get; } = new[]
    {
        new PluginEventDefinition { Id = "streamerbot.connected", DisplayName = "Verbunden", ValueHint = "true/false", SuggestedValues = new[] { "true", "false" } },
        new PluginEventDefinition { Id = "streamerbot.lastAction", DisplayName = "Letzte Action", ValueHint = "Name/ID", SuggestedValues = Array.Empty<string>() },
        new PluginEventDefinition { Id = "streamerbot.lastError", DisplayName = "Letzter Fehler", ValueHint = "Text", SuggestedValues = Array.Empty<string>() }
    };

    public string GetSettingValue(string key) => key switch
    {
        "host" => Host,
        "port" => Port,
        "endpoint" => Endpoint,
        "password" => Password,
        "defaultArgs" => DefaultArgsJson,
        _ => ""
    };

    public void SetSettingValue(string key, string value)
    {
        switch (key)
        {
            case "host": Host = value; break;
            case "port": Port = value; break;
            case "endpoint": Endpoint = string.IsNullOrWhiteSpace(value) ? "/" : value; break;
            case "password": Password = value; break;
            case "defaultArgs": DefaultArgsJson = string.IsNullOrWhiteSpace(value) ? "{}" : value; break;
        }
    }

    public async Task ExecuteAsync(string action, string value)
    {
        try
        {
            _lastError = "";
            switch (action)
            {
                case "streamerbot.connect":
                case "Verbinden":
                    await ConnectAsync();
                    break;
                case "streamerbot.disconnect":
                case "Trennen":
                    await DisconnectAsync();
                    break;
                case "streamerbot.doActionName":
                case "Action ausführen (Name)":
                    await SendWithReconnectAsync(() => SendDoActionAsync(new { name = value }, DefaultArgsJson));
                    _lastAction = value;
                    break;
                case "streamerbot.doActionId":
                case "Action ausführen (ID)":
                    await SendWithReconnectAsync(() => SendDoActionAsync(new { id = value }, DefaultArgsJson));
                    _lastAction = value;
                    break;
                case "streamerbot.doActionRaw":
                case "Action ausführen (JSON)":
                    await SendWithReconnectAsync(() => SendRawActionAsync(value));
                    _lastAction = "raw";
                    break;
            }
        }
        catch (Exception ex)
        {
            // Streamer.bot sometimes closes the WebSocket without a clean close handshake.
            // Do not show a disruptive popup in Studio; expose the error via plugin events instead.
            _lastError = ex.Message;
            MarkDisconnected();
        }
    }

    private async Task SendWithReconnectAsync(Func<Task> sendOperation)
    {
        try
        {
            await EnsureConnectedAsync();
            await sendOperation();
        }
        catch (Exception ex) when (IsConnectionException(ex))
        {
            _lastError = ex.Message;
            MarkDisconnected();

            // Short retry: if Streamer.bot closed the socket after the previous request,
            // reconnect once and send the action again without bothering the user.
            await Task.Delay(750);
            await EnsureConnectedAsync();
            await sendOperation();
        }
    }

    private static bool IsConnectionException(Exception ex)
    {
        return ex is WebSocketException
            || ex is IOException
            || ex is OperationCanceledException
            || ex is InvalidOperationException && ex.Message.Contains("WebSocket", StringComparison.OrdinalIgnoreCase)
            || ex.InnerException != null && IsConnectionException(ex.InnerException);
    }

    private async Task EnsureConnectedAsync()
    {
        if (_socket == null || _socket.State != WebSocketState.Open)
            await ConnectAsync();
    }

    private async Task ConnectAsync()
    {
        await DisconnectAsync();
        _cts = new CancellationTokenSource();
        _socket = new ClientWebSocket();

        var endpoint = string.IsNullOrWhiteSpace(Endpoint) ? "/" : Endpoint;
        if (!endpoint.StartsWith("/")) endpoint = "/" + endpoint;
        var uri = new Uri($"ws://{Host}:{Port}{endpoint}");
        await _socket.ConnectAsync(uri, _cts.Token);

        // Streamer.bot sends a Hello packet immediately after connecting.
        // If authentication is enabled, Hello contains salt/challenge and we must
        // send an Authenticate request before any DoAction request.
        var helloJson = await ReceiveTextAsync(TimeSpan.FromSeconds(5));
        if (!string.IsNullOrWhiteSpace(helloJson))
        {
            using var helloDoc = JsonDocument.Parse(helloJson);
            var root = helloDoc.RootElement;
            if (root.TryGetProperty("authentication", out var auth))
            {
                if (string.IsNullOrWhiteSpace(Password))
                    throw new InvalidOperationException("Streamer.bot verlangt Authentifizierung, aber im Plugin ist kein Passwort gesetzt.");

                var salt = auth.GetProperty("salt").GetString() ?? "";
                var challenge = auth.GetProperty("challenge").GetString() ?? "";
                var authentication = BuildAuthenticationString(Password, salt, challenge);
                var authPayload = new Dictionary<string, object?>
                {
                    ["request"] = "Authenticate",
                    ["id"] = Guid.NewGuid().ToString("N"),
                    ["authentication"] = authentication
                };
                await SendJsonAsync(authPayload);

                var authResponse = await ReceiveTextAsync(TimeSpan.FromSeconds(5));
                if (!string.IsNullOrWhiteSpace(authResponse))
                {
                    using var authDoc = JsonDocument.Parse(authResponse);
                    var authRoot = authDoc.RootElement;
                    if (authRoot.TryGetProperty("status", out var status) && status.GetString() == "error")
                        throw new InvalidOperationException("Streamer.bot Auth fehlgeschlagen: " + authResponse);
                }
            }
        }

        _connected = true;
    }

    private void MarkDisconnected()
    {
        _connected = false;
        try { _socket?.Abort(); } catch { }
        try { _socket?.Dispose(); } catch { }
        _socket = null;
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }

    private async Task DisconnectAsync()
    {
        try
        {
            if (_socket != null && _socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "LifeDeck disconnect", CancellationToken.None);
        }
        catch { }
        _socket?.Dispose();
        _socket = null;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _connected = false;
    }

    private async Task SendDoActionAsync(object actionObject, string argsJson)
    {
        object argsObj = new Dictionary<string, object>();
        try
        {
            if (!string.IsNullOrWhiteSpace(argsJson))
                argsObj = JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson) ?? new Dictionary<string, object>();
        }
        catch
        {
            argsObj = new Dictionary<string, object>();
        }

        var payload = new Dictionary<string, object?>
        {
            ["request"] = "DoAction",
            ["id"] = Guid.NewGuid().ToString("N"),
            ["action"] = actionObject,
            ["args"] = argsObj
        };
        await SendJsonAsync(payload);
    }

    private async Task SendRawActionAsync(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return;
        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement.Clone();
        var payload = new Dictionary<string, object?>
        {
            ["request"] = "DoAction",
            ["id"] = Guid.NewGuid().ToString("N")
        };

        if (root.TryGetProperty("action", out var actionElement)) payload["action"] = actionElement;
        if (root.TryGetProperty("args", out var argsElement)) payload["args"] = argsElement;
        await SendJsonAsync(payload);
    }

    private async Task SendJsonAsync(object payload)
    {
        if (_socket == null || _socket.State != WebSocketState.Open) throw new InvalidOperationException("Streamer.bot ist nicht verbunden.");
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
    }

    private async Task<string> ReceiveTextAsync(TimeSpan timeout)
    {
        if (_socket == null) return "";
        using var timeoutCts = new CancellationTokenSource(timeout);
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, timeoutCts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new InvalidOperationException("Streamer.bot hat die WebSocket-Verbindung geschlossen.");

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    private static string BuildAuthenticationString(string password, string salt, string challenge)
    {
        using var sha = SHA256.Create();
        var secretBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt));
        var secret = Convert.ToBase64String(secretBytes);
        var authBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge));
        return Convert.ToBase64String(authBytes);
    }

    public IReadOnlyDictionary<string, string> GetCurrentEvents()
    {
        return new Dictionary<string, string>
        {
            ["streamerbot.connected"] = _connected && _socket?.State == WebSocketState.Open ? "true" : "false",
            ["streamerbot.lastAction"] = _lastAction,
            ["streamerbot.lastError"] = _lastError
        };
    }
}
