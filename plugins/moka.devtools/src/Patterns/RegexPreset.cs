namespace Moka.DevTools.Patterns;

/// <summary>One ready-made pattern: a name for the picker, a one-line description of what it matches, and the pattern.</summary>
public sealed record RegexPreset(string Name, string Description, string Pattern);
