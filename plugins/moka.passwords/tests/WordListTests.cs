using Moka.Passwords.Generation;

namespace Moka.Passwords.Tests;

public sealed class WordListTests
{
	public static TheoryData<WordList> Lists()
	{
		TheoryData<WordList> data = [];
		foreach (WordList list in new[] { WordList.Large, WordList.Small })
		{
			data.Add(list);
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(Lists))]
	public void TheList_HoldsItsSizeInUniqueWords(WordList list)
	{
		Assert.Equal(list.Count, list.Words.Count);
		Assert.Equal(list.Count, list.Words.Distinct(StringComparer.Ordinal).Count());
		Assert.All(list.Words, word => Assert.False(string.IsNullOrWhiteSpace(word)));
	}

	[Theory]
	[MemberData(nameof(Lists))]
	public void EveryWord_IsLowerCaseLetters_WithAtMostInnerDashes(WordList list) =>
		Assert.All(list.Words, word => Assert.Matches("^[a-z]{1,9}(-[a-z]+)*$", word));

	[Theory]
	[MemberData(nameof(Lists))]
	public void TheList_IsEmbeddedUnderItsName(WordList list) =>
		Assert.Contains(list.ResourceName, typeof(WordList).Assembly.GetManifestResourceNames());

	[Theory]
	[MemberData(nameof(Lists))]
	public void TheCredit_NamesTheListAndItsLicense(WordList list)
	{
		Assert.Contains("EFF", list.Credit, StringComparison.Ordinal);
		Assert.Contains("CC BY 3.0 US", list.Credit, StringComparison.Ordinal);
	}

	// What lets a dash separator keep the passphrase strength exact (PassphraseOptions.BoundariesAmbiguous).
	[Theory]
	[MemberData(nameof(Lists))]
	public void DashedWords_NeverSplitIntoTwoWordsOfTheList(WordList list)
	{
		HashSet<string> words = new(list.Words, StringComparer.Ordinal);
		string[] dashed = [.. list.Words.Where(word => word.Contains('-', StringComparison.Ordinal))];

		Assert.All(dashed, word => Assert.False(word.Split('-').All(words.Contains)));
	}

	[Fact]
	public void TheLargeList_KeepsTheEffOrder()
	{
		Assert.Equal(7776, WordList.Large.Count);
		Assert.Equal("abacus", WordList.Large.Words[0]);
		Assert.Equal("abdomen", WordList.Large.Words[1]);
		Assert.Equal("zoom", WordList.Large.Words[^1]);
	}

	[Fact]
	public void TheLargeList_HasTheFourDashedWordsOfTheEffFile() =>
		Assert.Equal(["drop-down", "felt-tip", "t-shirt", "yo-yo"], WordList.Large.Words.Where(word => word.Contains('-', StringComparison.Ordinal)));

	[Fact]
	public void TheShortList_KeepsTheEffOrder()
	{
		Assert.Equal(1296, WordList.Small.Count);
		Assert.Equal("acid", WordList.Small.Words[0]);
		Assert.Equal("acorn", WordList.Small.Words[1]);
		Assert.Equal("zoom", WordList.Small.Words[^1]);
	}

	// The short list holds words of three to five letters, so a passphrase is quicker to type.
	[Fact]
	public void TheShortList_HasOnlyShortWords()
	{
		Assert.All(WordList.Small.Words, word => Assert.InRange(word.Length, 3, 5));
		Assert.Equal("yo-yo", WordList.Small.Words.Single(word => word.Contains('-', StringComparison.Ordinal)));
	}

	[Theory]
	[InlineData(false, 7776)]
	[InlineData(true, 1296)]
	public void For_PicksTheListTheOptionNames(bool shorter, int count) => Assert.Equal(count, WordList.For(shorter).Count);
}
