using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Moka.Network.Targets;

namespace Moka.Network.Ports;

/// <summary>
/// Opens one TCP connection and closes it again. Nothing is sent; with the greeting asked for, it listens for up to
/// <see cref="GreetingWait"/> for what servers that speak first (SSH, SMTP, FTP) say, keeping at most
/// <see cref="MaxGreetingBytes"/>, which are shown as plain text with control characters taken out.
/// </summary>
public static class PortCheck
{
	public const int MaxGreetingBytes = 512;

	public static readonly TimeSpan GreetingWait = TimeSpan.FromSeconds(1);

	public static async Task<PortResult> CheckAsync(IPAddress address, int port, TimeSpan timeout, bool readGreeting, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(address);
		IPEndPoint endpoint = new(address, port);
		long started = Stopwatch.GetTimestamp();
		using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		try
		{
			await socket.ConnectAsync(endpoint, limit.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return Result(PortState.TimedOut, null);
		}
		catch (SocketException ex)
		{
			return ex.SocketErrorCode switch
			{
				SocketError.ConnectionRefused => Result(PortState.Refused, null),
				SocketError.TimedOut => Result(PortState.TimedOut, null),
				SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.HostDown => Result(PortState.Unreachable, "No route to the machine."),
				SocketError.AddressNotAvailable or SocketError.AddressFamilyNotSupported => Result(PortState.Failed, $"This machine has no {TargetResolver.FamilyName(address.AddressFamily)} route to use."),
				SocketError.AccessDenied => Result(PortState.Failed, "This machine's firewall refused the connection."),
				_ => Result(PortState.Failed, $"The connection failed ({ex.SocketErrorCode})."),
			};
		}

		TimeSpan connected = Stopwatch.GetElapsedTime(started);
		if (!readGreeting)
		{
			return Result(PortState.Open, null, connected);
		}

		(string? greeting, int bytes) = await ReadGreetingAsync(socket, cancellationToken);
		return Result(PortState.Open, null, connected) with { Greeting = greeting, GreetingBytes = bytes };

		PortResult Result(PortState state, string? detail, TimeSpan? elapsed = null) => new()
		{
			Port = port,
			State = state,
			Endpoint = endpoint,
			Elapsed = elapsed ?? Stopwatch.GetElapsedTime(started),
			Detail = detail,
		};
	}

	/// <summary>Reads until the cap, the end of the stream or the wait runs out, whichever comes first.</summary>
	private static async Task<(string? Text, int Bytes)> ReadGreetingAsync(Socket socket, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[MaxGreetingBytes];
		int filled = 0;
		using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		wait.CancelAfter(GreetingWait);
		try
		{
			while (filled < buffer.Length)
			{
				int read = await socket.ReceiveAsync(buffer.AsMemory(filled), SocketFlags.None, wait.Token);
				if (read == 0)
				{
					break;
				}

				filled += read;
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// The wait is over; whatever arrived is the greeting.
		}
		catch (SocketException)
		{
			// The server dropped the connection; whatever arrived before that still counts.
		}

		return filled == 0 ? (null, 0) : (PlainText.FromBytes(buffer.AsSpan(0, filled)), filled);
	}
}
