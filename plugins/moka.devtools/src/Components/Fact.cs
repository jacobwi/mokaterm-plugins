namespace Moka.DevTools.Components;

/// <summary>One labeled fact in a details list, shown in mono when it is data, with a copy button when it is worth one.</summary>
public sealed record Fact(string Label, string Value, bool Mono = true, bool Copyable = false, string? Note = null);
