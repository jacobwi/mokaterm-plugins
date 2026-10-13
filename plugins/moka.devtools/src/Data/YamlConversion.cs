namespace Moka.DevTools.Data;

/// <summary>A conversion's text and what the reader should know about how it was made.</summary>
public sealed record YamlConversion(string Text, IReadOnlyList<string> Notes);
