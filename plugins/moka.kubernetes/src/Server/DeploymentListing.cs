namespace Moka.Kubernetes.Server;

/// <summary>
/// One deployment as <c>kubectl get deployments --no-headers</c> prints it: how many of its pods stand ready out of
/// how many it wants, how many run the newest revision, how many are available, and its age.
/// </summary>
public sealed record DeploymentRow(
	string Namespace,
	string Name,
	string Ready,
	string UpToDate,
	string Available,
	string Age)
{
	/// <summary>True when every pod the deployment wants stands ready, which "3/3" says and "2/3" does not.</summary>
	public bool FullyReady => Ready.Split('/') is { Length: 2 } parts &&
		int.TryParse(parts[0], out int ready) &&
		int.TryParse(parts[1], out int wanted) &&
		ready == wanted;
}

/// <summary>
/// Reads the deployment listing. kubectl promises five plain columns, or six with a namespace first when every
/// namespace was asked for, and pads none of them with a space, so a line either holds the shape or is skipped.
/// </summary>
public static class DeploymentListing
{
	public static IReadOnlyList<DeploymentRow> Parse(string text, bool hasNamespace)
	{
		List<DeploymentRow> rows = [];
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("NAME ", StringComparison.Ordinal) || trimmed.StartsWith("NAMESPACE ", StringComparison.Ordinal))
			{
				continue;
			}

			string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			int least = hasNamespace ? 6 : 5;
			if (parts.Length < least || parts[0] == "NAME")
			{
				continue;
			}

			int at = 0;
			string space = hasNamespace ? parts[at++] : "";
			rows.Add(new DeploymentRow(space, parts[at], parts[at + 1], parts[at + 2], parts[at + 3], parts[at + 4]));
		}

		return rows;
	}
}
