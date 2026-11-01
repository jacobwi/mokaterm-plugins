namespace Moka.Kubernetes.Server;

/// <summary>
/// One pod as <c>kubectl get pods -o wide --no-headers</c> prints it: how many of its containers stand ready, the
/// status kubectl words, its restart count, its age, and the wide output's node and IP. The namespace is there when
/// the listing asked for every one.
/// </summary>
public sealed record PodRow(
	string Namespace,
	string Name,
	string Ready,
	string Status,
	string Restarts,
	string Age,
	string Ip,
	string Node)
{
	/// <summary>
	/// The states a pod in ordinary work sits in: running, being created, starting its containers, finished well or
	/// going away. Everything else, from <c>CrashLoopBackOff</c> to <c>Pending</c>, is a pod that needs looking at,
	/// which the tab marks with a warning chip.
	/// </summary>
	public bool Settled => Status is "Running" or "ContainerCreating" or "PodInitializing" or "Succeeded" or "Completed" or "Terminating";
}

/// <summary>
/// Reads the pod listing kubectl prints with <c>-o wide --no-headers</c>. The columns are fixed, save one wrinkle:
/// a recent restart reads as <c>5 (2m ago)</c>, a parenthetical of two or three words that is part of the restart
/// count, so the reader steps over it before the age. A line that does not hold the shape is skipped rather than
/// guessed at, and whether the first column is a namespace is what the caller asked for, not something to smell.
/// </summary>
public static class PodListing
{
	public static IReadOnlyList<PodRow> Parse(string text, bool hasNamespace)
	{
		List<PodRow> rows = [];
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("NAME ", StringComparison.Ordinal) || trimmed.StartsWith("NAMESPACE ", StringComparison.Ordinal))
			{
				continue;
			}

			if (ReadRow(trimmed, hasNamespace) is { } row)
			{
				rows.Add(row);
			}
		}

		return rows;
	}

	/// <summary>One line of the listing, or null when the line is not a pod row whole.</summary>
	internal static PodRow? ReadRow(string line, bool hasNamespace)
	{
		string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		// The shape through the node column; the two <none> columns kubectl's wide output ends with are tolerated
		// by their absence rather than required.
		int least = hasNamespace ? 8 : 7;
		if (tokens.Length < least)
		{
			return null;
		}

		int at = 0;
		string space = hasNamespace ? tokens[at++] : "";
		string name = tokens[at++];
		string ready = tokens[at++];
		string status = tokens[at++];
		string restarts = tokens[at++];

		// "5 (2m ago)": the parenthetical belongs to the count, whatever its own word count, and after it the age,
		// the IP and the node must still be there.
		if (tokens[at].StartsWith('('))
		{
			while (at < tokens.Length && !tokens[at].EndsWith(')'))
			{
				at++;
			}

			at++;
			if (tokens.Length - at < 3)
			{
				return null;
			}
		}

		string age = tokens[at++];
		string ip = tokens[at++];
		string node = tokens[at++];
		return new PodRow(space, name, ready, status, restarts, age, ip, node);
	}
}
