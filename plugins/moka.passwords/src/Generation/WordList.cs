using System.Text;

namespace Moka.Passwords.Generation;

/// <summary>
/// The EFF large word list (CC BY 3.0 US): 7776 words, one for each roll of five dice. It is embedded in the plugin as
/// the words alone, one per line, and read once.
/// </summary>
public static class WordList
{
	public const int Count = 7776;

	public const string ResourceName = "Moka.Passwords.eff-large-words.txt";

	/// <summary>The credit the tool shows beside a passphrase, which the license asks for.</summary>
	public const string Credit = "Passphrase words: EFF large word list, CC BY 3.0 US";

	private static readonly Lazy<string[]> Loaded = new(Load);

	public static IReadOnlyList<string> Words => Loaded.Value;

	private static string[] Load()
	{
		using Stream stream = typeof(WordList).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException("The word list is missing from the plugin.");
		using StreamReader reader = new(stream, Encoding.ASCII);
		string[] words = reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		// A short or padded list would make every passphrase weaker than the strength says.
		return words.Length == Count && words.Distinct(StringComparer.Ordinal).Count() == Count
			? words
			: throw new InvalidOperationException("The word list is damaged.");
	}
}
