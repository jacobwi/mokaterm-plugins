using System.Text;

namespace Moka.Passwords.Generation;

/// <summary>
/// One EFF word list (CC BY 3.0 US): a word for every roll of its dice, which is what makes each word as likely as any
/// other. The words are embedded alone, one per line, and read once.
/// </summary>
public sealed class WordList
{
	/// <summary>The EFF large list: 7776 words, one for each roll of five dice.</summary>
	public static WordList Large { get; } = new("EFF large word list", "Moka.Passwords.eff-large-words.txt", 7776);

	/// <summary>The EFF short list: 1296 short words, one for each roll of four dice, so a passphrase is quicker to type.</summary>
	public static WordList Small { get; } = new("EFF short word list", "Moka.Passwords.eff-short-words.txt", 1296);

	public static WordList For(bool shorter) => shorter ? Small : Large;

	/// <summary>How many words the list holds, which is what one word of it is worth in entropy.</summary>
	public int Count { get; }

	/// <summary>Where the list is embedded. <see cref="Words"/> loads it once and checks it against <see cref="Count"/>.</summary>
	public string ResourceName { get; }

	/// <summary>The credit the tool shows beside a passphrase, which the license asks for.</summary>
	public string Credit { get; }

	/// <summary>The words, in the list's own order.</summary>
	public IReadOnlyList<string> Words => _loaded.Value;

	private readonly string _name;

	private readonly Lazy<IReadOnlyList<string>> _loaded;

	private WordList(string name, string resourceName, int count)
	{
		_name = name;
		ResourceName = resourceName;
		Count = count;
		Credit = $"Passphrase words: {name}, CC BY 3.0 US";
		_loaded = new Lazy<IReadOnlyList<string>>(Load);
	}

	private string[] Load()
	{
		using Stream stream = typeof(WordList).Assembly.GetManifestResourceStream(ResourceName)
			?? throw new InvalidOperationException("The word list is missing from the plugin.");
		using StreamReader reader = new(stream, Encoding.ASCII);
		string[] words = reader.ReadToEnd().Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

		// A short or padded list would make every passphrase weaker than the strength says.
		return words.Length == Count && words.Distinct(StringComparer.Ordinal).Count() == Count
			? words
			: throw new InvalidOperationException($"The {_name} is damaged.");
	}
}
