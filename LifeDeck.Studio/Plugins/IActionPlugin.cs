namespace LifeDeck.Studio.Plugins;

public interface IActionPlugin
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<PluginActionDefinition> Actions { get; }
    IReadOnlyList<PluginEventDefinition> Events { get; }
    IReadOnlyList<PluginSettingDefinition> Settings => Array.Empty<PluginSettingDefinition>();
    string GetSettingValue(string key) => "";
    void SetSettingValue(string key, string value) { }
    Task ExecuteAsync(string action, string value);
}

public sealed class PluginActionDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Hint { get; set; } = "";
    public IReadOnlyList<string> SuggestedValues { get; set; } = Array.Empty<string>();
    public string BrowseFilter { get; set; } = "";

    public override string ToString() => DisplayName;
}

public sealed class PluginSettingDefinition
{
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Hint { get; set; } = "";
    public string Type { get; set; } = "text";
    public IReadOnlyList<string> SuggestedValues { get; set; } = Array.Empty<string>();
    public string BrowseFilter { get; set; } = "";

    public override string ToString() => DisplayName;
}

public sealed class PluginEventDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string ValueHint { get; set; } = "true/false";
    public IReadOnlyList<string> SuggestedValues { get; set; } = new[] { "true", "false" };

    public override string ToString() => DisplayName;
}

public interface IStatefulPlugin
{
    PluginButtonState GetState(string action, string value);
}

public interface IEventStateProvider
{
    IReadOnlyDictionary<string, string> GetCurrentEvents();
}

public sealed class PluginButtonState
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Color { get; set; }
    public string? IconPath { get; set; }
}
