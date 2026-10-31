namespace Moka.DevTools.Patterns;

/// <summary>
/// A handful of ready-made patterns for the picker in the Regex section. Each one is pragmatic: it matches what people
/// actually write, without the full rules of the RFC that governs the thing (emails accept far more than these
/// patterns; an IPv4 part here can be 999). They are for everyday searching and highlighting, not for validation that
/// anything turns on.
/// </summary>
public static class RegexPresets
{
	/// <summary>Every preset, in the order the picker shows them.</summary>
	public static readonly IReadOnlyList<RegexPreset> All =
	[
		new("IPv4 address", "A dotted quad, 1 to 3 digits in each part; the parts are not range-checked.", @"\b(?:\d{1,3}\.){3}\d{1,3}\b"),
		new("IPv6 address", "A full eight-group IPv6 address; the :: shortened form does not match.", @"\b(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}\b"),
		new("Email address", "An address in the usual shape; pragmatic, not the whole of RFC 5322.", @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b"),
		new("HTTP(S) URL", "An http or https URL, up to the next space or quote.", @"https?://[^\s<>""']+"),
		new("ISO 8601 date", "A date with an optional time and zone; year, month and day are named groups.", @"\b(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?\b"),
		new("UUID", "A UUID in the hyphenated 8-4-4-4-12 form, any version, either case.", @"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b"),
		new("Hex colour", "A colour as #rgb or #rrggbb.", @"#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})\b"),
		new("MAC address", "A MAC address with colons or dashes.", @"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b"),
		new("JWT", "A JWT: header and payload both start eyJ, since each is base64url of JSON, then the signature.", @"eyJ[A-Za-z0-9_-]*\.eyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]*"),
		new("Semantic version", "A semver with an optional pre-release and build; major, minor and patch are named groups.", @"\b(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\b"),
		new("Decimal number", "A whole or decimal number with an optional sign, not part of a longer run of digits; one named group.", @"(?<!\d)(?<number>-?\d+(?:\.\d+)?)(?!\d)"),
		new("Cron schedule", "A five-field cron schedule of numbers, ranges, steps and stars; each field is a named group.", @"(?<minute>\*|\d+(?:-\d+)?(?:/\d+)?)[ \t]+(?<hour>\*|\d+(?:-\d+)?(?:/\d+)?)[ \t]+(?<day>\*|\d+(?:-\d+)?(?:/\d+)?)[ \t]+(?<month>\*|\d+(?:-\d+)?(?:/\d+)?)[ \t]+(?<weekday>\*|\d+(?:-\d+)?(?:/\d+)?)"),
	];
}
