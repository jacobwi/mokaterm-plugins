namespace Moka.Kubernetes.Server;

/// <summary>
/// One node as <c>kubectl get nodes --no-headers</c> prints it: the status kubectl words (<c>Ready</c>,
/// <c>NotReady</c>, <c>Ready,SchedulingDisabled</c>), its roles, its age and its kubelet's version.
/// </summary>
public sealed record NodeRow(string Name, string Status, string Roles, string Age, string Version)
{
	/// <summary>True when the status word starts with Ready, which SchedulingDisabled keeps: the node works, it just takes no pods.</summary>
	public bool Ready => Status.StartsWith("Ready", StringComparison.Ordinal);
}

/// <summary>
/// Reads the node listing. kubectl promises five columns and never pads one with a space, so a line is read when it
/// holds them and skipped when it does not, header and noise included, exactly as the server told it.
/// </summary>
public static class NodeListing
{
	public static IReadOnlyList<NodeRow> Parse(string text)
	{
		List<NodeRow> rows = [];
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("NAME ", StringComparison.Ordinal))
			{
				continue;
			}

			string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length < 5 || parts[0] == "NAME")
			{
				continue;
			}

			rows.Add(new NodeRow(parts[0], parts[1], parts[2], parts[3], parts[4]));
		}

		return rows;
	}
}
