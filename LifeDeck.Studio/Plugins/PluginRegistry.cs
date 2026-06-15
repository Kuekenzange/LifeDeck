namespace LifeDeck.Studio.Plugins;

public class PluginRegistry
{
    public List<IActionPlugin> Plugins { get; } = new();

    public PluginRegistry()
    {
        Plugins.Add(new NoActionPlugin());
        Plugins.Add(new KeyboardPlugin());
        Plugins.Add(new SystemPlugin());
        Plugins.Add(new SoundboardPlugin());
        Plugins.Add(new StreamerBotPlugin());
        Plugins.Add(new DiscordPlugin());
        Plugins.Add(new ObsPlugin());
        Plugins.Add(new TwitchPlugin());
    }

    public IActionPlugin? Find(string id) => Plugins.FirstOrDefault(p => p.Id == id);
}

public class NoActionPlugin : IActionPlugin
{
    public string Id => "none";
    public string DisplayName => "Keine Aktion";
    public IReadOnlyList<PluginActionDefinition> Actions { get; } = new[] { new PluginActionDefinition { Id = "none", DisplayName = "Keine Aktion" } };
    public IReadOnlyList<PluginEventDefinition> Events { get; } = Array.Empty<PluginEventDefinition>();
    public Task ExecuteAsync(string action, string value) => Task.CompletedTask;
}
