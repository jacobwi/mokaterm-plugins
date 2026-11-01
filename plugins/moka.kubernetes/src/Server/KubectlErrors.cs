namespace Moka.Kubernetes.Server;

/// <summary>
/// The honest words for a kubectl call that did not answer: the ways a cluster is unreachable are few and known, so
/// each is named for what it is rather than shown as kubectl printed it, and everything else is kubectl's own first
/// line, kept whole. A run that never started says the host's own words.
/// </summary>
public static class KubectlErrors
{
	/// <summary>Why the run did not answer, in words for the user; <see cref="ServerRun.Ok"/> runs never come here.</summary>
	public static string Words(ServerRun run)
	{
		ArgumentNullException.ThrowIfNull(run);
		if (run.NeverRan || run.TimedOut || run.Broke)
		{
			return run.Problem ?? "The command could not run.";
		}

		string said = FirstLine(run.Error.Length > 0 ? run.Error : run.Output);
		return Said(said) ?? (said.Length > 0 ? said : $"kubectl ended with status {run.ExitCode}.");
	}

	/// <summary>
	/// What kubectl said, when its words name a state the tool knows: no kubeconfig, a refused or unresolvable API
	/// server, credentials the cluster refused, rights it will not grant, a context that names nothing, a certificate
	/// it does not trust, something that is not there any more.
	/// </summary>
	internal static string? Said(string text)
	{
		if (text.Length == 0)
		{
			return null;
		}

		string bare = text.StartsWith("error: ", StringComparison.Ordinal) ? text["error: ".Length..].Trim() : text;
		if (bare.Contains("no configuration has been provided", StringComparison.OrdinalIgnoreCase) ||
			bare.Contains("KUBERNETES_MASTER", StringComparison.Ordinal))
		{
			return "kubectl on the server has no cluster configuration: there is no kubeconfig for this account to read, so no cluster can be reached from here.";
		}

		if (bare.Contains("was refused", StringComparison.OrdinalIgnoreCase) || bare.Contains("connection refused", StringComparison.OrdinalIgnoreCase))
		{
			return "The connection to the cluster's API server was refused: kubectl points at an address nothing listens on.";
		}

		if (bare.Contains("no such host", StringComparison.OrdinalIgnoreCase))
		{
			return "The cluster's API server address does not resolve from the server, so kubectl cannot even knock.";
		}

		if (bare.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
			bare.Contains("asked for the client to provide credentials", StringComparison.OrdinalIgnoreCase))
		{
			return "The cluster refused the credentials: the kubeconfig on the server names a token or an account that no longer works.";
		}

		if (bare.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
		{
			return "The cluster answered but said no: the account kubectl uses is not allowed to see this.";
		}

		if (bare.Contains("context was not found", StringComparison.OrdinalIgnoreCase) || bare.Contains("no context chosen", StringComparison.Ordinal))
		{
			return "The kubeconfig's current context names a context that is not in it, so kubectl has no cluster to ask.";
		}

		if (bare.Contains("x509", StringComparison.OrdinalIgnoreCase) || bare.Contains("TLS handshake", StringComparison.OrdinalIgnoreCase) ||
			bare.Contains("certificate is trusted", StringComparison.OrdinalIgnoreCase))
		{
			return "kubectl could not establish TLS with the API server: the kubeconfig's certificate authority does not match the server's certificate.";
		}

		if (bare.Contains("not found", StringComparison.OrdinalIgnoreCase))
		{
			return "The cluster no longer has what was asked for: it was deleted, or the name has a typo in it.";
		}

		return null;
	}

	private static string FirstLine(string text)
	{
		int end = text.IndexOf('\n');
		return (end < 0 ? text : text[..end]).Trim();
	}
}
