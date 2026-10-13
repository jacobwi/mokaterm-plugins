namespace Moka.DevTools.Data;

/// <summary>What a valid document holds at its top ("an object with 12 keys"), and what is legal but likely a mistake.</summary>
public sealed record JsonSummary(string Description, IReadOnlyList<string> Warnings);
