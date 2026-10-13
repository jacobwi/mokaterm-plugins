using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using Moka.DevTools.Text;

namespace Moka.DevTools.Hashing;

/// <summary>
/// Every algorithm over the same bytes in one pass, so a large file is read once. With a key every algorithm runs as an
/// HMAC instead, and the ones with no HMAC form (CRC32) say so.
/// </summary>
internal static class HashComputer
{
	private const int ChunkBytes = 1024 * 1024;

	public static IReadOnlyList<HashValue> Compute(ReadOnlySpan<byte> data, ReadOnlySpan<byte> hmacKey, bool hmac)
	{
		List<Runner> runners = Start(hmacKey, hmac);
		try
		{
			foreach (Runner runner in runners)
			{
				runner.Append(data);
			}

			return Finish(runners, hmac);
		}
		finally
		{
			Dispose(runners);
		}
	}

	/// <summary>
	/// Reads <paramref name="stream"/> to its end. <paramref name="progress"/> hears the bytes read so far after every
	/// chunk, and a stream longer than <see cref="TextLimits.MaxHashFileBytes"/> is refused rather than cut.
	/// </summary>
	public static async Task<IReadOnlyList<HashValue>> ComputeAsync(
		Stream stream,
		byte[]? hmacKey,
		bool hmac,
		IProgress<long>? progress,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(stream);
		List<Runner> runners = Start(hmacKey, hmac);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);
		try
		{
			long total = 0;
			int read;
			while ((read = await stream.ReadAsync(buffer.AsMemory(0, ChunkBytes), cancellationToken)) > 0)
			{
				total += read;
				if (total > TextLimits.MaxHashFileBytes)
				{
					throw new IOException(string.Create(CultureInfo.InvariantCulture, $"The file is larger than {TextLimits.MaxHashFileBytes / (1024 * 1024 * 1024)} GiB, the most this hashes."));
				}

				foreach (Runner runner in runners)
				{
					runner.Append(buffer.AsSpan(0, read));
				}

				progress?.Report(total);
			}

			return Finish(runners, hmac);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
			Dispose(runners);
		}
	}

	private static List<Runner> Start(ReadOnlySpan<byte> key, bool hmac)
	{
		List<Runner> runners = [];
		foreach (HashAlgorithmInfo info in HashAlgorithmInfo.All)
		{
			if (!info.Runs(hmac))
			{
				continue;
			}

			if (info.Kind == HashKind.Crc32)
			{
				runners.Add(new Runner(info, null, new Crc32()));
			}
			else
			{
				HashAlgorithmName name = info.Algorithm!.Value;
				runners.Add(new Runner(info, hmac ? IncrementalHash.CreateHMAC(name, key) : IncrementalHash.CreateHash(name), null));
			}
		}

		return runners;
	}

	private static List<HashValue> Finish(List<Runner> runners, bool hmac)
	{
		List<HashValue> values = [];
		foreach (HashAlgorithmInfo info in HashAlgorithmInfo.All)
		{
			Runner? runner = runners.Find(candidate => candidate.Info.Kind == info.Kind);
			values.Add(runner is null
				? new HashValue(info, null, hmac && info.Supported ? "has no HMAC form" : "not available here")
				: new HashValue(info, runner.Finish(), null));
		}

		return values;
	}

	private static void Dispose(List<Runner> runners)
	{
		foreach (Runner runner in runners)
		{
			runner.Hash?.Dispose();
		}
	}

	private sealed record Runner(HashAlgorithmInfo Info, IncrementalHash? Hash, Crc32? Crc)
	{
		public void Append(ReadOnlySpan<byte> data)
		{
			Hash?.AppendData(data);
			Crc?.Append(data);
		}

		public byte[] Finish() => Hash?.GetHashAndReset() ?? Crc!.Finish();
	}
}
