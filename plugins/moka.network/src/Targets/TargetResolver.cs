using System.Net;
using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;

namespace Moka.Network.Targets;

/// <summary>
/// Turns a host into the one address a probe goes to, through the system resolver: the same answer ssh or a browser on
/// this machine would get, hosts file and search domains included. The DNS lookup tab is where other resolvers are asked.
/// </summary>
public static class TargetResolver
{
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

	public static async Task<IPAddress> ResolveAsync(HostTarget target, AddressChoice choice, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (target.Address is { } address)
		{
			if (!Fits(address, choice))
			{
				throw new ProbeException($"{address} is an {FamilyName(address.AddressFamily)} address, but {Choice(choice)} is picked.");
			}

			return address;
		}

		AddressFamily family = choice switch
		{
			AddressChoice.IPv4 => AddressFamily.InterNetwork,
			AddressChoice.IPv6 => AddressFamily.InterNetworkV6,
			_ => AddressFamily.Unspecified,
		};

		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		IPAddress[] found;
		try
		{
			found = await Dns.GetHostAddressesAsync(target.Host, family, limit.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new ProbeException($"Looking up {target.Host} took longer than {timeout.TotalSeconds:0} s.");
		}
		catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
		{
			throw new ProbeException(choice == AddressChoice.Any ? SocketFailures.UnknownHost(target.Host) : $"{target.Host} has no {Choice(choice)} address.", ex);
		}
		catch (SocketException ex)
		{
			throw new ProbeException($"Looking up {target.Host} failed ({ex.SocketErrorCode}).", ex);
		}

		IPAddress? first = found.FirstOrDefault(candidate => Fits(candidate, choice));
		return first ?? throw new ProbeException($"{target.Host} has no {(choice == AddressChoice.Any ? "" : Choice(choice) + " ")}address.");
	}

	public static string FamilyName(AddressFamily family) => family == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";

	private static bool Fits(IPAddress address, AddressChoice choice) => choice switch
	{
		AddressChoice.IPv4 => address.AddressFamily == AddressFamily.InterNetwork,
		AddressChoice.IPv6 => address.AddressFamily == AddressFamily.InterNetworkV6,
		_ => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6,
	};

	private static string Choice(AddressChoice choice) => choice == AddressChoice.IPv6 ? "IPv6" : "IPv4";
}
