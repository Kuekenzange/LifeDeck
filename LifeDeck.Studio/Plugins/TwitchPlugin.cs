using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeDeck.Studio.Plugins;

public sealed class TwitchPlugin : IActionPlugin, IStatefulPlugin, IEventStateProvider
{
    private readonly HttpClient _http = new();

    private string _clientId = "";
    private string _accessToken = "";
    private string _refreshToken = "";
    private string _scopes = "channel:manage:broadcast channel:edit:commercial clips:edit moderator:manage:chat_settings";
    private string _broadcasterId = "";
    private string _broadcasterLogin = "";
    private string _broadcasterName = "";
    private string _lastError = "";
    private string _lastStatus = "Nicht verbunden";
    private string _lastMarkerId = "";
    private string _lastClipEditUrl = "";
    private bool _connected;
    private bool _live;
    private int _viewerCount;
    private string _streamTitle = "";
    private string _gameName = "";

    private string _deviceCode = "";
    private string _userCode = "";
    private string _verificationUri = "";
    private int _deviceIntervalSeconds = 5;
    private DateTime _deviceExpiresAtUtc = DateTime.MinValue;

    public string Id => "twitch";
    public string DisplayName => "Twitch";

    private readonly List<PluginActionDefinition> _actions;
    public IReadOnlyList<PluginActionDefinition> Actions => _actions;

    private readonly List<PluginEventDefinition> _events;
    public IReadOnlyList<PluginEventDefinition> Events => _events;

