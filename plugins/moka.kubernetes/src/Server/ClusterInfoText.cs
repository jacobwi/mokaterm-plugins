namespace Moka.Kubernetes.Server;

/// <summary>One service <c>kubectl cluster-info</c> names: what it is, and the URL it answers at.</summary>
/// <param name="Name">As kubectl said it: "Kubernetes control plane", "CoreDNS".</param>
/// <param name="Url">Where it answers, such as <c>https://192.0.2.10:6443</c>.</param>
public sealed record ClusterService(string Name, string Url);

/// <summary>
/// Reads what <c>kubectl cluster-info</c> prints: a line per service, each "<c>NAME is running at URL</c>", with
/// kubectl's advice about debugging below, which is not a service and does not parse as one. A URL that does not
/// start with http is not a URL kubectl meant, so the line is left alone.
/// </summary>
public static class ClusterInfoText
{
	public static IReadOnlyList<ClusterService> Parse(string text)
	{
		List<ClusterService> services = [];
		foreach (string line in text.Split('\n'))
		{
			if (ReadLine(line.Trim()) is { } service)
			{
				services.Add(service);
			}
		}

		return services;
	}

	/// <summary>One line of it, or null when the line is not a service at a URL.</summary>
	internal static ClusterService? ReadLine(string line)
	{
		const string Join = " is running at ";
		int at = line.IndexOf(Join, StringComparison.Ordinal);
		if (at <= 0)
		{
			return null;
		}

		string name = line[..at].Trim();
		string url = line[(at + Join.Length)..].Trim();
		if (name.Length == 0 || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return new ClusterService(name, url);
	}
}
