namespace Moka.DevTools.Patterns;

/// <summary>What Mokaterm's terminal triggers make of the pattern, when the page could ask them.</summary>
public sealed record RegexTriggerCheck(bool Refused, string? Reason);
