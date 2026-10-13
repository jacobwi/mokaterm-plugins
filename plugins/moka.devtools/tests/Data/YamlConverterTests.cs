using System.Text;
using Moka.DevTools.Data;

namespace Moka.DevTools.Tests.Data;

public sealed class YamlConverterTests
{
	[Fact]
	public void ToJson_TypesPlainValuesByTheCoreSchema()
	{
		string yaml = "a: 1\nb: true\nc: yes\nd: ~\ne: '1'\nf: 0x1F\ng: .5\nh: 1e3\ni: 0o17\nj: +12\nk: \"true\"\nl:\nm: 007\nn: 1.\n";

		string json = Minify(YamlConverter.ToJson(yaml, JsonIndent.TwoSpaces, sortKeys: false).Text);

		Assert.Equal("""{"a":1,"b":true,"c":"yes","d":null,"e":"1","f":31,"g":0.5,"h":1e3,"i":15,"j":12,"k":"true","l":null,"m":7,"n":1.0}""", json);
	}

	[Fact]
	public void ToJson_KeepsBlockTextAndNesting()
	{
		string yaml = "list:\n  - one\n  - two: 2\n    three: [3, 4]\ntext: |\n  line 1\n  line 2\n";

		Assert.Equal("""{"list":["one",{"two":2,"three":[3,4]}],"text":"line 1\nline 2\n"}""", Minify(YamlConverter.ToJson(yaml, JsonIndent.TwoSpaces, false).Text));
	}

	[Fact]
	public void ToJson_AppliesAnchorsAndMergeKeys()
	{
		string yaml = "base: &b\n  x: 1\n  y: 2\nderived:\n  <<: *b\n  y: 3\ncopy: *b\n";

		Assert.Equal("""{"base":{"x":1,"y":2},"derived":{"y":3,"x":1},"copy":{"x":1,"y":2}}""", Minify(YamlConverter.ToJson(yaml, JsonIndent.TwoSpaces, false).Text));
		Assert.Equal("""{"base":{"x":1,"y":2},"copy":{"x":1,"y":2},"derived":{"x":1,"y":3}}""", Minify(YamlConverter.ToJson(yaml, JsonIndent.TwoSpaces, true).Text));
	}

	[Fact]
	public void ToJson_RefusesTheBillionLaughs()
	{
		StringBuilder yaml = new("a: &a [x, x, x, x, x, x, x, x, x, x]\n");
		for (char name = 'b'; name <= 'j'; name++)
		{
			char previous = (char)(name - 1);
			yaml.Append(name).Append(": &").Append(name).Append(" [")
				.Append(string.Join(", ", Enumerable.Repeat("*" + previous, 10))).Append("]\n");
		}

		Assert.Contains("too large", Assert.Throws<DataException>(() => YamlConverter.ToJson(yaml.ToString(), JsonIndent.TwoSpaces, false)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ToJson_RefusesAnAliasIntoItself() =>
		Assert.Throws<DataException>(() => YamlConverter.ToJson("a: &a [*a]\n", JsonIndent.TwoSpaces, false));

	[Theory]
	[InlineData("a: 1\nb: 2\na: 3\n", "appears twice", 3)]
	[InlineData("a: 1\n? [x, y]\n: 2\n", "can only be text", 2)]
	[InlineData("a: 1\nb: .inf\n", "no way to write", 2)]
	[InlineData("a: [1, 2\nb: 3\n", "", 1)]
	[InlineData("a: *nothing\n", "names no anchor", 1)]
	[InlineData("a: !!int x\n", "tagged as a number", 1)]
	public void ToJson_SaysWhatJsonCannotHoldAndWhere(string yaml, string expected, int line)
	{
		DataException error = Assert.Throws<DataException>(() => YamlConverter.ToJson(yaml, JsonIndent.TwoSpaces, false));

		Assert.Contains(expected, error.Message, StringComparison.Ordinal);
		Assert.NotNull(error.Position);
		Assert.True(error.Position!.Value.Line >= line, error.Message);
	}

	[Fact]
	public void ToJson_TurnsSeveralDocumentsIntoAnArrayAndSaysSo()
	{
		YamlConversion conversion = YamlConverter.ToJson("a: 1\n---\nb: 2\n", JsonIndent.TwoSpaces, false);

		Assert.Equal("""[{"a":1},{"b":2}]""", Minify(conversion.Text));
		Assert.Contains(conversion.Notes, note => note.Contains("2 documents", StringComparison.Ordinal));
	}

	[Fact]
	public void ToJson_DropsCustomTagsAndSaysWhich()
	{
		YamlConversion conversion = YamlConverter.ToJson("bucket: !Ref MyBucket\nname: !!str 12\n", JsonIndent.TwoSpaces, false);

		Assert.Equal("""{"bucket":"MyBucket","name":"12"}""", Minify(conversion.Text));
		Assert.Contains(conversion.Notes, note => note.Contains("!Ref", StringComparison.Ordinal));
	}

	[Fact]
	public void FromJson_QuotesWhatWouldReadAsAnotherType()
	{
		string yaml = YamlConverter.FromJson("""{"a":"true","b":"hello","c":"12","d":"yes","e":"","f":1.5,"g":null,"h":false,"i":[],"j":{}," k":" x"}""", sortKeys: false).Text;

		Assert.Contains("a: \"true\"", yaml, StringComparison.Ordinal);
		Assert.Contains("b: hello", yaml, StringComparison.Ordinal);
		Assert.Contains("c: \"12\"", yaml, StringComparison.Ordinal);
		Assert.Contains("d: \"yes\"", yaml, StringComparison.Ordinal);
		Assert.Contains("e: \"\"", yaml, StringComparison.Ordinal);
		Assert.Contains("f: 1.5", yaml, StringComparison.Ordinal);
		Assert.Contains("g: null", yaml, StringComparison.Ordinal);
		Assert.Contains("h: false", yaml, StringComparison.Ordinal);
		Assert.Contains("i: []", yaml, StringComparison.Ordinal);
		Assert.Contains("j: {}", yaml, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("""{"a":"true","b":"hello","c":"12","d":"yes","e":"","f":1.5,"g":null,"h":false,"i":[],"j":{}," k":" x","l":"line 1\nline 2\n","m":"a: b # c","n":"- x","o":[1,[2,{"p":"é 😀"}]],"q":"~","r":"0x10","s":".5","t":"1e3"}""")]
	[InlineData("""[1,"two",{"three":3}]""")]
	[InlineData("\"just text\"")]
	[InlineData("42")]
	public void RoundTrip_GivesBackTheSameData(string json)
	{
		string yaml = YamlConverter.FromJson(json, sortKeys: false).Text;
		string back = YamlConverter.ToJson(yaml, JsonIndent.TwoSpaces, sortKeys: false).Text;

		Assert.Equal(Minify(json), Minify(back));
	}

	[Fact]
	public void Validate_CountsDocuments()
	{
		Assert.Equal(1, YamlConverter.Validate("a: 1\n"));
		Assert.Equal(3, YamlConverter.Validate("a\n---\nb\n---\nc\n"));
		Assert.Throws<DataException>(() => YamlConverter.Validate("a: [\n"));
	}

	private static string Minify(string json) => JsonTools.Minify(json, sortKeys: false);
}
