using System.Net.Sockets;
using System.Text;
using Moka.Network.Targets;
using Mokaterm.Abstractions.Protocols;

namespace Moka.Network.Whois;

/// <summary>Whois over TCP port 43 (RFC 3912): the query and CRLF, then everything until the server closes.</summary>
public sealed class TcpWhoisTransport : IWhoisTransport
{
	public const int Port = 43;

	public static TcpWhoisTransport Instance { get; } = new();

	public async Task<WhoisRaw> AskAsync(string server, string query, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(server);
		ArgumentNullException.ThrowIfNull(query);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
		string endpoint = HostEndpoint.Format(server, Port);
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		using TcpClient client = new();
		try
		{
			await client.ConnectAsync(server, Port, limit.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProbeException($"{endpoint} did not answer within {timeout.TotalSeconds:0} s.");
		}
		catch (SocketException ex)
		{
			throw new ProbeException(ex.SocketErrorCode switch
			{
				SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => SocketFailures.UnknownHost(server),
				SocketError.ConnectionRefused => $"{endpoint} refused the connection.",
				SocketError.NetworkUnreachable or SocketError.HostUnreachable => $"There is no route to {endpoint}.",
				_ => $"Could not connect to {endpoint} ({ex.SocketErrorCode}).",
			}, ex);
		}

		NetworkStream stream = client.GetStream();
		byte[] buffer = new byte[Math.Min(maxBytes, 64 * 1024)];
		using MemoryStream received = new();
		bool cut = false;
		bool timedOut = false;
		try
		{
			await stream.WriteAsync(Encoding.ASCII.GetBytes(query + "\r\n"), limit.Token);
			while (true)
			{
				int room = maxBytes - (int)received.Length;
				if (room <= 0)
				{
					cut = true;
					break;
				}

				int read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(room, buffer.Length)), limit.Token);
				if (read == 0)
				{
					break;
				}

				received.Write(buffer, 0, read);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			if (received.Length == 0)
			{
				throw new ProbeException($"{endpoint} accepted the connection but sent nothing within {timeout.TotalSeconds:0} s.");
			}

			timedOut = true;
		}
		catch (IOException ex)
		{
			if (received.Length == 0)
			{
				throw new ProbeException($"{endpoint} closed the connection without an answer.", ex);
			}
		}

		return new WhoisRaw(received.ToArray(), cut, timedOut);
	}
}
