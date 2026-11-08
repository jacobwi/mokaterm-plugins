namespace Moka.DevTools.Dates;

/// <summary>A zone added to the Time section's list, under the IANA name it was found by.</summary>
public sealed record ZoneEntry(string Id, TimeZoneInfo Zone);
