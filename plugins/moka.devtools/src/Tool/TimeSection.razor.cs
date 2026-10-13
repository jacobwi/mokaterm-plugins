using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Moka.DevTools.Components;
using Moka.DevTools.Dates;
using Moka.DevTools.Text;
using Mokaterm.UI.Common.Components;

namespace Moka.DevTools.Tool;

/// <summary>
/// A Unix time (unit guessed from its size or picked) or a written date, shown in UTC, local time, every Unix unit,
/// the usual text forms and any IANA zones added, with how far it is from now.
/// </summary>
public sealed partial class TimeSection : DevToolViewBase
{
	private static readonly IReadOnlyList<EnumOption<EpochChoice>> Units =
	[
		new(EpochChoice.Auto, "Auto"),
		new(EpochChoice.Seconds, "s"),
		new(EpochChoice.Milliseconds, "ms"),
		new(EpochChoice.Microseconds, "µs"),
		new(EpochChoice.Nanoseconds, "ns"),
	];

	private readonly List<(string Id, TimeZoneInfo Zone)> _zones = [];
	private string _text = "";
	private EpochChoice _unit;
	private TimeReading? _reading;
	private string? _error;
	private string? _zonePick;
	private string _zoneText = "";
	private string? _zoneError;

	private string Summary => _reading is { } reading ? reading.Source : "Unix time and dates";

	private static string ReadAs(TimeReading reading) => reading.Unit is { } unit
		? reading.UnitReason is { } reason ? $"Read as {TimeParser.UnitName(unit)}, from its {reason}." : $"Read as {TimeParser.UnitName(unit)}."
		: reading.WrittenOffset is { } offset ? $"Read as {reading.Source}, offset {TimeFormats.Offset(offset)}." : $"Read as {reading.Source}.";

	private IReadOnlyList<Fact> Facts(DateTimeOffset instant)
	{
		DateTime utc = instant.UtcDateTime;
		return
		[
			new("UTC", TimeFormats.IsoUtc(instant), Copyable: true),
			new("Local", TimeFormats.InZone(instant, TimeZoneInfo.Local), Copyable: true, Note: TimeFormats.ZoneName(instant, TimeZoneInfo.Local)),
			new("From now", RelativeTime.Describe(instant, Now), Mono: false),
			new("Unix s", TimeFormats.UnixSeconds(instant), Copyable: true),
			new("Unix ms", TimeFormats.UnixMilliseconds(instant), Copyable: true),
			new("Unix µs", TimeFormats.UnixMicroseconds(instant), Copyable: true),
			new("Unix ns", TimeFormats.UnixNanoseconds(instant), Copyable: true),
			new("RFC 3339", TimeFormats.IsoUtc(instant), Copyable: true),
			new("RFC 1123", TimeFormats.Rfc1123(instant), Copyable: true),
			new("Day", string.Create(CultureInfo.InvariantCulture,
				$"{utc.DayOfWeek}, day {utc.DayOfYear} of {utc.Year}, ISO week {ISOWeek.GetWeekOfYear(utc)} (UTC)"), Mono: false),
		];
	}

	private void SetNow()
	{
		_unit = EpochChoice.Auto;
		_text = Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
		Read();
	}

	private void OnTextChanged(string? value)
	{
		_text = value ?? "";
		Read();
	}

	private void OnUnitChanged(EpochChoice unit)
	{
		_unit = unit;
		Read();
	}

	private void Read()
	{
		_reading = null;
		_error = null;
		if (_text.Trim().Length == 0)
		{
			return;
		}

		EpochUnit? unit = _unit switch
		{
			EpochChoice.Seconds => EpochUnit.Seconds,
			EpochChoice.Milliseconds => EpochUnit.Milliseconds,
			EpochChoice.Microseconds => EpochUnit.Microseconds,
			EpochChoice.Nanoseconds => EpochUnit.Nanoseconds,
			_ => null,
		};

		try
		{
			_reading = TimeParser.Read(_text, unit);
		}
		catch (FormatException ex)
		{
			_error = ex.Message;
		}
	}

	private void AddPicked(string? id)
	{
		_zonePick = null;
		if (id is not null)
		{
			Add(id);
		}
	}

	private void AddTyped()
	{
		if (Add(_zoneText))
		{
			_zoneText = "";
		}
	}

	private void OnZoneKey(KeyboardEventArgs args)
	{
		if (args.Key == "Enter")
		{
			AddTyped();
		}
	}

	private bool Add(string id)
	{
		_zoneError = null;
		if (!ZoneCatalog.TryFind(id, out TimeZoneInfo? zone, out string? ianaId))
		{
			_zoneError = $"No time zone is called {id.Trim()}. Names look like Europe/Berlin or America/New_York.";
			return false;
		}

		if (!_zones.Exists(entry => string.Equals(entry.Id, ianaId, StringComparison.Ordinal)))
		{
			_zones.Add((ianaId, zone));
		}

		return true;
	}

	private void Remove(string id) => _zones.RemoveAll(entry => string.Equals(entry.Id, id, StringComparison.Ordinal));
}
