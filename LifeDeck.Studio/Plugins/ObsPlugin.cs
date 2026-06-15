using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeDeck.Studio.Plugins;

public sealed class ObsPlugin : IActionPlugin, IStatefulPlugin, IEventStateProvider, IDisposable
{
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _cts;
    private readonly SemaphoreSlim _requestLock = new(1, 1);

    private bool _connected;
    private bool _recording;
    private bool _streaming;
    private bool _replayBuffer;
    private bool _studioMode;
    private string _currentScene = "";
    private string _currentTransition = "";
    private string _currentProfile = "";
    private string _currentSceneCollection = "";
    private string _lastError = "";

    private readonly List<string> _scenes = new();
    private readonly List<string> _inputs = new();
    private readonly List<string> _transitions = new();
    private readonly List<string> _profiles = new();
    private readonly List<string> _sceneCollections = new();
    private readonly Dictionary<string, bool> _inputMuteStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ObsSceneItemRef> _sceneItems = new();
    private readonly Dictionary<string, bool> _sceneItemStates = new(StringComparer.OrdinalIgnoreCase);

    public string Host { get; set; } = "localhost";
    public string Port { get; set; } = "4455";
    public string Password { get; set; } = "";
    public string AutoConnect { get; set; } = "true";

    public string Id => "obs";
    public string DisplayName => "OBS";

    private readonly List<PluginActionDefinition> _actions;
    public IReadOnlyList<PluginActionDefinition> Actions => _actions;

    private readonly List<PluginEventDefinition> _events;
    public IReadOnlyList<PluginEventDefinition> Events => _events;

