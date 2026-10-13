using System.Diagnostics.CodeAnalysis;

namespace Mokaterm.Gallery.LoadCheck;

/// <summary>
/// --package FILE --id ID --work DIR --result FILE [--taken-module ID]...
/// A taken module id is one Mokaterm or another listed plugin already registers, which the app refuses a second time.
/// </summary>
internal sealed record LoadCheckArguments(string PackagePath, string Id, string WorkDirectory, string ResultPath, IReadOnlyList<string> TakenModules)
{
	private const string Usage = "Usage: Mokaterm.Gallery.LoadCheck --package FILE --id ID --work DIR --result FILE [--taken-module ID]...";

	public static bool TryParse(string[] args, [NotNullWhen(true)] out LoadCheckArguments? arguments, [NotNullWhen(false)] out string? usage)
	{
		arguments = null;
		usage = Usage;
		Dictionary<string, string> single = new(StringComparer.Ordinal);
		List<string> taken = [];
		for (int index = 0; index < args.Length; index += 2)
		{
			if (index + 1 >= args.Length)
			{
				return false;
			}

			string name = args[index];
			string value = args[index + 1];
			if (name == "--taken-module")
			{
				taken.Add(value);
			}
			else if (name is "--package" or "--id" or "--work" or "--result")
			{
				single[name] = value;
			}
			else
			{
				return false;
			}
		}

		if (!single.TryGetValue("--package", out string? package)
			|| !single.TryGetValue("--id", out string? id)
			|| !single.TryGetValue("--work", out string? work)
			|| !single.TryGetValue("--result", out string? result))
		{
			return false;
		}

		arguments = new LoadCheckArguments(Path.GetFullPath(package), id, Path.GetFullPath(work), Path.GetFullPath(result), taken);
		usage = null;
		return true;
	}
}
