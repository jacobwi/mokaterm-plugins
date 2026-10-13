using System.Diagnostics.CodeAnalysis;

namespace Moka.DevTools.Dates;

/// <summary>
/// IANA time zone names. .NET finds any IANA name on every platform (through ICU on Windows), but only lists the
/// platform's own zones, so the list offered is those zones under their IANA names and any other name can be typed.
/// </summary>
public static class ZoneCatalog
{
	private static readonly Lazy<IReadOnlyList<string>> Known = new(List);

	/// <summary>The IANA names this machine lists, sorted.</summary>
	public static IReadOnlyList<string> Ids => Known.Value;

	/// <summary>The zone for an IANA name such as Europe/Berlin; a Windows name is taken too and reported under its IANA name.</summary>
	public static bool TryFind(string id, [NotNullWhen(true)] out TimeZoneInfo? zone, [NotNullWhen(true)] out string? ianaId)
	{
		ArgumentNullException.ThrowIfNull(id);
		zone = null;
		ianaId = null;
		string name = id.Trim();
		if (name.Length == 0)
		{
			return false;
		}

		try
		{
			zone = TimeZoneInfo.FindSystemTimeZoneById(name);
		}
		catch (TimeZoneNotFoundException)
		{
			return false;
		}
		catch (InvalidTimeZoneException)
		{
			return false;
		}

		ianaId = zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string? converted) ? converted : zone.Id;
		return true;
	}

	private static IReadOnlyList<string> List()
	{
		SortedSet<string> ids = new(StringComparer.Ordinal) { "UTC" };
		foreach (TimeZoneInfo zone in TimeZoneInfo.GetSystemTimeZones())
		{
			if (zone.HasIanaId)
			{
				ids.Add(zone.Id);
			}
			else if (TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string? iana))
			{
				ids.Add(iana);
			}
		}

		return [.. ids];
	}
}
