using System.Text.Json;

namespace Mokaterm.Gallery.LoadCheck;

/// <summary>What build/gallery_build.py reads back: every host the plugin was loaded for, and what went wrong.</summary>
internal sealed record LoadCheckReport(
	string HostSdk,
	bool Ok,
	string? ModuleId,
	IReadOnlyList<HostReport> Hosts,
	IReadOnlyList<string> Problems,
	IReadOnlyList<string> Warnings)
{
	public static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
	};
}

/// <summary>One load: an operator's folder (web host, or a desktop folder put in place by hand) or a desktop install.</summary>
internal sealed record HostReport(string Host, string Mode, string Status, string? Problem, IReadOnlyList<string> Registrations);