    public IReadOnlyList<PluginSettingDefinition> Settings { get; } = new[]
    {
        new PluginSettingDefinition { Key = "clientId", DisplayName = "Client ID", Hint = "Client ID deiner Twitch Developer App.", SuggestedValues = new[] { "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "scopes", DisplayName = "OAuth Scopes", Hint = "Benötigte Rechte. Standard reicht für Marker, Stream-Info, Commercials, Clips und Chat-Modi.", SuggestedValues = new[] { "channel:manage:broadcast channel:edit:commercial clips:edit moderator:manage:chat_settings", "channel:manage:broadcast clips:edit", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "accessToken", DisplayName = "Access Token", Hint = "Wird beim Geräte-Login automatisch gesetzt.", SuggestedValues = new[] { "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "refreshToken", DisplayName = "Refresh Token", Hint = "Wird beim Geräte-Login automatisch gesetzt.", SuggestedValues = new[] { "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "broadcasterId", DisplayName = "Broadcaster ID", Hint = "Wird nach Verbindung automatisch erkannt.", SuggestedValues = new[] { "Benutzerdefiniert" } }
    };

    public TwitchPlugin()
    {
        _actions = new List<PluginActionDefinition>
        {
            new PluginActionDefinition { Id = "twitch.deviceLoginStart", DisplayName = "Geräte-Login starten", Hint = "Startet den Twitch Device-Code-Login und öffnet die Aktivierungsseite." },
            new PluginActionDefinition { Id = "twitch.deviceLoginComplete", DisplayName = "Geräte-Login abschließen", Hint = "Prüft, ob der Login bestätigt wurde, und speichert Access/Refresh Token." },
            new PluginActionDefinition { Id = "twitch.refreshToken", DisplayName = "Token erneuern", Hint = "Erneuert den Twitch Access Token über den Refresh Token." },
            new PluginActionDefinition { Id = "twitch.refreshData", DisplayName = "Daten aktualisieren", Hint = "Liest Benutzer, Live-Status, Titel, Kategorie und Viewer Count." },
            new PluginActionDefinition { Id = "twitch.createMarker", DisplayName = "Marker setzen", Hint = "Wert = Marker-Beschreibung.", SuggestedValues = new[] { "LifeDeck Marker", "Highlight", "Benutzerdefiniert" } },
            new PluginActionDefinition { Id = "twitch.createClip", DisplayName = "Clip erstellen", Hint = "Erstellt einen Clip vom aktuellen Stream." },
            new PluginActionDefinition { Id = "twitch.setTitle", DisplayName = "Titel ändern", Hint = "Wert = neuer Streamtitel.", SuggestedValues = new[] { "Benutzerdefiniert" } },
            new PluginActionDefinition { Id = "twitch.setCategory", DisplayName = "Kategorie ändern", Hint = "Wert = Kategorie-/Spielname. LifeDeck sucht die passende Twitch-Kategorie.", SuggestedValues = new[] { "Just Chatting", "Minecraft", "Art", "Science & Technology", "Benutzerdefiniert" } },
            new PluginActionDefinition { Id = "twitch.setTitleAndCategory", DisplayName = "Titel + Kategorie ändern", Hint = "Wert = Titel|Kategorie, z.B. Mein Stream|Just Chatting.", SuggestedValues = new[] { "Mein Stream|Just Chatting", "Benutzerdefiniert" } },
            new PluginActionDefinition { Id = "twitch.startCommercial", DisplayName = "Werbung starten", Hint = "Wert = Länge in Sekunden: 30, 60, 90, 120, 150 oder 180.", SuggestedValues = new[] { "30", "60", "90", "120", "150", "180" } },
            new PluginActionDefinition { Id = "twitch.chatEmoteOnly", DisplayName = "Emote-only Mode", Hint = "Wert: toggle, on, off.", SuggestedValues = new[] { "toggle", "on", "off" } },
            new PluginActionDefinition { Id = "twitch.chatSlowMode", DisplayName = "Slowmode", Hint = "Wert: off, 10, 30, 60, 120 Sekunden.", SuggestedValues = new[] { "off", "10", "30", "60", "120", "Benutzerdefiniert" } },
            new PluginActionDefinition { Id = "twitch.chatFollowerMode", DisplayName = "Follower-only Mode", Hint = "Wert: off oder Minuten, z.B. 10.", SuggestedValues = new[] { "off", "10", "30", "60", "Benutzerdefiniert" } }
        };

        _events = new List<PluginEventDefinition>
        {
            new PluginEventDefinition { Id = "twitch.connected", DisplayName = "Verbunden", ValueHint = "true/false", SuggestedValues = new[] { "true", "false" } },
            new PluginEventDefinition { Id = "twitch.live", DisplayName = "Live Status", ValueHint = "true/false", SuggestedValues = new[] { "true", "false" } },
            new PluginEventDefinition { Id = "twitch.viewerCount", DisplayName = "Viewer Count", ValueHint = "Zahl" },
            new PluginEventDefinition { Id = "twitch.title", DisplayName = "Streamtitel", ValueHint = "Text" },
            new PluginEventDefinition { Id = "twitch.category", DisplayName = "Kategorie", ValueHint = "Text" },
            new PluginEventDefinition { Id = "twitch.markerId", DisplayName = "Letzter Marker", ValueHint = "Marker-ID" },
            new PluginEventDefinition { Id = "twitch.clipUrl", DisplayName = "Letzter Clip", ValueHint = "URL" },
            new PluginEventDefinition { Id = "twitch.status", DisplayName = "Status", ValueHint = "Text" },
            new PluginEventDefinition { Id = "twitch.error", DisplayName = "Letzter Fehler", ValueHint = "Text" }
        };
    }

    public string GetSettingValue(string key) => key switch
    {
        "clientId" => _clientId,
        "accessToken" => _accessToken,
        "refreshToken" => _refreshToken,
        "scopes" => _scopes,
        "broadcasterId" => _broadcasterId,
        _ => ""
    };

    public void SetSettingValue(string key, string value)
    {
        switch (key)
        {
            case "clientId": _clientId = value; break;
            case "accessToken": _accessToken = value; break;
            case "refreshToken": _refreshToken = value; break;
            case "scopes": _scopes = value; break;
            case "broadcasterId": _broadcasterId = value; break;
        }
    }

    public async Task ExecuteAsync(string action, string value)
    {
        try
        {
            _lastError = "";
            switch (action)
            {
                case "twitch.deviceLoginStart":
                case "Geräte-Login starten":
                    await StartDeviceLoginAsync();
                    return;
                case "twitch.deviceLoginComplete":
                case "Geräte-Login abschließen":
                    await CompleteDeviceLoginAsync();
                    await RefreshDataAsync();
                    return;
                case "twitch.refreshToken":
                case "Token erneuern":
                    await RefreshTokenAsync();
                    await RefreshDataAsync();
                    return;
                case "twitch.refreshData":
                case "Daten aktualisieren":
                    await EnsureReadyAsync();
                    await RefreshDataAsync();
                    return;
            }

            await EnsureReadyAsync();

            switch (action)
            {
                case "twitch.createMarker":
                case "Marker setzen":
                    await CreateMarkerAsync(string.IsNullOrWhiteSpace(value) || value == "Benutzerdefiniert" ? "LifeDeck Marker" : value);
                    break;
                case "twitch.createClip":
                case "Clip erstellen":
                    await CreateClipAsync();
                    break;
                case "twitch.setTitle":
                case "Titel ändern":
                    await SetChannelInfoAsync(value, null);
                    break;
                case "twitch.setCategory":
                case "Kategorie ändern":
                    await SetChannelInfoAsync(null, value);
                    break;
                case "twitch.setTitleAndCategory":
                case "Titel + Kategorie ändern":
                    await SetTitleAndCategoryAsync(value);
                    break;
                case "twitch.startCommercial":
                case "Werbung starten":
                    await StartCommercialAsync(ParseCommercialLength(value));
                    break;
                case "twitch.chatEmoteOnly":
                case "Emote-only Mode":
                    await SetEmoteOnlyAsync(value);
                    break;
                case "twitch.chatSlowMode":
                case "Slowmode":
                    await SetSlowModeAsync(value);
                    break;
                case "twitch.chatFollowerMode":
                case "Follower-only Mode":
                    await SetFollowerModeAsync(value);
                    break;
            }

            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _lastStatus = "Fehler";
        }
    }

    public PluginButtonState GetState(string action, string value)
    {
        if (action == "twitch.refreshData" || action == "Daten aktualisieren")
            return new PluginButtonState { Subtitle = _live ? $"Live · {_viewerCount}" : "Offline", Color = _live ? "#5B2A86" : "#333333" };

        if (action == "twitch.createMarker" || action == "Marker setzen")
            return new PluginButtonState { Subtitle = string.IsNullOrWhiteSpace(_lastMarkerId) ? "Marker" : "Gesetzt" };

        if (action == "twitch.createClip" || action == "Clip erstellen")
            return new PluginButtonState { Subtitle = string.IsNullOrWhiteSpace(_lastClipEditUrl) ? "Clip" : "Erstellt" };

        return new PluginButtonState { Subtitle = _connected ? "Twitch" : "Setup" };
    }

    public IReadOnlyDictionary<string, string> GetCurrentEvents()
    {
        return new Dictionary<string, string>
        {
            ["twitch.connected"] = _connected ? "true" : "false",
            ["twitch.live"] = _live ? "true" : "false",
            ["twitch.viewerCount"] = _viewerCount.ToString(),
            ["twitch.title"] = _streamTitle,
            ["twitch.category"] = _gameName,
            ["twitch.markerId"] = _lastMarkerId,
            ["twitch.clipUrl"] = _lastClipEditUrl,
            ["twitch.status"] = _lastStatus,
            ["twitch.error"] = _lastError
        };
    }

    private async Task StartDeviceLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_clientId)) throw new InvalidOperationException("Bitte zuerst Twitch Client ID eintragen.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId.Trim(),
            ["scopes"] = _scopes.Trim()
        });

