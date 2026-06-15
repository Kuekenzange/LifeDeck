namespace LifeDeck.Studio.Plugins;

public class DiscordPlugin : IActionPlugin, IStatefulPlugin, IEventStateProvider
{
    private bool _estimatedMuted;
    private bool _estimatedDeafened;
    private bool _estimatedStreamerMode;
    private bool _estimatedOverlayLocked;

    public string MuteHotkey { get; set; } = "Ctrl+Shift+M";
    public string DeafenHotkey { get; set; } = "Ctrl+Shift+D";
    public string StreamerModeHotkey { get; set; } = "Ctrl+Shift+S";
    public string OverlayLockHotkey { get; set; } = "Ctrl+Shift+O";
    public string LeaveVoiceHotkey { get; set; } = "Ctrl+Shift+L";
    public string AcceptCallHotkey { get; set; } = "Ctrl+Shift+A";
    public string NavigateToCallHotkey { get; set; } = "Ctrl+Shift+J";
    public string JoinPresetVoiceHotkey { get; set; } = "Ctrl+Shift+V";

    public string Id => "discord";
    public string DisplayName => "Discord";

    public IReadOnlyList<PluginActionDefinition> Actions { get; } = new[]
    {
        new PluginActionDefinition {
            Id = "discord.muteToggle",
            DisplayName = "Mute Toggle",
            Hint = "Manueller Discord-Hotkey für Mikrofon-Stummschaltung. Toggle mit geschätztem Status.",
            SuggestedValues = new[] { "", "Ctrl+Shift+M", "Ctrl+Alt+M", "Benutzerdefiniert" }
        },
        new PluginActionDefinition {
            Id = "discord.deafenToggle",
            DisplayName = "Deafen Toggle",
            Hint = "Manueller Discord-Hotkey für Deafen. Toggle mit geschätztem Status.",
            SuggestedValues = new[] { "", "Ctrl+Shift+D", "Ctrl+Alt+D", "Benutzerdefiniert" }
        },
        new PluginActionDefinition {
            Id = "discord.streamerModeToggle",
            DisplayName = "Streamer-Modus Toggle",
            Hint = "Manueller Discord-Hotkey für Streamer-Modus. Toggle mit geschätztem Status.",
            SuggestedValues = new[] { "", "Ctrl+Shift+S", "Ctrl+Alt+S", "Benutzerdefiniert" }
        },
        new PluginActionDefinition {
            Id = "discord.overlayLockToggle",
            DisplayName = "Overlay-Sperre Toggle",
            Hint = "Manueller Discord-Hotkey für Overlay-Sperre. Toggle mit geschätztem Status.",
            SuggestedValues = new[] { "", "Ctrl+Shift+O", "Ctrl+Alt+O", "Benutzerdefiniert" }
        },
        new PluginActionDefinition {
            Id = "discord.voiceDisconnect",
            DisplayName = "Voice verlassen",
            Hint = "Manueller Discord-Hotkey zum Sprachkanal verlassen. Einmalige Aktion ohne Toggle-Status.",
            SuggestedValues = new[] { "", "Ctrl+Shift+L", "Ctrl+Alt+L", "Benutzerdefiniert" }
        },
        new PluginActionDefinition {
            Id = "discord.acceptCall",
            DisplayName = "Anruf annehmen",
            Hint = "Manueller Discord-Hotkey zum Annehmen eines Anrufs. Einmalige Aktion ohne Toggle-Status.",
            SuggestedValues = new[] { "", "Ctrl+Shift+A", "Ctrl+Alt+A", "Benutzerdefiniert" }
        },
        new PluginActionDefinition {
            Id = "discord.navigateToCall",
            DisplayName = "Zum aktuellen Anruf",
            Hint = "Manueller Discord-Hotkey zum Öffnen/Navigieren zum aktuellen Call. Einmalige Aktion ohne Toggle-Status.",
            SuggestedValues = new[] { "", "Ctrl+Shift+J", "Ctrl+Alt+J", "Benutzerdefiniert" }
        },
        new PluginActionDefinition {
            Id = "discord.joinPresetVoice",
            DisplayName = "Vorgewähltem Sprachkanal beitreten",
            Hint = "Manueller Discord-Hotkey oder Makro zum Beitreten eines vorgewählten Sprachkanals. Einmalige Aktion ohne Toggle-Status.",
            SuggestedValues = new[] { "", "Ctrl+Shift+V", "Ctrl+Alt+V", "Benutzerdefiniert" }
        },
        new PluginActionDefinition {
            Id = "discord.resetEstimatedStates",
            DisplayName = "Geschätzte Status zurücksetzen",
            Hint = "Setzt die geschätzten Toggle-Zustände für Mute, Deafen, Streamer-Modus und Overlay-Sperre zurück.",
            SuggestedValues = new[] { "off", "on", "Benutzerdefiniert" }
        }
    };

