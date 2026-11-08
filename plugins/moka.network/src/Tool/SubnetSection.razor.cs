using System.Globalization;
using System.Net;
using System.Numerics;
using Microsoft.AspNetCore.Components;
using Moka.Network.Subnets;
using Moka.Network.Targets;

namespace Moka.Network.Tool;

/// <summary>
/// The subnet calculator: an IPv4 or IPv6 prefix worked out as it is typed, what kind of range it is, whether an
/// address falls inside, and the prefix split into smaller ones. Pure arithmetic, nothing leaves the machine.
/// </summary>
public sealed partial class SubnetSection : ComponentBase
{
	private string _input = "192.168.1.10/24";
	private string _inside = "";
	private string _split = "";
	private SubnetInfo? _info;
	private string? _problem;

	/// <summary>What is wrong with the text, once there is text to be wrong.</summary>
	private string? ShownProblem => _input.Trim().Length > 0 ? _problem : null;

	/// <summary>Split subnets listed per page; a split can make up to <see cref="SubnetMath.MaxSplitRows"/>.</summary>
	private const int SplitPage = 32;

	private string Summary => _info is { } info ? $"{info.Cidr}, {CountText(info.HostCount, info)} hosts" : "IPv4 and IPv6 prefixes";

	protected override void OnInitialized() => Calculate();

	/// <summary>A count in full, and for a large IPv6 one the power of two it is, which reads better than 20 digits.</summary>
	private static string CountText(BigInteger count, SubnetInfo info)
	{
		string text = count.ToString("N0", CultureInfo.CurrentCulture);
		int bits = info.Bits - info.PrefixLength;
		return info.IsIPv6 && bits > 16 && count == BigInteger.One << bits ? $"2^{bits} ({text})" : text;
	}

	/// <summary>A count short enough for a tile: the power of two alone for a large IPv6 one.</summary>
	private static string ShortCount(BigInteger count, SubnetInfo info)
	{
		int bits = info.Bits - info.PrefixLength;
		return info.IsIPv6 && bits > 16 && count == BigInteger.One << bits
			? $"2^{bits}"
			: count.ToString("N0", CultureInfo.CurrentCulture);
	}

	/// <summary>The count in full under a tile that shows only its power of two, and nothing otherwise.</summary>
	private static string? FullCount(BigInteger count, SubnetInfo info) =>
		ShortCount(count, info).StartsWith("2^", StringComparison.Ordinal) ? count.ToString("N0", CultureInfo.CurrentCulture) : null;

	private static string SplitSummary(SubnetInfo info, int prefix, BigInteger total, int shown)
	{
		string count = total.ToString("N0", CultureInfo.CurrentCulture);
		string text = $"{info.Cidr} makes {count} subnets of /{prefix}";
		return total > shown ? $"{text}; the first {shown} are listed." : text + ".";
	}

	private (bool Inside, bool Problem, string Text)? InsideResult(SubnetInfo info)
	{
		string text = _inside.Trim();
		if (text.Length == 0)
		{
			return null;
		}

		if (!AddressText.TryParse(text, out IPAddress? address))
		{
			return (false, true, $"{text} is not an IPv4 or IPv6 address.");
		}

		if (SubnetMath.Contains(info, address))
		{
			return (true, false, $"Yes: {address} is inside {info.Cidr}.");
		}

		bool sameFamily = address.IsIPv4MappedToIPv6 ? !info.IsIPv6 : (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) == info.IsIPv6;
		return sameFamily
			? (false, false, $"No: {address} is outside {info.Cidr}, which runs from {info.Network} to {LastAddress(info)}.")
			: (false, true, $"{address} is {(info.IsIPv6 ? "IPv4" : "IPv6")} and {info.Cidr} is not, so it cannot be inside.");
	}

	private static IPAddress LastAddress(SubnetInfo info) => info.Broadcast ?? info.LastHost;

	private void OnInputChanged(string? value)
	{
		_input = value ?? "";
		Calculate();
	}

	private void Calculate()
	{
		if (SubnetMath.TryCalculate(_input, out SubnetInfo? info, out string? problem))
		{
			_info = info;
			_problem = null;
		}
		else
		{
			_info = null;
			_problem = problem;
		}
	}
}
