using System.Text;
using Moka.Network.Targets;

namespace Moka.Network.Whois;

/// <summary>
/// RDAP over HTTPS (RFC 9082): a plain GET with no credentials, the whole body read within the time limit. One client
/// for every request, so the connections a lookup opens are reused across the app run.
/// </summary>
public sealed class HttpsRdapTransport : IRdapTransport
{
	/// <summary>The most one answer may be, the same ceiling as the whois answers.</summary>
	public const int MaxBytes = 1024 * 1024;

	private static readonly HttpClient Shared = new() { Timeout = Timeout.InfiniteTimeSpan };

	public async Task<RdapReply> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
	{
		if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
		{
			throw new ProbeException($"{url} is not an HTTPS address.");
		}

		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		try
		{
			using HttpResponseMessage response = await Shared.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, limit.Token);
			if (response.Content.Headers.ContentLength is > MaxBytes)
			{
				throw new ProbeException($"{uri.Host} would send more than the {MaxBytes / 1024} KiB this tool reads.");
			}

			await using Stream body = await response.Content.ReadAsStreamAsync(limit.Token);
			using MemoryStream buffered = new();
			byte[] chunk = new byte[16 * 1024];
			while (true)
			{
				int read = await body.ReadAsync(chunk, limit.Token);
				if (read == 0)
				{
					break;
				}

				if (buffered.Length + read > MaxBytes)
				{
					throw new ProbeException($"The RDAP answer from {uri.Host} went past the {MaxBytes / 1024} KiB this tool reads.");
				}

				buffered.Write(chunk, 0, read);
			}

			return new RdapReply((int)response.StatusCode, Encoding.UTF8.GetString(buffered.ToArray()));
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProbeException($"{uri.Host} did not answer within {timeout.TotalSeconds:0} s.");
		}
		catch (HttpRequestException ex)
		{
			throw new ProbeException($"{uri.Host} could not be reached ({ex.HttpRequestError}).", ex);
		}
	}
}
