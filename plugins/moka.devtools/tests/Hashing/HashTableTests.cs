using System.Security.Cryptography;
using System.Text;
using Moka.DevTools.Components;
using Moka.DevTools.Hashing;
using Moka.DevTools.Tests.Shared;
using Moka.DevTools.Tool;

namespace Moka.DevTools.Tests.Hashing;

public sealed class HashTableTests
{
	private static readonly byte[] Data = Encoding.UTF8.GetBytes("hello world");

	[Fact]
	public async Task Table_ListsEveryDigestWithACopyEach()
	{
		IReadOnlyList<HashValue> values = HashComputer.Compute(Data, [], hmac: false);

		string html = await RenderAsync(values);

		Assert.Contains("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", html, StringComparison.Ordinal);
		Assert.Contains("5eb63bbbe01eeed093cb22bb8f5acdc3", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy SHA-256\"", html, StringComparison.Ordinal);
		Assert.Contains(">Weak<", html, StringComparison.Ordinal);
		Assert.Contains(">Checksum<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">Matches<", html, StringComparison.Ordinal);
		Assert.Contains("algorithms", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Expected_MarksTheRowThatGivesIt()
	{
		IReadOnlyList<HashValue> values = HashComputer.Compute(Data, [], hmac: false);

		string html = await RenderAsync(values, expected: SHA256.HashData(Data));

		Assert.Equal(1, Render.Count(html, ">Matches<"));
	}

	[Fact]
	public async Task Format_ChangesHowDigestsRead()
	{
		IReadOnlyList<HashValue> values = HashComputer.Compute(Data, [], hmac: false);

		string upper = await RenderAsync(values, HashFormat.UpperHex);
		string base64 = await RenderAsync(values, HashFormat.Base64);

		Assert.Contains("B94D27B9934D3E08A52E52D7DA7DABFAC484EFE37A5380EE9088F7ACE2EFCDE9", upper, StringComparison.Ordinal);
		Assert.Contains(Convert.ToBase64String(SHA256.HashData(Data)), base64, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Hmac_NamesItsRowsAndSaysWhatDoesNotRun()
	{
		IReadOnlyList<HashValue> values = HashComputer.Compute(Data, Encoding.UTF8.GetBytes("key"), hmac: true);

		string html = await RenderAsync(values, hmac: true);

		Assert.Contains(">HMACs<", html, StringComparison.Ordinal);
		Assert.Contains("HMAC-SHA-256", html, StringComparison.Ordinal);
		Assert.Contains("with the key", html, StringComparison.Ordinal);
		HashValue crc = Assert.Single(values, value => value.Algorithm.Kind == HashKind.Crc32);
		Assert.Contains(crc.Unavailable!, html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(IReadOnlyList<HashValue> values, HashFormat format = HashFormat.Hex, byte[]? expected = null, bool hmac = false) =>
		Render.HtmlAsync<HashTable>(new()
		{
			[nameof(HashTable.Values)] = values,
			[nameof(HashTable.Format)] = format,
			[nameof(HashTable.Expected)] = expected,
			[nameof(HashTable.Hmac)] = hmac,
		});
}
