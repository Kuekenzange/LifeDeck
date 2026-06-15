using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace LifeDeck.Studio.Models;

public class DeckProfile
{
    public string Name { get; set; } = "Default";
    public ObservableCollection<DeckPage> Pages { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> PluginSettings { get; set; } = new();
}

public class DeckPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Neue Seite";
    public ObservableCollection<DeckButton> Buttons { get; set; } = new();

    public override string ToString() => Title;
}

public class DeckButton
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Button";
    public string Subtitle { get; set; } = "";
    public string Color { get; set; } = "#333333";
    public string IconPath { get; set; } = "";
    // text, imageText, image
    public string DisplayMode { get; set; } = "imageText";
    public string Plugin { get; set; } = "none";
    public string Action { get; set; } = "None";
    public string Value { get; set; } = "";

    // Normal Mode: simple state mappings that casual users can configure.
    public ObservableCollection<ButtonStateRule> StateRules { get; set; } = new();

    // Advanced Mode: node-style rules. Stored separately so the UI can expose them later
    // without changing the plugin/core data model again.
    public ObservableCollection<NodeRule> Nodes { get; set; } = new();
}

public class ButtonVisualState
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Color { get; set; } = "";
    public string IconPath { get; set; } = "";
}

public class ButtonStateRule
{
    public string Name { get; set; } = "Neue Regel";
    public string SourcePlugin { get; set; } = "none";
    public string EventName { get; set; } = "";
    public string ExpectedValue { get; set; } = "true";
    public ButtonVisualState Apply { get; set; } = new();

    public override string ToString() => string.IsNullOrWhiteSpace(EventName) ? Name : $"{Name} · {EventName} == {ExpectedValue}";
}

public class NodeRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Advanced Node";
    public string TriggerPlugin { get; set; } = "none";
    public string TriggerEvent { get; set; } = "";
    public string ConditionOperator { get; set; } = "==";
    public string ConditionValue { get; set; } = "true";
    public string Target { get; set; } = "button.visual";
    public ButtonVisualState Apply { get; set; } = new();

    public override string ToString() => $"{Name}: {TriggerEvent} {ConditionOperator} {ConditionValue}";
}
