using System.Text.Json;

namespace Moka.Kubernetes.Server;

/// <summary>
/// The versions <c>kubectl version -o json</c> answers: the client kubectl itself, always, and the server when the
/// cluster answered. No server in the answer is not a failure, and this record says so by shape rather than by
/// throwing: <see cref="Server"/> is null then.
/// </summary>
public sealed record KubectlVersion(string? Client, string? Server)
{
	/// <summary>True when the cluster's API server answered with its own version.</summary>
	public bool HasServer => Server is not null;

	/// <summary>
	/// Reads the JSON kubectl printed. Anything that is not the shape kubectl promises, including an empty answer,
	/// reads as a version with nothing in it, because the words for why live with the run, not the parser.
	/// </summary>
	public static KubectlVersion Parse(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return new(null, null);
		}

		// Some kubectl builds print a deprecation notice before the JSON; the JSON is still the answer, so the
		// reader starts where it begins.
		int start = json.IndexOf('{');
		if (start < 0)
		{
			return new(null, null);
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(json[start..]);
			JsonElement root = document.RootElement;
			return new(Version(root, "clientVersion"), Version(root, "serverVersion"));
		}
		catch (JsonException)
		{
			return new(null, null);
		}
		catch (InvalidOperationException)
		{
			return new(null, null);
		}
	}

	private static string? Version(JsonElement root, string name) =>
		root.ValueKind == JsonValueKind.Object &&
		root.TryGetProperty(name, out JsonElement section) && section.ValueKind == JsonValueKind.Object &&
		section.TryGetProperty("gitVersion", out JsonElement git) && git.ValueKind == JsonValueKind.String
			? git.GetString()
			: null;
}
