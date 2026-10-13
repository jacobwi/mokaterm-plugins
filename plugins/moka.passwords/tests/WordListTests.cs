using Moka.Passwords.Generation;

namespace Moka.Passwords.Tests;

public sealed class WordListTests
{
	[Fact]
	public void TheList_Has7776UniqueWords()
	{
		Assert.Equal(7776, WordList.Count);
		Assert.Equal(WordList.Count, WordList.Words.Count);
		Assert.Equal(WordList.Count, WordList.Words.Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public void EveryWord_IsLowerCaseLetters_WithAtMostInnerDashes() =>
		Assert.All(WordList.Words, word => Assert.Matches("^[a-z]{1,9}(-[a-z]+)*$", word));

	[Fact]
	public void TheList_KeepsTheEffOrder()
	{
		Assert.Equal("abacus", WordList.Words[0]);
		Assert.Equal("abdomen", WordList.Words[1]);
		Assert.Equal("zoom", WordList.Words[^1]);
	}

	// What lets a dash separator keep the passphrase strength exact (PassphraseOptions.BoundariesAmbiguous).
	[Fact]
	public void DashedWords_NeverSplitIntoTwoWordsOfTheList()
	{
		HashSet<string> words = new(WordList.Words, StringComparer.Ordinal);
		string[] dashed = [.. WordList.Words.Where(word => word.Contains('-', StringComparison.Ordinal))];

		Assert.Equal(["drop-down", "felt-tip", "t-shirt", "yo-yo"], dashed);
		Assert.All(dashed, word => Assert.False(word.Split('-').All(words.Contains)));
	}

	[Fact]
	public void TheList_IsEmbeddedUnderItsName() =>
		Assert.Contains(WordList.ResourceName, typeof(WordList).Assembly.GetManifestResourceNames());

	[Fact]
	public void TheCredit_NamesTheListAndItsLicense()
	{
		Assert.Contains("EFF large word list", WordList.Credit, StringComparison.Ordinal);
		Assert.Contains("CC BY 3.0 US", WordList.Credit, StringComparison.Ordinal);
	}
}
