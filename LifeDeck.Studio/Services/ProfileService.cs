using System.IO;
using System.Text.Json;
using LifeDeck.Studio.Models;

namespace LifeDeck.Studio.Services;

public class ProfileService
{
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public DeckProfile CreateDemoProfile()
    {
        var p = new DeckProfile { Name = "Default" };
        p.Pages.Add(CreatePage("Streaming", new[] { "Gaming", "Just Chatting", "Mic Mute", "OBS", "Record", "Stream", "BRB", "Chat", "Discord", "Twitch", "Marker", "Clip" }));

        var discord = new DeckPage { Title = "Discord" };
        discord.Buttons.Add(new DeckButton { Title = "Mic", Subtitle = "Unmuted", Color = "#1F6F3A", Plugin = "discord", Action = "discord.muteToggle", Value = "Ctrl+Shift+M" });
        discord.Buttons.Add(new DeckButton { Title = "Sound", Subtitle = "Undeafened", Color = "#1F6F3A", Plugin = "discord", Action = "discord.deafenToggle", Value = "Ctrl+Shift+D" });
        discord.Buttons.Add(new DeckButton { Title = "Leave", Subtitle = "Voice", Color = "#7A1F1F", Plugin = "discord", Action = "discord.voiceDisconnect", Value = "Ctrl+Shift+L" });
        for (int i = 4; i <= 12; i++) discord.Buttons.Add(new DeckButton { Title = "Discord " + i, Color = "#5865F2" });
        p.Pages.Add(discord);

        p.Pages.Add(CreatePage("Twitch", new[] { "Marker", "Clip", "Ad", "Title", "Chat", "Emote", "Raid", "Poll", "Prediction", "Shoutout", "Mod", "Dashboard" }));
        return p;
    }

    private DeckPage CreatePage(string title, IEnumerable<string> names)
    {
        var page = new DeckPage { Title = title };
        var colors = new[] { "#3949AB", "#00897B", "#6D4C41", "#C62828", "#5E35B1", "#2E7D32" };
        int i = 0;
        foreach (var n in names)
        {
            page.Buttons.Add(new DeckButton { Title = n, Color = colors[i % colors.Length] });
            i++;
        }
        return page;
    }

    public void Save(string path, DeckProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(profile, _jsonOptions));
    }

    public DeckProfile Load(string path)
    {
        return JsonSerializer.Deserialize<DeckProfile>(File.ReadAllText(path)) ?? CreateDemoProfile();
    }
}