        var json = await PostFormAsync("https://id.twitch.tv/oauth2/device", content, auth: false);
        _deviceCode = json["device_code"]?.GetValue<string>() ?? "";
        _userCode = json["user_code"]?.GetValue<string>() ?? "";
        _verificationUri = json["verification_uri"]?.GetValue<string>() ?? "https://www.twitch.tv/activate";
        _deviceIntervalSeconds = json["interval"]?.GetValue<int>() ?? 5;
        var expires = json["expires_in"]?.GetValue<int>() ?? 1800;
        _deviceExpiresAtUtc = DateTime.UtcNow.AddSeconds(expires);
        _lastStatus = $"Code: {_userCode}";

        try
        {
            Process.Start(new ProcessStartInfo { FileName = _verificationUri, UseShellExecute = true });
        }
        catch { }
    }

    private async Task CompleteDeviceLoginAsync()
    {
        if (string.IsNullOrWhiteSpace(_deviceCode)) throw new InvalidOperationException("Bitte zuerst Geräte-Login starten.");
        if (DateTime.UtcNow > _deviceExpiresAtUtc) throw new InvalidOperationException("Der Geräte-Code ist abgelaufen. Bitte Login neu starten.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId.Trim(),
            ["device_code"] = _deviceCode,
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
        });

        var response = await _http.PostAsync("https://id.twitch.tv/oauth2/token", content);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            if (text.Contains("authorization_pending", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Login noch nicht bestätigt. Öffne Twitch Activate und gib den Code {_userCode} ein.");
            throw new InvalidOperationException(text);
        }

        var json = JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
        _accessToken = json["access_token"]?.GetValue<string>() ?? "";
        _refreshToken = json["refresh_token"]?.GetValue<string>() ?? _refreshToken;
        _deviceCode = "";
        _lastStatus = "Login erfolgreich";
    }

    private async Task RefreshTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(_clientId)) throw new InvalidOperationException("Client ID fehlt.");
        if (string.IsNullOrWhiteSpace(_refreshToken)) throw new InvalidOperationException("Refresh Token fehlt.");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId.Trim(),
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refreshToken
        });

        var json = await PostFormAsync("https://id.twitch.tv/oauth2/token", content, auth: false);
        _accessToken = json["access_token"]?.GetValue<string>() ?? _accessToken;
        _refreshToken = json["refresh_token"]?.GetValue<string>() ?? _refreshToken;
        _lastStatus = "Token erneuert";
    }

    private async Task EnsureReadyAsync()
    {
        if (string.IsNullOrWhiteSpace(_clientId)) throw new InvalidOperationException("Twitch Client ID fehlt.");
        if (string.IsNullOrWhiteSpace(_accessToken)) throw new InvalidOperationException("Twitch Access Token fehlt. Bitte Geräte-Login starten und abschließen.");
        if (string.IsNullOrWhiteSpace(_broadcasterId)) await RefreshUserAsync();
    }

    private async Task RefreshDataAsync()
    {
        await RefreshUserAsync();
        await RefreshStreamAsync();
        _connected = true;
        _lastStatus = _live ? "Live" : "Verbunden";
    }

    private async Task RefreshUserAsync()
    {
        var json = await GetJsonAsync("https://api.twitch.tv/helix/users");
        var data = json["data"]?.AsArray();
        var user = data?.Count > 0 ? data[0]?.AsObject() : null;
        if (user == null) throw new InvalidOperationException("Twitch Benutzer konnte nicht gelesen werden.");
        _broadcasterId = user["id"]?.GetValue<string>() ?? _broadcasterId;
        _broadcasterLogin = user["login"]?.GetValue<string>() ?? "";
        _broadcasterName = user["display_name"]?.GetValue<string>() ?? _broadcasterLogin;
        _connected = true;
    }

    private async Task RefreshStreamAsync()
    {
        if (string.IsNullOrWhiteSpace(_broadcasterId)) return;
        var json = await GetJsonAsync($"https://api.twitch.tv/helix/streams?user_id={Uri.EscapeDataString(_broadcasterId)}");
        var data = json["data"]?.AsArray();
        var stream = data?.Count > 0 ? data[0]?.AsObject() : null;
        _live = stream != null;
        if (stream != null)
        {
            _viewerCount = stream["viewer_count"]?.GetValue<int>() ?? 0;
            _streamTitle = stream["title"]?.GetValue<string>() ?? "";
            _gameName = stream["game_name"]?.GetValue<string>() ?? "";
        }
        else
        {
            _viewerCount = 0;
        }
    }

    private async Task CreateMarkerAsync(string description)
    {
        var body = new JsonObject { ["user_id"] = _broadcasterId, ["description"] = description };
        var json = await PostJsonAsync("https://api.twitch.tv/helix/streams/markers", body);
        var marker = json["data"]?.AsArray()?.FirstOrDefault()?.AsObject();
        _lastMarkerId = marker?["id"]?.GetValue<string>() ?? "";
        _lastStatus = "Marker gesetzt";
    }

    private async Task CreateClipAsync()
    {
        var json = await PostJsonAsync($"https://api.twitch.tv/helix/clips?broadcaster_id={Uri.EscapeDataString(_broadcasterId)}", null);
        var clip = json["data"]?.AsArray()?.FirstOrDefault()?.AsObject();
        _lastClipEditUrl = clip?["edit_url"]?.GetValue<string>() ?? "";
        _lastStatus = "Clip erstellt";
    }

    private async Task SetTitleAndCategoryAsync(string value)
    {
        var parts = (value ?? "").Split('|', 2);
        var title = parts.Length > 0 ? parts[0].Trim() : null;
        var category = parts.Length > 1 ? parts[1].Trim() : null;
        await SetChannelInfoAsync(title, category);
    }

    private async Task SetChannelInfoAsync(string? title, string? categoryName)
    {
        var body = new JsonObject();
        if (!string.IsNullOrWhiteSpace(title) && title != "Benutzerdefiniert") body["title"] = title;
        if (!string.IsNullOrWhiteSpace(categoryName) && categoryName != "Benutzerdefiniert")
        {
            var gameId = await FindCategoryIdAsync(categoryName);
            if (!string.IsNullOrWhiteSpace(gameId)) body["game_id"] = gameId;
        }
        if (body.Count == 0) return;
        await PatchJsonAsync($"https://api.twitch.tv/helix/channels?broadcaster_id={Uri.EscapeDataString(_broadcasterId)}", body);
        _lastStatus = "Stream-Info geändert";
    }

    private async Task<string> FindCategoryIdAsync(string query)
    {
        var json = await GetJsonAsync($"https://api.twitch.tv/helix/search/categories?query={Uri.EscapeDataString(query)}");
        var data = json["data"]?.AsArray();
        if (data == null || data.Count == 0) return "";
        var exact = data.Select(n => n?.AsObject()).FirstOrDefault(o => string.Equals(o?["name"]?.GetValue<string>(), query, StringComparison.OrdinalIgnoreCase));
        var chosen = exact ?? data[0]?.AsObject();
        return chosen?["id"]?.GetValue<string>() ?? "";
    }

    private async Task StartCommercialAsync(int length)
    {
        var body = new JsonObject { ["broadcaster_id"] = _broadcasterId, ["length"] = length };
        await PostJsonAsync("https://api.twitch.tv/helix/channels/commercial", body);
        _lastStatus = $"Werbung gestartet ({length}s)";
    }

    private async Task SetEmoteOnlyAsync(string value)
    {
        value = Normalize(value, "toggle");
        var current = false;
        var enabled = value == "toggle" ? !current : value == "on" || value == "true";
        await PatchChatSettingsAsync(new JsonObject { ["emote_mode"] = enabled });
        _lastStatus = enabled ? "Emote-only an" : "Emote-only aus";
    }

    private async Task SetSlowModeAsync(string value)
    {
        value = Normalize(value, "30");
        if (value == "off" || value == "0" || value == "false")
        {
            await PatchChatSettingsAsync(new JsonObject { ["slow_mode"] = false });
            _lastStatus = "Slowmode aus";
            return;
        }
        var seconds = int.TryParse(value, out var n) ? Math.Clamp(n, 3, 120) : 30;
        await PatchChatSettingsAsync(new JsonObject { ["slow_mode"] = true, ["slow_mode_wait_time"] = seconds });
        _lastStatus = $"Slowmode {seconds}s";
    }

    private async Task SetFollowerModeAsync(string value)
    {
        value = Normalize(value, "10");
        if (value == "off" || value == "0" || value == "false")
        {
            await PatchChatSettingsAsync(new JsonObject { ["follower_mode"] = false });
            _lastStatus = "Follower-only aus";
            return;
        }
        var minutes = int.TryParse(value, out var n) ? Math.Clamp(n, 0, 129600) : 10;
        await PatchChatSettingsAsync(new JsonObject { ["follower_mode"] = true, ["follower_mode_duration"] = minutes });
        _lastStatus = $"Follower-only {minutes}m";
    }

    private async Task PatchChatSettingsAsync(JsonObject body)
    {
        await PatchJsonAsync($"https://api.twitch.tv/helix/chat/settings?broadcaster_id={Uri.EscapeDataString(_broadcasterId)}&moderator_id={Uri.EscapeDataString(_broadcasterId)}", body);
    }

    private int ParseCommercialLength(string value)
    {
        if (!int.TryParse(value, out var length)) length = 30;
        var allowed = new[] { 30, 60, 90, 120, 150, 180 };
        return allowed.Contains(length) ? length : 30;
    }

    private static string Normalize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Benutzerdefiniert") return fallback;
        return value.Trim().ToLowerInvariant();
    }

    private async Task<JsonObject> GetJsonAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuthHeaders(req);
        return await SendJsonAsync(req);
    }

    private async Task<JsonObject> PostJsonAsync(string url, JsonObject? body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        AddAuthHeaders(req);
        if (body != null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await SendJsonAsync(req);
    }

    private async Task<JsonObject> PatchJsonAsync(string url, JsonObject body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, url);
        AddAuthHeaders(req);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await SendJsonAsync(req);
    }

    private async Task<JsonObject> PostFormAsync(string url, FormUrlEncodedContent content, bool auth)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        if (auth) AddAuthHeaders(req);
        return await SendJsonAsync(req);
    }

    private void AddAuthHeaders(HttpRequestMessage req)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken.Trim());
        if (!string.IsNullOrWhiteSpace(_clientId)) req.Headers.TryAddWithoutValidation("Client-Id", _clientId.Trim());
    }

    private async Task<JsonObject> SendJsonAsync(HttpRequestMessage req)
    {
        var res = await _http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(_refreshToken))
            {
                await RefreshTokenAsync();
                throw new InvalidOperationException("Token wurde erneuert. Bitte Aktion erneut ausführen.");
            }
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(text) ? res.ReasonPhrase ?? "Twitch API Fehler" : text);
        }
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text)?.AsObject() ?? new JsonObject();
    }
}
