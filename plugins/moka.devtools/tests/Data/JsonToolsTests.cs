using Moka.DevTools.Data;
using Moka.DevTools.Text;

namespace Moka.DevTools.Tests.Data;

public sealed class JsonToolsTests
{
	[Fact]
	public void Format_KeepsNumbersOrderAndTextAsWritten()
	{
		string formatted = JsonTools.Format("""{"b":1.50,"a":[1e10,-0,"é <&>"],"c":{}}""", JsonIndent.TwoSpaces, sortKeys: false);

		Assert.Equal("{\n  \"b\": 1.50,\n  \"a\": [\n    1e10,\n    -0,\n    \"é <&>\"\n  ],\n  \"c\": {}\n}", formatted);
	}

	[Fact]
	public void Format_SortsKeysAtEveryLevelAndUsesTheIndentAsked()
	{
		string sorted = JsonTools.Format("""{"b":{"z":1,"y":2},"a":0}""", JsonIndent.Tab, sortKeys: true);

		Assert.Equal("{\n\t\"a\": 0,\n\t\"b\": {\n\t\t\"y\": 2,\n\t\t\"z\": 1\n\t}\n}", sorted);
		Assert.Contains("\n    \"a\"", JsonTools.Format("""{"a":0}""", JsonIndent.FourSpaces, sortKeys: false), StringComparison.Ordinal);
	}

	[Fact]
	public void Minify_TakesOutEveryGap() =>
		Assert.Equal("""{"a":[1,2],"b":"x y"}""", JsonTools.Minify("{\n  \"b\": \"x y\",\n  \"a\": [ 1, 2 ]\n}", sortKeys: true));

	[Fact]
	public void Errors_GiveTheLineAndTheColumnInCharacters()
	{
		// Two bytes of é come before the error on its line, and the column counts it once.
		DataException error = Assert.Throws<DataException>(() => JsonTools.Format("{\n  \"a\": 1,\n  \"é\": x\n}", JsonIndent.TwoSpaces, false));

		Assert.Equal(new TextPosition(3, 8), error.Position);
		Assert.Contains("line 3, column 8", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("""{"a":1,}""")]
	[InlineData("""{"a":1} x""")]
	[InlineData("[1, 2")]
	[InlineData("{'a':1}")]
	[InlineData("// note\n{}")]
	public void Errors_RefuseWhatRfc8259Refuses(string text) =>
		Assert.NotNull(Assert.Throws<DataException>(() => JsonTools.Validate(text)).Position);

	[Fact]
	public void Errors_SayWhenThereIsNothing() =>
		Assert.Contains("no JSON", Assert.Throws<DataException>(() => JsonTools.Format("  \n ", JsonIndent.TwoSpaces, false)).Message, StringComparison.Ordinal);

	[Fact]
	public void Validate_DescribesTheTopAndWarnsOfDuplicateKeys()
	{
		JsonSummary summary = JsonTools.Validate("{\n  \"a\": 1,\n  \"b\": {\"x\": 1, \"x\": 2},\n  \"a\": 3\n}");

		Assert.Equal("an object with 3 keys", summary.Description);
		Assert.Equal(2, summary.Warnings.Count);
		Assert.Contains("\"x\"", summary.Warnings[0], StringComparison.Ordinal);
		Assert.Contains("line 3", summary.Warnings[0], StringComparison.Ordinal);
		Assert.Contains("line 4, column 3", summary.Warnings[1], StringComparison.Ordinal);
		Assert.Equal("an array of 2 items", JsonTools.Validate("[1,2]").Description);
		Assert.Equal("a string", JsonTools.Validate("\"x\"").Description);
		Assert.Empty(JsonTools.Validate("[{\"a\":1},{\"a\":2}]").Warnings);
	}

	[Fact]
	public void Depth_IsCapped()
	{
		string deep = new string('[', JsonTools.MaxDepth + 10) + new string(']', JsonTools.MaxDepth + 10);

		Assert.Throws<DataException>(() => JsonTools.Validate(deep));
		Assert.Equal("an array of 1 item", JsonTools.Validate(new string('[', 100) + new string(']', 100)).Description);
	}

	[Fact]
	public void Bom_IsSkipped() => Assert.Equal("{}", JsonTools.Minify((char)0xFEFF + "{ }", false));
}