    public IReadOnlyList<PluginSettingDefinition> Settings { get; } = new[]
    {
        new PluginSettingDefinition { Key = "host", DisplayName = "OBS Host", Hint = "Meistens localhost.", SuggestedValues = new[] { "localhost", "127.0.0.1", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "port", DisplayName = "OBS WebSocket Port", Hint = "Standard bei OBS 28+ ist 4455.", SuggestedValues = new[] { "4455", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "password", DisplayName = "OBS WebSocket Passwort", Hint = "Leer lassen, wenn in OBS kein Passwort gesetzt ist." },
        new PluginSettingDefinition { Key = "autoConnect", DisplayName = "Auto Connect", Hint = "Bei OBS-Aktionen automatisch verbinden.", SuggestedValues = new[] { "true", "false" } }
    };

    public ObsPlugin()
    {
        _actions = new List<PluginActionDefinition>
        {
            new PluginActionDefinition { Id = "obs.connect", DisplayName = "Verbinden", Hint = "Verbindet LifeDeck mit OBS WebSocket." },
            new PluginActionDefinition { Id = "obs.disconnect", DisplayName = "Trennen", Hint = "Trennt die OBS WebSocket-Verbindung." },
            new PluginActionDefinition { Id = "obs.refresh", DisplayName = "Daten aktualisieren", Hint = "Liest Szenen, Inputs, Quellen und Status neu aus OBS." },
            new PluginActionDefinition { Id = "obs.setScene", DisplayName = "Szene wechseln", Hint = "Wert = Szenenname. Nach Verbindung werden Szenen als Werte vorgeschlagen.", SuggestedValues = WithCustomScenes() },
            new PluginActionDefinition { Id = "obs.recording", DisplayName = "Aufnahme", Hint = "Startet, stoppt oder toggelt die OBS-Aufnahme.", SuggestedValues = new[] { "toggle", "start", "stop" } },
            new PluginActionDefinition { Id = "obs.streaming", DisplayName = "Stream", Hint = "Startet, stoppt oder toggelt den OBS-Stream.", SuggestedValues = new[] { "toggle", "start", "stop" } },
            new PluginActionDefinition { Id = "obs.replayBuffer", DisplayName = "Replay Buffer", Hint = "Startet, stoppt, toggelt oder speichert den Replay Buffer.", SuggestedValues = new[] { "toggle", "start", "stop", "save" } },
            new PluginActionDefinition { Id = "obs.studioMode", DisplayName = "Studio Mode", Hint = "Aktiviert, deaktiviert oder toggelt den OBS Studio Mode.", SuggestedValues = new[] { "toggle", "enable", "disable" } },
            new PluginActionDefinition { Id = "obs.setTransition", DisplayName = "Transition wechseln", Hint = "Wert = Transition-Name. Nach Aktualisieren werden Transitionen vorgeschlagen.", SuggestedValues = WithCustomTransitions() },
            new PluginActionDefinition { Id = "obs.setProfile", DisplayName = "Profil wechseln", Hint = "Wert = OBS-Profilname. Nach Aktualisieren werden Profile vorgeschlagen.", SuggestedValues = WithCustomProfiles() },
            new PluginActionDefinition { Id = "obs.setSceneCollection", DisplayName = "Scene Collection wechseln", Hint = "Wert = OBS-Szenensammlung. Nach Aktualisieren werden Collections vorgeschlagen.", SuggestedValues = WithCustomSceneCollections() },
            new PluginActionDefinition { Id = "obs.inputMute", DisplayName = "Audio Mute", Hint = "Wert: Input|toggle, Input|mute oder Input|unmute. Nach Aktualisieren werden Inputs vorgeschlagen.", SuggestedValues = WithCustomInputs() },
            new PluginActionDefinition { Id = "obs.sourceVisibility", DisplayName = "Quelle sichtbar", Hint = "Wert: Szene|Quelle|toggle, Szene|Quelle|show oder Szene|Quelle|hide. Nach Aktualisieren werden Quellen vorgeschlagen.", SuggestedValues = WithCustomSceneItems() }
        };

        _events = new List<PluginEventDefinition>
        {
            new PluginEventDefinition { Id = "obs.connected", DisplayName = "Verbunden", ValueHint = "true/false", SuggestedValues = new[] { "true", "false" } },
            new PluginEventDefinition { Id = "obs.recording", DisplayName = "Recording aktiv", ValueHint = "true/false", SuggestedValues = new[] { "true", "false" } },
            new PluginEventDefinition { Id = "obs.streaming", DisplayName = "Streaming aktiv", ValueHint = "true/false", SuggestedValues = new[] { "true", "false" } },
            new PluginEventDefinition { Id = "obs.replayBuffer", DisplayName = "Replay Buffer aktiv", ValueHint = "true/false", SuggestedValues = new[] { "true", "false" } },
            new PluginEventDefinition { Id = "obs.studioMode", DisplayName = "Studio Mode aktiv", ValueHint = "true/false", SuggestedValues = new[] { "true", "false" } },
            new PluginEventDefinition { Id = "obs.scene", DisplayName = "Aktuelle Szene", ValueHint = "Szenenname" },
            new PluginEventDefinition { Id = "obs.transition", DisplayName = "Aktuelle Transition", ValueHint = "Transition" },
            new PluginEventDefinition { Id = "obs.profile", DisplayName = "Aktuelles Profil", ValueHint = "Profil" },
            new PluginEventDefinition { Id = "obs.sceneCollection", DisplayName = "Aktuelle Scene Collection", ValueHint = "Szenensammlung" },
            new PluginEventDefinition { Id = "obs.error", DisplayName = "Letzter Fehler", ValueHint = "Text" }
        };
    }

    public string GetSettingValue(string key) => key switch
    {
        "host" => Host,
        "port" => Port,
        "password" => Password,
        "autoConnect" => AutoConnect,
        _ => ""
    };

    public void SetSettingValue(string key, string value)
    {
        switch (key)
        {
            case "host": Host = value; break;
            case "port": Port = value; break;
            case "password": Password = value; break;
            case "autoConnect": AutoConnect = value; break;
        }
    }

    public async Task ExecuteAsync(string action, string value)
    {
        try
        {
            switch (action)
            {
                case "obs.connect":
                case "Verbinden":
                    await ConnectAsync();
                    await RefreshStatusAsync();
                    return;

                case "obs.disconnect":
                case "Trennen":
                    await DisconnectAsync();
                    return;

                case "obs.refresh":
                case "Daten aktualisieren":
                    await EnsureConnectedIfWantedAsync();
                    await RefreshStatusAsync();
                    return;
            }

            await EnsureConnectedIfWantedAsync();
            if (!_connected) return;

            switch (action)
            {
                case "obs.setScene":
                case "Szene wechseln":
                    if (!string.IsNullOrWhiteSpace(value) && value != "Benutzerdefiniert")
                    {
                        await SendRequestAsync("SetCurrentProgramScene", new JsonObject { ["sceneName"] = value });
                        _currentScene = value;
                    }
                    break;

                case "obs.recording":
                case "Aufnahme":
                    await ExecuteRecordingAsync(value);
                    break;

                case "obs.streaming":
                case "Stream":
                    await ExecuteStreamingAsync(value);
                    break;

                case "obs.replayBuffer":
                case "Replay Buffer":
                    await ExecuteReplayBufferAsync(value);
                    break;

                case "obs.studioMode":
                case "Studio Mode":
                    await ExecuteStudioModeAsync(value);
                    break;

                case "obs.setTransition":
                case "Transition wechseln":
                    await ExecuteSetTransitionAsync(value);
                    break;

                case "obs.setProfile":
                case "Profil wechseln":
                    await ExecuteSetProfileAsync(value);
                    break;

                case "obs.setSceneCollection":
                case "Scene Collection wechseln":
                    await ExecuteSetSceneCollectionAsync(value);
                    break;

                case "obs.inputMute":
                case "Audio Mute":
                    await ExecuteInputMuteAsync(value);
                    break;

                case "obs.sourceVisibility":
                case "Quelle sichtbar":
                    await ExecuteSourceVisibilityAsync(value);
                    break;
            }

            await RefreshStatusAsync();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _connected = false;
        }
    }

    private async Task ExecuteRecordingAsync(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "toggle" : value.ToLowerInvariant();
        if (value == "start") await SendRequestAsync("StartRecord");
        else if (value == "stop") await SendRequestAsync("StopRecord");
        else await SendRequestAsync("ToggleRecord");
    }

    private async Task ExecuteStreamingAsync(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "toggle" : value.ToLowerInvariant();
        if (value == "start") await SendRequestAsync("StartStream");
        else if (value == "stop") await SendRequestAsync("StopStream");
        else await SendRequestAsync("ToggleStream");
    }

    private async Task ExecuteReplayBufferAsync(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "toggle" : value.ToLowerInvariant();
        if (value == "start") await SendRequestAsync("StartReplayBuffer");
        else if (value == "stop") await SendRequestAsync("StopReplayBuffer");
        else if (value == "save") await SendRequestAsync("SaveReplayBuffer");
        else
        {
            await RefreshReplayBufferAsync();
            await SendRequestAsync(_replayBuffer ? "StopReplayBuffer" : "StartReplayBuffer");
        }
    }

    private async Task ExecuteStudioModeAsync(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "toggle" : value.ToLowerInvariant();
        var enabled = value switch
        {
            "enable" or "on" or "true" => true,
            "disable" or "off" or "false" => false,
            _ => !_studioMode
        };
        await SendRequestAsync("SetStudioModeEnabled", new JsonObject { ["studioModeEnabled"] = enabled });
        _studioMode = enabled;
    }

    private async Task ExecuteSetTransitionAsync(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Benutzerdefiniert") return;
        await SendRequestAsync("SetCurrentSceneTransition", new JsonObject { ["transitionName"] = value });
        _currentTransition = value;
    }

    private async Task ExecuteSetProfileAsync(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Benutzerdefiniert") return;
        await SendRequestAsync("SetCurrentProfile", new JsonObject { ["profileName"] = value });
        _currentProfile = value;
    }

    private async Task ExecuteSetSceneCollectionAsync(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "Benutzerdefiniert") return;
        await SendRequestAsync("SetCurrentSceneCollection", new JsonObject { ["sceneCollectionName"] = value });
        _currentSceneCollection = value;
    }

    private async Task ExecuteInputMuteAsync(string value)
    {
        var (input, mode) = ParseTwoPartValue(value, "toggle");
        if (string.IsNullOrWhiteSpace(input) || input == "Benutzerdefiniert") return;

        mode = NormalizeMode(mode, "toggle");
        if (mode == "toggle")
        {
            var response = await SendRequestAsync("ToggleInputMute", new JsonObject { ["inputName"] = input });
            var muted = response?["inputMuted"]?.GetValue<bool>();
            if (muted.HasValue) _inputMuteStates[input] = muted.Value;
        }
        else
        {
            var mute = mode == "mute" || mode == "true" || mode == "on";
            await SendRequestAsync("SetInputMute", new JsonObject { ["inputName"] = input, ["inputMuted"] = mute });
            _inputMuteStates[input] = mute;
        }
    }

    private async Task ExecuteSourceVisibilityAsync(string value)
    {
        var parts = SplitValue(value);
        if (parts.Length < 2) return;

        var scene = parts[0];
        var source = parts[1];
        var mode = NormalizeMode(parts.Length >= 3 ? parts[2] : "toggle", "toggle");
        if (string.IsNullOrWhiteSpace(scene) || string.IsNullOrWhiteSpace(source) || scene == "Benutzerdefiniert") return;

        var item = _sceneItems.FirstOrDefault(x =>
            string.Equals(x.SceneName, scene, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.SourceName, source, StringComparison.OrdinalIgnoreCase));

        if (item == null)
        {
            await RefreshSceneItemsAsync();
            item = _sceneItems.FirstOrDefault(x =>
                string.Equals(x.SceneName, scene, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.SourceName, source, StringComparison.OrdinalIgnoreCase));
        }

        if (item == null) return;

        var enabled = true;
        if (mode == "toggle")
        {
            try
            {
                var response = await SendRequestAsync("GetSceneItemEnabled", new JsonObject { ["sceneName"] = item.SceneName, ["sceneItemId"] = item.SceneItemId });
                enabled = response?["sceneItemEnabled"]?.GetValue<bool>() ?? true;
                enabled = !enabled;
            }
            catch
            {
                var key = SceneItemKey(item.SceneName, item.SourceName);
                enabled = !_sceneItemStates.TryGetValue(key, out var old) || !old;
            }
        }
        else
        {
            enabled = mode == "show" || mode == "true" || mode == "on";
        }

        await SendRequestAsync("SetSceneItemEnabled", new JsonObject { ["sceneName"] = item.SceneName, ["sceneItemId"] = item.SceneItemId, ["sceneItemEnabled"] = enabled });
        _sceneItemStates[SceneItemKey(item.SceneName, item.SourceName)] = enabled;
    }

    private async Task EnsureConnectedIfWantedAsync()
    {
        if (_connected && _socket?.State == WebSocketState.Open) return;
        if (!string.Equals(AutoConnect, "true", StringComparison.OrdinalIgnoreCase)) return;
        await ConnectAsync();
    }

    public async Task ConnectAsync()
    {
        await DisconnectAsync();
        _cts = new CancellationTokenSource();
        _socket = new ClientWebSocket();
        var uri = new Uri($"ws://{Host}:{Port}");
        await _socket.ConnectAsync(uri, _cts.Token);

        var hello = await ReceiveObjectAsync(_cts.Token);
        if (hello?["op"]?.GetValue<int>() != 0)
            throw new InvalidOperationException("OBS WebSocket hat kein Hello gesendet.");

        var d = hello["d"]?.AsObject();
        var auth = d?["authentication"]?.AsObject();
        var identify = new JsonObject { ["rpcVersion"] = 1 };
        if (auth != null && !string.IsNullOrWhiteSpace(Password))
        {
            var challenge = auth["challenge"]?.GetValue<string>() ?? "";
            var salt = auth["salt"]?.GetValue<string>() ?? "";
            identify["authentication"] = BuildAuthentication(Password, salt, challenge);
        }

        await SendObjectAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, _cts.Token);
        var identified = await ReceiveObjectAsync(_cts.Token);
        if (identified?["op"]?.GetValue<int>() != 2)
            throw new InvalidOperationException("OBS WebSocket Identify fehlgeschlagen.");

        _connected = true;
        _lastError = "";
        await RefreshStatusAsync();
    }

