using Moka.Red.Core.Icons;
using Moka.Red.Icons;
using Mokaterm.UI.Common.Icons;

namespace Moka.Certificates.Components;

/// <summary>The icons the plugin draws beyond Mokaterm's, in the same style: one stroked path on a 24 by 24 grid.</summary>
public static class CertificateIcons
{
	/// <summary>A certificate: a page with a seal and its ribbon.</summary>
	public static readonly MokaIconDefinition Certificate = new("mc-certificate",
		"M4 4h16v11H13 M4 4v11h3 M8 8h8 M8 11h3 M14 16a2.5 2.5 0 1 1-5 0 2.5 2.5 0 0 1 5 0z M10 18v4l1.5-1 1.5 1v-4");

	public static readonly MokaIconDefinition Inspect = new("mc-inspect",
		"M11 18a7 7 0 1 1 0-14 7 7 0 0 1 0 14z M21 21l-5-5");

	public static readonly MokaIconDefinition Generate = new("mc-generate",
		"M12 3v4 M12 17v4 M3 12h4 M17 12h4 M5.6 5.6l2.8 2.8 M15.6 15.6l2.8 2.8 M5.6 18.4l2.8-2.8 M15.6 8.4l2.8-2.8");

	public static readonly MokaIconDefinition Convert = new("mc-convert",
		"M4 8h13l-3-3 M20 16H7l3 3");

	public static readonly MokaIconDefinition Sign = new("mc-sign",
		"M3 21c3 0 4-3 6-3s2 2 4 2 3-2 5-2 M14.5 3.5l3 3L9 15l-4 1 1-4z");

	public static readonly MokaIconDefinition Encrypt = MokatermIcons.Lock;

	public static readonly MokaIconDefinition Server = MokatermIcons.Server;

	public static readonly MokaIconDefinition Key = MokatermIcons.Key;

	public static readonly MokaIconDefinition File = MokaIcons.File.Document;
}