    public IReadOnlyList<PluginSettingDefinition> Settings { get; } = new[]
    {
        new PluginSettingDefinition { Key = "muteHotkey", DisplayName = "Mute Toggle", Hint = "Discord-Hotkey für Mikrofon stummschalten.", SuggestedValues = new[] { "Ctrl+Shift+M", "Ctrl+Alt+M", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "deafenHotkey", DisplayName = "Deafen Toggle", Hint = "Discord-Hotkey für Deafen.", SuggestedValues = new[] { "Ctrl+Shift+D", "Ctrl+Alt+D", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "streamerModeHotkey", DisplayName = "Streamer-Modus Toggle", Hint = "Discord-Hotkey für Streamer-Modus.", SuggestedValues = new[] { "Ctrl+Shift+S", "Ctrl+Alt+S", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "overlayLockHotkey", DisplayName = "Overlay-Sperre Toggle", Hint = "Discord-Hotkey für Overlay-Sperre.", SuggestedValues = new[] { "Ctrl+Shift+O", "Ctrl+Alt+O", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "leaveVoiceHotkey", DisplayName = "Voice verlassen", Hint = "Discord-Hotkey zum Sprachkanal verlassen.", SuggestedValues = new[] { "Ctrl+Shift+L", "Ctrl+Alt+L", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "acceptCallHotkey", DisplayName = "Anruf annehmen", Hint = "Discord-Hotkey zum Anruf annehmen.", SuggestedValues = new[] { "Ctrl+Shift+A", "Ctrl+Alt+A", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "navigateToCallHotkey", DisplayName = "Zum aktuellen Anruf", Hint = "Discord-Hotkey zum aktuellen Call navigieren.", SuggestedValues = new[] { "Ctrl+Shift+J", "Ctrl+Alt+J", "Benutzerdefiniert" } },
        new PluginSettingDefinition { Key = "joinPresetVoiceHotkey", DisplayName = "Vorgewähltem Sprachkanal beitreten", Hint = "Discord-Hotkey oder Makro zum Beitreten eines festgelegten Sprachkanals.", SuggestedValues = new[] { "Ctrl+Shift+V", "Ctrl+Alt+V", "Benutzerdefiniert" } }
    };

    public string GetSettingValue(string key) => key switch
    {
        "muteHotkey" => MuteHotkey,
        "deafenHotkey" => DeafenHotkey,
        "streamerModeHotkey" => StreamerModeHotkey,
        "overlayLockHotkey" => OverlayLockHotkey,
        "leaveVoiceHotkey" => LeaveVoiceHotkey,
        "acceptCallHotkey" => AcceptCallHotkey,
        "navigateToCallHotkey" => NavigateToCallHotkey,
        "joinPresetVoiceHotkey" => JoinPresetVoiceHotkey,
        _ => ""
    };

    public void SetSettingValue(string key, string value)
    {
        switch (key)
        {
            case "muteHotkey": MuteHotkey = value; break;
            case "deafenHotkey": DeafenHotkey = value; break;
            case "streamerModeHotkey": StreamerModeHotkey = value; break;
            case "overlayLockHotkey": OverlayLockHotkey = value; break;
            case "leaveVoiceHotkey": LeaveVoiceHotkey = value; break;
            case "acceptCallHotkey": AcceptCallHotkey = value; break;
            case "navigateToCallHotkey": NavigateToCallHotkey = value; break;
            case "joinPresetVoiceHotkey": JoinPresetVoiceHotkey = value; break;
        }
    }

    public IReadOnlyList<PluginEventDefinition> Events { get; } = new[]
    {
        new PluginEventDefinition { Id = "discord.muted", DisplayName = "Muted", ValueHint = "true/false (geschätzt)", SuggestedValues = new[] { "true", "false" } },
        new PluginEventDefinition { Id = "discord.deafened", DisplayName = "Deafened", ValueHint = "true/false (geschätzt)", SuggestedValues = new[] { "true", "false" } },
        new PluginEventDefinition { Id = "discord.streamerMode", DisplayName = "Streamer-Modus", ValueHint = "true/false (geschätzt)", SuggestedValues = new[] { "true", "false" } },
        new PluginEventDefinition { Id = "discord.overlayLocked", DisplayName = "Overlay-Sperre", ValueHint = "true/false (geschätzt)", SuggestedValues = new[] { "true", "false" } },
        new PluginEventDefinition { Id = "discord.statusSource", DisplayName = "Statusquelle", ValueHint = "estimated", SuggestedValues = new[] { "estimated" } }
    };

    public Task ExecuteAsync(string action, string value)
    {
        var hotkey = ResolveHotkey(action, value);

        if (!string.IsNullOrWhiteSpace(hotkey))
            HotkeySender.SendHotkey(hotkey);

        switch (NormalizeAction(action))
        {
            case "discord.muteToggle":
                _estimatedMuted = !_estimatedMuted;
                break;
            case "discord.deafenToggle":
                _estimatedDeafened = !_estimatedDeafened;
                break;
            case "discord.streamerModeToggle":
                _estimatedStreamerMode = !_estimatedStreamerMode;
                break;
            case "discord.overlayLockToggle":
                _estimatedOverlayLocked = !_estimatedOverlayLocked;
                break;
            case "discord.voiceDisconnect":
                _estimatedMuted = false;
                _estimatedDeafened = false;
                break;
            case "discord.resetEstimatedStates":
                ResetEstimatedStates(value);
                break;
        }

        return Task.CompletedTask;
    }

    public PluginButtonState GetState(string action, string value)
    {
        return NormalizeAction(action) switch
        {
            "discord.muteToggle" => new PluginButtonState
            {
                Subtitle = _estimatedMuted ? "Muted" : "Unmuted",
                Color = _estimatedMuted ? "#7A1F1F" : "#1F6F3A"
            },
            "discord.deafenToggle" => new PluginButtonState
            {
                Subtitle = _estimatedDeafened ? "Deafened" : "Undeafened",
                Color = _estimatedDeafened ? "#7A1F1F" : "#1F6F3A"
            },
            "discord.streamerModeToggle" => new PluginButtonState
            {
                Subtitle = _estimatedStreamerMode ? "Streamer an" : "Streamer aus",
                Color = _estimatedStreamerMode ? "#5B2A86" : "#333333"
            },
            "discord.overlayLockToggle" => new PluginButtonState
            {
                Subtitle = _estimatedOverlayLocked ? "Overlay gesperrt" : "Overlay frei",
                Color = _estimatedOverlayLocked ? "#7A4A1F" : "#333333"
            },
            _ => new PluginButtonState()
        };
    }

    public IReadOnlyDictionary<string, string> GetCurrentEvents()
    {
        return new Dictionary<string, string>
        {
            ["discord.muted"] = _estimatedMuted ? "true" : "false",
            ["discord.deafened"] = _estimatedDeafened ? "true" : "false",
            ["discord.streamerMode"] = _estimatedStreamerMode ? "true" : "false",
            ["discord.overlayLocked"] = _estimatedOverlayLocked ? "true" : "false",
            ["discord.statusSource"] = "estimated"
        };
    }

    private string ResolveHotkey(string action, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && value != "Benutzerdefiniert" && NormalizeAction(action) != "discord.resetEstimatedStates")
            return value;

        return NormalizeAction(action) switch
        {
            "discord.muteToggle" => MuteHotkey,
            "discord.deafenToggle" => DeafenHotkey,
            "discord.streamerModeToggle" => StreamerModeHotkey,
            "discord.overlayLockToggle" => OverlayLockHotkey,
            "discord.voiceDisconnect" => LeaveVoiceHotkey,
            "discord.acceptCall" => AcceptCallHotkey,
            "discord.navigateToCall" => NavigateToCallHotkey,
            "discord.joinPresetVoice" => JoinPresetVoiceHotkey,
            _ => ""
        };
    }

    private static string NormalizeAction(string action)
    {
        return action switch
        {
            "Mute Toggle" => "discord.muteToggle",
            "Deafen Toggle" => "discord.deafenToggle",
            "Streamer-Modus Toggle" => "discord.streamerModeToggle",
            "Overlay-Sperre Toggle" => "discord.overlayLockToggle",
            "Voice verlassen" or "Voice Disconnect" => "discord.voiceDisconnect",
            "Anruf annehmen" => "discord.acceptCall",
            "Zum aktuellen Anruf" => "discord.navigateToCall",
            "Vorgewähltem Sprachkanal beitreten" => "discord.joinPresetVoice",
            "Geschätzte Status zurücksetzen" => "discord.resetEstimatedStates",
            _ => action
        };
    }

    private void ResetEstimatedStates(string value)
    {
        var state = value?.Trim().ToLowerInvariant();

        if (state == "on" || state == "true")
        {
            _estimatedMuted = true;
            _estimatedDeafened = true;
            _estimatedStreamerMode = true;
            _estimatedOverlayLocked = true;
            return;
        }

        _estimatedMuted = false;
        _estimatedDeafened = false;
        _estimatedStreamerMode = false;
        _estimatedOverlayLocked = false;
    }
}