    public async Task DisconnectAsync()
    {
        _connected = false;
        try { _cts?.Cancel(); } catch { }
        if (_socket != null)
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "LifeDeck disconnect", CancellationToken.None);
            }
            catch { }
            _socket.Dispose();
        }
        _socket = null;
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RefreshStatusAsync()
    {
        if (!_connected || _socket?.State != WebSocketState.Open) return;

        try
        {
            var scene = await SendRequestAsync("GetCurrentProgramScene");
            _currentScene = scene?["currentProgramSceneName"]?.GetValue<string>() ?? _currentScene;
        }
        catch { }

        try
        {
            var rec = await SendRequestAsync("GetRecordStatus");
            _recording = rec?["outputActive"]?.GetValue<bool>() ?? _recording;
        }
        catch { }

        try
        {
            var stream = await SendRequestAsync("GetStreamStatus");
            _streaming = stream?["outputActive"]?.GetValue<bool>() ?? _streaming;
        }
        catch { }

        await RefreshReplayBufferAsync();
        await RefreshStudioModeAsync();
        await RefreshTransitionsAsync();
        await RefreshProfilesAsync();
        await RefreshSceneCollectionsAsync();
        await RefreshScenesAsync();
        await RefreshInputsAsync();
        await RefreshSceneItemsAsync();
        UpdateSuggestionsAndEvents();
    }

    private async Task RefreshReplayBufferAsync()
    {
        try
        {
            var rb = await SendRequestAsync("GetReplayBufferStatus");
            _replayBuffer = rb?["outputActive"]?.GetValue<bool>() ?? _replayBuffer;
        }
        catch { }
    }

    private async Task RefreshStudioModeAsync()
    {
        try
        {
            var sm = await SendRequestAsync("GetStudioModeEnabled");
            _studioMode = sm?["studioModeEnabled"]?.GetValue<bool>() ?? _studioMode;
        }
        catch { }
    }

    private async Task RefreshTransitionsAsync()
    {
        try
        {
            var transitions = await SendRequestAsync("GetSceneTransitionList");
            _currentTransition = transitions?["currentSceneTransitionName"]?.GetValue<string>() ?? _currentTransition;
            var arr = transitions?["transitions"]?.AsArray();
            _transitions.Clear();
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    var name = item?["transitionName"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(name)) _transitions.Add(name);
                }
            }
        }
        catch { }
    }

    private async Task RefreshProfilesAsync()
    {
        try
        {
            var profiles = await SendRequestAsync("GetProfileList");
            _currentProfile = profiles?["currentProfileName"]?.GetValue<string>() ?? _currentProfile;
            var arr = profiles?["profiles"]?.AsArray();
            _profiles.Clear();
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    var name = item?["profileName"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(name)) _profiles.Add(name);
                }
            }
        }
        catch { }
    }

    private async Task RefreshSceneCollectionsAsync()
    {
        try
        {
            var collections = await SendRequestAsync("GetSceneCollectionList");
            _currentSceneCollection = collections?["currentSceneCollectionName"]?.GetValue<string>() ?? _currentSceneCollection;
            var arr = collections?["sceneCollections"]?.AsArray();
            _sceneCollections.Clear();
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    var name = item?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(name)) _sceneCollections.Add(name);
                }
            }
        }
        catch { }
    }

    private async Task RefreshScenesAsync()
    {
        try
        {
            var scenes = await SendRequestAsync("GetSceneList");
            var arr = scenes?["scenes"]?.AsArray();
            _scenes.Clear();
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    var name = item?["sceneName"]?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(name)) _scenes.Add(name);
                }
            }
        }
        catch { }
    }

    private async Task RefreshInputsAsync()
    {
        try
        {
            var inputs = await SendRequestAsync("GetInputList");
            var arr = inputs?["inputs"]?.AsArray();
            _inputs.Clear();
            if (arr != null)
            {
                foreach (var item in arr)
                {
                    var name = item?["inputName"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    _inputs.Add(name);

                    try
                    {
                        var mute = await SendRequestAsync("GetInputMute", new JsonObject { ["inputName"] = name });
                        var muted = mute?["inputMuted"]?.GetValue<bool>();
                        if (muted.HasValue) _inputMuteStates[name] = muted.Value;
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private async Task RefreshSceneItemsAsync()
    {
        _sceneItems.Clear();
        foreach (var scene in _scenes.ToArray())
        {
            try
            {
                var items = await SendRequestAsync("GetSceneItemList", new JsonObject { ["sceneName"] = scene });
                var arr = items?["sceneItems"]?.AsArray();
                if (arr == null) continue;

                foreach (var item in arr)
                {
                    var sourceName = item?["sourceName"]?.GetValue<string>();
                    var sceneItemId = item?["sceneItemId"]?.GetValue<int>();
                    if (string.IsNullOrWhiteSpace(sourceName) || !sceneItemId.HasValue) continue;

                    var enabled = item?["sceneItemEnabled"]?.GetValue<bool>() ?? true;
                    var refItem = new ObsSceneItemRef(scene, sourceName, sceneItemId.Value);
                    _sceneItems.Add(refItem);
                    _sceneItemStates[SceneItemKey(scene, sourceName)] = enabled;
                }
            }
            catch { }
        }
    }

    private async Task<JsonObject?> SendRequestAsync(string requestType, JsonObject? requestData = null)
    {
        if (_socket == null || _socket.State != WebSocketState.Open || _cts == null)
            throw new InvalidOperationException("OBS ist nicht verbunden.");

        await _requestLock.WaitAsync(_cts.Token);
        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            var d = new JsonObject { ["requestType"] = requestType, ["requestId"] = requestId };
            if (requestData != null) d["requestData"] = requestData;
            await SendObjectAsync(new JsonObject { ["op"] = 6, ["d"] = d }, _cts.Token);

            while (true)
            {
                var msg = await ReceiveObjectAsync(_cts.Token);
                var op = msg?["op"]?.GetValue<int>() ?? -1;
                if (op == 5)
                {
                    HandleEvent(msg?["d"]?.AsObject());
                    continue;
                }

                if (op != 7) continue;
                var rd = msg?["d"]?.AsObject();
                if (rd?["requestId"]?.GetValue<string>() != requestId) continue;

                var status = rd["requestStatus"]?.AsObject();
                var ok = status?["result"]?.GetValue<bool>() ?? false;
                if (!ok)
                {
                    var comment = status?["comment"]?.GetValue<string>() ?? requestType + " fehlgeschlagen";
                    throw new InvalidOperationException(comment);
                }

                return rd["responseData"]?.AsObject();
            }
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private void HandleEvent(JsonObject? d)
    {
        if (d == null) return;
        var eventType = d["eventType"]?.GetValue<string>() ?? "";
        var data = d["eventData"]?.AsObject();
        switch (eventType)
        {
            case "CurrentProgramSceneChanged":
                _currentScene = data?["sceneName"]?.GetValue<string>() ?? _currentScene;
                break;
            case "RecordStateChanged":
                _recording = data?["outputActive"]?.GetValue<bool>() ?? _recording;
                break;
            case "StreamStateChanged":
                _streaming = data?["outputActive"]?.GetValue<bool>() ?? _streaming;
                break;
            case "ReplayBufferStateChanged":
                _replayBuffer = data?["outputActive"]?.GetValue<bool>() ?? _replayBuffer;
                break;
            case "StudioModeStateChanged":
                _studioMode = data?["studioModeEnabled"]?.GetValue<bool>() ?? _studioMode;
                break;
            case "CurrentSceneTransitionChanged":
                _currentTransition = data?["transitionName"]?.GetValue<string>() ?? _currentTransition;
                break;
            case "CurrentProfileChanged":
                _currentProfile = data?["profileName"]?.GetValue<string>() ?? _currentProfile;
                break;
            case "CurrentSceneCollectionChanged":
                _currentSceneCollection = data?["sceneCollectionName"]?.GetValue<string>() ?? _currentSceneCollection;
                break;
            case "InputMuteStateChanged":
                {
                    var name = data?["inputName"]?.GetValue<string>();
                    var muted = data?["inputMuted"]?.GetValue<bool>();
                    if (!string.IsNullOrWhiteSpace(name) && muted.HasValue) _inputMuteStates[name] = muted.Value;
                    break;
                }
            case "SceneItemEnableStateChanged":
                // OBS liefert hier je nach Version sceneName + sceneItemId. Wir aktualisieren beim nächsten Refresh vollständig.
                break;
        }
    }

    private async Task SendObjectAsync(JsonObject obj, CancellationToken token)
    {
        var json = obj.ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket!.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    private async Task<JsonObject?> ReceiveObjectAsync(CancellationToken token)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await _socket!.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("OBS WebSocket wurde geschlossen.");
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        return JsonNode.Parse(Encoding.UTF8.GetString(ms.ToArray()))?.AsObject();
    }

    private static string BuildAuthentication(string password, string salt, string challenge)
    {
        using var sha = SHA256.Create();
        var secret = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private void UpdateSuggestionsAndEvents()
    {
        var sceneAction = _actions.FirstOrDefault(a => a.Id == "obs.setScene");
        if (sceneAction != null) sceneAction.SuggestedValues = WithCustomScenes();

        var transitionAction = _actions.FirstOrDefault(a => a.Id == "obs.setTransition");
        if (transitionAction != null) transitionAction.SuggestedValues = WithCustomTransitions();

        var profileAction = _actions.FirstOrDefault(a => a.Id == "obs.setProfile");
        if (profileAction != null) profileAction.SuggestedValues = WithCustomProfiles();

        var collectionAction = _actions.FirstOrDefault(a => a.Id == "obs.setSceneCollection");
        if (collectionAction != null) collectionAction.SuggestedValues = WithCustomSceneCollections();

        var inputAction = _actions.FirstOrDefault(a => a.Id == "obs.inputMute");
        if (inputAction != null) inputAction.SuggestedValues = WithCustomInputs();

        var sourceAction = _actions.FirstOrDefault(a => a.Id == "obs.sourceVisibility");
        if (sourceAction != null) sourceAction.SuggestedValues = WithCustomSceneItems();

        _events.RemoveAll(e => e.Id.StartsWith("obs.input.", StringComparison.OrdinalIgnoreCase) || e.Id.StartsWith("obs.source.", StringComparison.OrdinalIgnoreCase));

        foreach (var input in _inputs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _events.Add(new PluginEventDefinition
            {
                Id = "obs.input." + SafeId(input) + ".muted",
                DisplayName = "Input muted: " + input,
                ValueHint = "true/false",
                SuggestedValues = new[] { "true", "false" }
            });
        }

        foreach (var item in _sceneItems.GroupBy(x => SceneItemKey(x.SceneName, x.SourceName)).Select(g => g.First()))
        {
            _events.Add(new PluginEventDefinition
            {
                Id = "obs.source." + SafeId(item.SceneName) + "." + SafeId(item.SourceName) + ".visible",
                DisplayName = "Quelle sichtbar: " + item.SceneName + " / " + item.SourceName,
                ValueHint = "true/false",
                SuggestedValues = new[] { "true", "false" }
            });
        }
    }

    private IReadOnlyList<string> WithCustomScenes()
    {
        var list = new List<string>();
        list.AddRange(_scenes.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));
        list.Add("Benutzerdefiniert");
        return list;
    }

    private IReadOnlyList<string> WithCustomTransitions()
    {
        var list = new List<string>();
        list.AddRange(_transitions.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));
        list.Add("Benutzerdefiniert");
        return list;
    }

    private IReadOnlyList<string> WithCustomProfiles()
    {
        var list = new List<string>();
        list.AddRange(_profiles.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));
        list.Add("Benutzerdefiniert");
        return list;
    }

    private IReadOnlyList<string> WithCustomSceneCollections()
    {
        var list = new List<string>();
        list.AddRange(_sceneCollections.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase));
        list.Add("Benutzerdefiniert");
        return list;
    }

    private IReadOnlyList<string> WithCustomInputs()
    {
        var list = new List<string>();
        foreach (var input in _inputs.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            list.Add(input + "|toggle");
            list.Add(input + "|mute");
            list.Add(input + "|unmute");
        }
        list.Add("Benutzerdefiniert");
        return list;
    }

    private IReadOnlyList<string> WithCustomSceneItems()
    {
        var list = new List<string>();
        foreach (var item in _sceneItems.GroupBy(x => SceneItemKey(x.SceneName, x.SourceName)).Select(g => g.First()))
        {
            list.Add(item.SceneName + "|" + item.SourceName + "|toggle");
            list.Add(item.SceneName + "|" + item.SourceName + "|show");
            list.Add(item.SceneName + "|" + item.SourceName + "|hide");
        }
        list.Add("Benutzerdefiniert");
        return list;
    }

    public PluginButtonState GetState(string action, string value)
    {
        if (action == "obs.replayBuffer" || action == "Replay Buffer")
            return new PluginButtonState { Subtitle = _replayBuffer ? "Aktiv" : "Aus", Color = _replayBuffer ? "#1F6F3A" : "#333333" };

        if (action == "obs.studioMode" || action == "Studio Mode")
            return new PluginButtonState { Subtitle = _studioMode ? "Aktiv" : "Aus", Color = _studioMode ? "#1F6F3A" : "#333333" };

        if (action == "obs.setTransition" || action == "Transition wechseln")
            return new PluginButtonState { Subtitle = string.IsNullOrWhiteSpace(_currentTransition) ? "Transition" : _currentTransition };

        if (action == "obs.setProfile" || action == "Profil wechseln")
            return new PluginButtonState { Subtitle = string.IsNullOrWhiteSpace(_currentProfile) ? "Profil" : _currentProfile };

        if (action == "obs.setSceneCollection" || action == "Scene Collection wechseln")
            return new PluginButtonState { Subtitle = string.IsNullOrWhiteSpace(_currentSceneCollection) ? "Collection" : _currentSceneCollection };

        if (action == "obs.inputMute" || action == "Audio Mute")
        {
            var (input, _) = ParseTwoPartValue(value, "toggle");
            if (!string.IsNullOrWhiteSpace(input) && _inputMuteStates.TryGetValue(input, out var muted))
            {
                return new PluginButtonState { Subtitle = muted ? "Muted" : "Unmuted", Color = muted ? "#7A1F1F" : "#1F6F3A" };
            }
            return new PluginButtonState { Subtitle = string.IsNullOrWhiteSpace(input) ? "Input" : input };
        }

        if (action == "obs.sourceVisibility" || action == "Quelle sichtbar")
        {
            var parts = SplitValue(value);
            if (parts.Length >= 2)
            {
                var key = SceneItemKey(parts[0], parts[1]);
                if (_sceneItemStates.TryGetValue(key, out var visible))
                    return new PluginButtonState { Subtitle = visible ? "Visible" : "Hidden", Color = visible ? "#1F6F3A" : "#333333" };
                return new PluginButtonState { Subtitle = parts[1] };
            }
            return new PluginButtonState { Subtitle = "Quelle" };
        }

        return action switch
        {
            "obs.connect" or "Verbinden" => new PluginButtonState { Subtitle = _connected ? "Verbunden" : "Getrennt", Color = _connected ? "#1F6F3A" : "#7A1F1F" },
            "obs.recording" or "Aufnahme" => new PluginButtonState { Subtitle = _recording ? "Recording" : "Idle", Color = _recording ? "#B71C1C" : "#333333" },
            "obs.streaming" or "Stream" => new PluginButtonState { Subtitle = _streaming ? "Live" : "Offline", Color = _streaming ? "#7B1FA2" : "#333333" },
            "obs.setScene" or "Szene wechseln" => new PluginButtonState { Subtitle = string.IsNullOrWhiteSpace(_currentScene) ? "Keine Szene" : _currentScene },
            _ => new PluginButtonState()
        };
    }

    public IReadOnlyDictionary<string, string> GetCurrentEvents()
    {
        var result = new Dictionary<string, string>
        {
            ["obs.connected"] = _connected ? "true" : "false",
            ["obs.recording"] = _recording ? "true" : "false",
            ["obs.streaming"] = _streaming ? "true" : "false",
            ["obs.replayBuffer"] = _replayBuffer ? "true" : "false",
            ["obs.studioMode"] = _studioMode ? "true" : "false",
            ["obs.scene"] = _currentScene,
            ["obs.transition"] = _currentTransition,
            ["obs.profile"] = _currentProfile,
            ["obs.sceneCollection"] = _currentSceneCollection,
            ["obs.error"] = _lastError
        };

        foreach (var input in _inputs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            result["obs.input." + SafeId(input) + ".muted"] = _inputMuteStates.TryGetValue(input, out var muted) && muted ? "true" : "false";
        }

        foreach (var item in _sceneItems.GroupBy(x => SceneItemKey(x.SceneName, x.SourceName)).Select(g => g.First()))
        {
            var key = SceneItemKey(item.SceneName, item.SourceName);
            result["obs.source." + SafeId(item.SceneName) + "." + SafeId(item.SourceName) + ".visible"] = _sceneItemStates.TryGetValue(key, out var visible) && visible ? "true" : "false";
        }

        return result;
    }

    private static string[] SplitValue(string value)
    {
        return (value ?? "")
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static (string first, string second) ParseTwoPartValue(string value, string defaultSecond)
    {
        var parts = SplitValue(value);
        if (parts.Length == 0) return ("", defaultSecond);
        if (parts.Length == 1) return (parts[0], defaultSecond);
        return (parts[0], parts[1]);
    }

    private static string NormalizeMode(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        value = value.Trim().ToLowerInvariant();
        return value switch
        {
            "start" => "show",
            "stop" => "hide",
            "enable" => "show",
            "disable" => "hide",
            "visible" => "show",
            "hidden" => "hide",
            "muted" => "mute",
            "unmuted" => "unmute",
            _ => value
        };
    }

    private static string SceneItemKey(string sceneName, string sourceName) => sceneName + "\u001f" + sourceName;

    private static string SafeId(string value)
    {
        var sb = new StringBuilder();
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (ch == '_' || ch == '-' || ch == '.') sb.Append(ch);
            else sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }

    public void Dispose()
    {
        try { DisconnectAsync().Wait(500); } catch { }
        _requestLock.Dispose();
    }

    private sealed record ObsSceneItemRef(string SceneName, string SourceName, int SceneItemId);
}
