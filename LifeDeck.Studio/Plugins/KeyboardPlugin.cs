namespace LifeDeck.Studio.Plugins;

public class KeyboardPlugin : IActionPlugin
{
    public string Id => "keyboard";
    public string DisplayName => "Keyboard / Hotkeys";
    public IReadOnlyList<PluginActionDefinition> Actions { get; } = new[]
    {
        new PluginActionDefinition { Id = "keyboard.hotkey", DisplayName = "Hotkey", Hint = "z.B. Ctrl+Shift+M", SuggestedValues = new[] { "Ctrl+Shift+M", "Ctrl+Shift+D", "Ctrl+Shift+L", "Alt+Tab" } },
        new PluginActionDefinition { Id = "keyboard.text", DisplayName = "Text schreiben", Hint = "Text" }
    };

    public IReadOnlyList<PluginEventDefinition> Events { get; } = Array.Empty<PluginEventDefinition>();

    public Task ExecuteAsync(string action, string value)
    {
        if (action == "keyboard.hotkey" || string.Equals(action, "Hotkey", StringComparison.OrdinalIgnoreCase))
            HotkeySender.SendHotkey(value);
        else if (action == "keyboard.text" || string.Equals(action, "Text schreiben", StringComparison.OrdinalIgnoreCase))
            HotkeySender.SendText(value);

        return Task.CompletedTask;
    }
}
