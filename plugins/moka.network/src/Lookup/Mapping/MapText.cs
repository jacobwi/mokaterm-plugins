using System.Globalization;
using System.Text;

namespace Moka.Network.Lookup.Mapping;

/// <summary>
/// The whole map as indented text, one block per view that has run, for the copy button. The same words the section
/// draws, so what lands on the clipboard is what was on screen.
/// </summary>
public static class MapText
{
	public static string Render(MapViews views)
	{
		ArgumentNullException.ThrowIfNull(views);
		StringBuilder text = new();
		if (views.Name.Length > 0)
		{
			// A sweep run on its own has no name behind it, so it stands without a header.
			text.Append(CultureInfo.InvariantCulture, $"DNS map of {views.Name}, {DnsNames.TypeName(views.Type)} records\n");
		}

		if (views.Walk is { } walk)
		{
			text.Append("\nDelegation, asked of the roots:\n");
			for (int index = 0; index < walk.Steps.Count; index++)
			{
				DelegationStep step = walk.Steps[index];
				string when = step.Exchange is { } exchange ? $", {Milliseconds(exchange.Elapsed)}" : "";
				text.Append(CultureInfo.InvariantCulture, $"  {index + 1}. {step.ServerName} ({DnsClient.Describe(step.Server)}){when} — {step.Outcome}\n");
			}

			text.Append(CultureInfo.InvariantCulture, $"  {walk.Summary}\n");
		}

		if (views.Chain is { } chain)
		{
			text.Append("\nCNAME chain:\n");
			foreach (CnameHop hop in chain.Hops)
			{
				text.Append(CultureInfo.InvariantCulture, $"  {hop.Name}\n");
				foreach (DnsRecord record in hop.Records)
				{
					text.Append(CultureInfo.InvariantCulture, $"    {record.TypeName} {record.Data} ({DnsClient.Describe(hop.Server)}, {Milliseconds(hop.Elapsed)})\n");
				}

				if (hop.Records.Count == 0)
				{
					text.Append(CultureInfo.InvariantCulture, $"    no records ({DnsClient.Describe(hop.Server)}, {Milliseconds(hop.Elapsed)})\n");
				}
			}

			text.Append(CultureInfo.InvariantCulture, $"  {chain.Summary}\n");
		}

		if (views.Matrix is { } matrix)
		{
			text.Append("\nResolvers, the same question:\n");
			foreach (ResolverMatrixRow row in matrix.Rows)
			{
				if (!row.Answered)
				{
					text.Append(CultureInfo.InvariantCulture, $"  {row.Resolver}: could not be asked — {row.Failure}\n");
					continue;
				}

				string answer = row.Answer is { } records ? $", {records}" : "";
				text.Append(CultureInfo.InvariantCulture, $"  {row.Resolver}: {row.Code}{answer}, {Milliseconds(row.Elapsed)}\n");
			}

			foreach (string disagreement in matrix.Disagreements)
			{
				text.Append(CultureInfo.InvariantCulture, $"  ! {disagreement}\n");
			}
		}

		if (views.Sweep is { } sweep && views.SweepPrefix is { } prefix)
		{
			text.Append(CultureInfo.InvariantCulture, $"\nPTR sweep of {prefix}:\n");
			foreach (PtrSweepItem item in sweep.Items.Where(item => !item.Silent))
			{
				text.Append(CultureInfo.InvariantCulture, $"  {item.Address}  {item.Name}\n");
			}

			text.Append(CultureInfo.InvariantCulture, $"  {sweep.Found} {Plural(sweep.Found, "name")}, {sweep.Silent} {Plural(sweep.Silent, "address")} with no name\n");
		}

		return text.ToString().TrimStart('\n').TrimEnd();
	}

	private static string Milliseconds(TimeSpan elapsed) => elapsed.TotalMilliseconds < 10
		? elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms"
		: elapsed.TotalMilliseconds.ToString("N0", CultureInfo.InvariantCulture) + " ms";

	private static string Plural(int count, string what) => count == 1 ? what : $"{what}s";
}
