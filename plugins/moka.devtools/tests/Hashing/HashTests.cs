using System.Security.Cryptography;
using System.Text;
using Moka.DevTools.Hashing;

namespace Moka.DevTools.Tests.Hashing;

public sealed class HashTests
{
	private static readonly byte[] Abc = Encoding.ASCII.GetBytes("abc");

	[Theory]
	[InlineData("", 0x00000000u)]
	[InlineData("123456789", 0xCBF43926u)]
	[InlineData("The quick brown fox jumps over the lazy dog", 0x414FA339u)]
	public void Crc32_MatchesThePublishedValues(string text, uint expected)
	{
		Assert.Equal(expected, Crc32.Compute(Encoding.ASCII.GetBytes(text)));

		Crc32 pieces = new();
		foreach (byte value in Encoding.ASCII.GetBytes(text))
		{
			pieces.Append([value]);
		}

		Assert.Equal(expected.ToString("x8", System.Globalization.CultureInfo.InvariantCulture), Convert.ToHexStringLower(pieces.Finish()));
	}

	[Fact]
	public void Compute_GivesThePublishedDigestsOfAbc()
	{
		Dictionary<string, string> expected = new()
		{
			["MD5"] = "900150983cd24fb0d6963f7d28e17f72",
			["SHA-1"] = "a9993e364706816aba3e25717850c26c9cd0d89d",
			["SHA-256"] = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
			["SHA-384"] = "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7",
			["SHA-512"] = "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f",
			["SHA3-256"] = "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532",
			["SHA3-384"] = "ec01498288516fc926459f58e2c6ad8df9b473cb0fc08c2596da7cf0e49be4b298d88cea927ac7f539f1edf228376d25",
			["SHA3-512"] = "b751850b1a57168a5693cd924b6b096e08f621827444f70d884f5d0240d2712e10e116e9192af3c91a7ec57647e3934057340b4cf408d5a56592f8274eec53f0",
			["CRC32"] = "352441c2",
		};

		IReadOnlyList<HashValue> values = HashComputer.Compute(Abc, [], hmac: false);

		Assert.Equal(expected.Keys, values.Select(value => value.Algorithm.Name));
		foreach (HashValue value in values)
		{
			if (value.Algorithm.Supported)
			{
				Assert.Equal(expected[value.Algorithm.Name], Convert.ToHexStringLower(value.Bytes!));
			}
			else
			{
				Assert.Equal("not available here", value.Unavailable);
			}
		}
	}

	[Fact]
	public void Compute_GivesThePublishedHmacs()
	{
		// RFC 2104, RFC 2202 and RFC 4231 test case 2.
		Dictionary<string, string> expected = new()
		{
			["MD5"] = "750c783e6ab0b503eaa86e310a5db738",
			["SHA-1"] = "effcdf6ae5eb2fa2d27416d5f184df9c259a7c79",
			["SHA-256"] = "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843",
			["SHA-384"] = "af45d2e376484031617f78d2b58a6b1b9c7ef464f5a01b47e42ec3736322445e8e2240ca5e69e2c78b3239ecfab21649",
			["SHA-512"] = "164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea2505549758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737",
			["SHA3-256"] = "c7d4072e788877ae3596bbb0da73b887c9171f93095b294ae857fbe2645e1ba5",
		};

		IReadOnlyList<HashValue> values = HashComputer.Compute(Encoding.ASCII.GetBytes("what do ya want for nothing?"), Encoding.ASCII.GetBytes("Jefe"), hmac: true);

		foreach (HashValue value in values)
		{
			if (expected.TryGetValue(value.Algorithm.Name, out string? hex) && value.Algorithm.HmacSupported)
			{
				Assert.Equal(hex, Convert.ToHexStringLower(value.Bytes!));
			}
		}

		Assert.Equal("has no HMAC form", values.Single(value => value.Algorithm.Kind == HashKind.Crc32).Unavailable);
	}

	[Fact]
	public async Task ComputeAsync_ReadsAStreamInPiecesWithProgress()
	{
		byte[] data = new byte[(3 * 1024 * 1024) + 17];
		new Random(3).NextBytes(data);
		List<long> reports = [];
		using MemoryStream stream = new(data);

		IReadOnlyList<HashValue> values = await HashComputer.ComputeAsync(stream, null, hmac: false, new SyncProgress(reports), TestContext.Current.CancellationToken);

		Assert.Equal(SHA256.HashData(data), values.Single(value => value.Algorithm.Kind == HashKind.Sha256).Bytes);
		Assert.Equal(Crc32.Compute(data), System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(values.Single(value => value.Algorithm.Kind == HashKind.Crc32).Bytes));
		Assert.Equal(data.Length, reports[^1]);
		Assert.True(reports.Count >= 4);
	}

	[Fact]
	public async Task ComputeAsync_StopsWhenCancelled()
	{
		using MemoryStream stream = new(new byte[1024]);
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HashComputer.ComputeAsync(stream, null, hmac: false, null, cancelled.Token));
	}

	[Theory]
	[InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", "hex")]
	[InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad  abc.txt", "hex")]
	[InlineData("sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "hex")]
	[InlineData("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", "Base64")]
	[InlineData("sha256-ungWv48Bz-pBQUDeXa4iI7ADYaOWF3qctBD_YfIAFa0", "Base64url")]
	public void ExpectedHash_ReadsEveryForm(string text, string form)
	{
		(byte[] bytes, string read) = ExpectedHash.Parse(text);

		Assert.Equal(SHA256.HashData(Abc), bytes);
		Assert.Equal(form, read);
	}

	[Theory]
	[InlineData("", "Paste the hash")]
	[InlineData("abc", "odd number")]
	[InlineData("not a hash!", "neither hex nor Base64")]
	public void ExpectedHash_SaysWhyItCannotRead(string text, string expected) =>
		Assert.Contains(expected, Assert.Throws<FormatException>(() => ExpectedHash.Parse(text)).Message, StringComparison.Ordinal);

	[Fact]
	public void SecretKey_ReadsEachFormAndNeverQuotesTheKey()
	{
		Assert.Equal("secret"u8.ToArray(), SecretKey.Read("secret", KeyFormat.Text));
		Assert.Equal("secret"u8.ToArray(), SecretKey.Read("736563726574", KeyFormat.Hex));
		Assert.Equal("secret"u8.ToArray(), SecretKey.Read("c2VjcmV0", KeyFormat.Base64));

		string message = Assert.Throws<FormatException>(() => SecretKey.Read("hunter2!", KeyFormat.Hex)).Message;
		Assert.DoesNotContain("hunter2", message, StringComparison.Ordinal);
		Assert.DoesNotContain("'h'", message, StringComparison.Ordinal);
		Assert.Contains("position 1", message, StringComparison.Ordinal);
	}

	private sealed class SyncProgress(List<long> reports) : IProgress<long>
	{
		public void Report(long value) => reports.Add(value);
	}
}
