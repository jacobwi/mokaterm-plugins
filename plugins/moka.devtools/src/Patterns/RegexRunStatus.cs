namespace Moka.DevTools.Patterns;

/// <summary>The status names the page's script reports, kept in one place.</summary>
public static class RegexRunStatus
{
	public const string Ok = "ok";

	/// <summary>The pattern or the flags do not compile.</summary>
	public const string Syntax = "syntax";

	/// <summary>The worker ran past the time limit and was stopped.</summary>
	public const string Timeout = "timeout";

	/// <summary>The page could not start a worker, so nothing ran.</summary>
	public const string Unavailable = "unavailable";

	/// <summary>A newer run took this one's place.</summary>
	public const string Superseded = "superseded";
}
