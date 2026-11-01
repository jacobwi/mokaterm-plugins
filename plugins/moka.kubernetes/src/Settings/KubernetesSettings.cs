using Mokaterm.Abstractions.Settings;

namespace Moka.Kubernetes.Settings;

/// <summary>
/// The plugin's defaults, under <c>plugin.moka.kubernetes</c> in settings.json: where kubectl lives on the servers,
/// how long one kubectl command gets, and which namespace the tabs prefer. Every range is a Min and Max pair here,
/// read by <see cref="Clamped"/>, by the settings page and by the tool, so none of them repeats it. Nothing the tabs
/// read from a cluster is kept here.
/// </summary>
public sealed record KubernetesSettings : ISettingsSection
{
	public const int MinCommandTimeoutSeconds = 2;

	public const int MaxCommandTimeoutSeconds = 120;

	/// <summary>The longest a namespace name may be, which RFC 1123 says and Kubernetes enforces.</summary>
	public const int MaxNamespaceLength = 63;

	public static string SectionKey => "plugin.moka.kubernetes";

	/// <summary>
	/// The kubectl the session tool calls on the server, as the server's shell reads it: a name from its PATH when
	/// empty, or a path such as <c>/usr/local/bin/kubectl</c>. Quoted as one word on every line, so spaces survive.
	/// </summary>
	public string KubectlPath { get; init; } = "";

	/// <summary>How long one kubectl command gets before the tab stops waiting, whatever it was asking for.</summary>
	public int CommandTimeoutSeconds { get; init; } = 30;

	/// <summary>
	/// The namespace the pods and rollouts tabs read: <c>-n</c> this on every listing. Empty leaves the choice to
	/// kubectl, which is the namespace of the current context, or <c>default</c> when the context names none; the
	/// All-namespaces switch on each tab still asks for every one.
	/// </summary>
	public string Namespace { get; init; } = "";

	public TimeSpan CommandTimeout => TimeSpan.FromSeconds(CommandTimeoutSeconds);

	/// <summary>A copy with every value inside what the settings page allows, since settings.json can be edited by hand.</summary>
	public KubernetesSettings Clamped()
	{
		// A value the validators refuse is emptied rather than kept: an empty setting is a choice the tabs word
		// (kubectl from PATH, the default namespace), while a broken one half-worked forever.
		string path = KubectlPath?.Trim() ?? "";
		string name = Namespace?.Trim() ?? "";
		return this with
		{
			KubectlPath = KubectlPathProblem(path) is null ? path : "",
			Namespace = NamespaceProblem(name) is null ? name : "",
			CommandTimeoutSeconds = Math.Clamp(CommandTimeoutSeconds, MinCommandTimeoutSeconds, MaxCommandTimeoutSeconds),
		};
	}

	/// <summary>Why <paramref name="path"/> cannot name the kubectl to call, or null when it can. Empty means PATH.</summary>
	public static string? KubectlPathProblem(string? path)
	{
		string trimmed = path?.Trim() ?? "";
		if (trimmed.Length == 0)
		{
			return null;
		}

		if (trimmed.StartsWith('-'))
		{
			return "The kubectl path cannot start with a dash, which kubectl would read as an option.";
		}

		foreach (char character in trimmed)
		{
			if (char.IsControl(character))
			{
				return "The kubectl path cannot hold control characters or line breaks.";
			}
		}

		return null;
	}

	/// <summary>Why <paramref name="value"/> cannot be a namespace name, or null when it can. Empty means kubectl's own choice.</summary>
	public static string? NamespaceProblem(string? value)
	{
		string trimmed = value?.Trim() ?? "";
		if (trimmed.Length == 0)
		{
			return null;
		}

		if (trimmed.Length > MaxNamespaceLength)
		{
			return $"A namespace name is at most {MaxNamespaceLength} characters.";
		}

		foreach (char character in trimmed)
		{
			if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
			{
				return "A namespace name is one lower-case word: letters, digits and dashes.";
			}
		}

		if (trimmed[0] == '-' || trimmed[^1] == '-')
		{
			return "A namespace name starts and ends with a letter or a digit.";
		}

		return null;
	}
}
