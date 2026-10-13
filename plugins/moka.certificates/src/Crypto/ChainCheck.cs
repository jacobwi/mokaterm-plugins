using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Whether a bundle's certificates form a chain in the order given (each one signed by the next, the way a server
/// should send them and nginx's ssl_certificate wants them), and if not, the order that would. Names and signatures
/// only: nothing is fetched and no trust store is asked.
/// </summary>
public sealed record ChainCheck
{
	public required bool InOrder { get; init; }

	/// <summary>One link between each certificate and the next.</summary>
	public required IReadOnlyList<ChainLink> Links { get; init; }

	/// <summary>The positions (from 1) in the order that forms one chain from the leaf up, when the bundle is out of order but complete.</summary>
	public IReadOnlyList<int>? CorrectOrder { get; init; }

	/// <summary>The last certificate signs itself, as a root does.</summary>
	public required bool EndsAtSelfSigned { get; init; }

	/// <summary>What the check found, in a sentence or two.</summary>
	public required string Summary { get; init; }

	public static ChainCheck Analyze(IReadOnlyList<X509Certificate2> certificates)
	{
		ArgumentNullException.ThrowIfNull(certificates);
		int count = certificates.Count;
		if (count == 0)
		{
			return new ChainCheck { InOrder = true, Links = [], EndsAtSelfSigned = false, Summary = "No certificates." };
		}

		List<ChainLink> links = [];
		for (int index = 0; index + 1 < count; index++)
		{
			links.Add(Link(certificates, index, index + 1));
		}

		X509Certificate2 last = certificates[^1];
		bool endsAtRoot = Issued(certificates, count - 1, count - 1);
		ChainLink? broken = links.FirstOrDefault(link => !link.Holds);
		if (broken is null)
		{
			string summary = endsAtRoot
				? "In order: each certificate is signed by the next, up to a self-signed root."
				: last.SubjectName.RawData.AsSpan().SequenceEqual(last.IssuerName.RawData)
					? "In order: each certificate is signed by the next, up to a self-issued root whose signature could not be checked here."
					: "In order: each certificate is signed by the next. The root is left out, which is what a server should send.";
			return new ChainCheck
			{
				InOrder = true,
				Links = links,
				EndsAtSelfSigned = endsAtRoot,
				Summary = count == 1 ? "One certificate." : summary,
			};
		}

		string why = broken.NamesMatch
			? $"Certificate {broken.Parent + 1} is named as the issuer of certificate {broken.Child + 1}, but its key does not verify the signature."
			: $"Certificate {broken.Parent + 1} did not issue certificate {broken.Child + 1}.";
		IReadOnlyList<int>? order = FindOrder(certificates);
		return new ChainCheck
		{
			InOrder = false,
			Links = links,
			CorrectOrder = order,
			EndsAtSelfSigned = endsAtRoot,
			Summary = order is null
				? $"These certificates do not form one chain. {why}"
				: $"Not in order. {why} In order they would be {string.Join(", ", order)}.",
		};
	}

	private static ChainLink Link(IReadOnlyList<X509Certificate2> certificates, int child, int parent)
	{
		X509Certificate2 issued = certificates[child];
		X509Certificate2 issuer = certificates[parent];
		bool names = issued.IssuerName.RawData.AsSpan().SequenceEqual(issuer.SubjectName.RawData);
		return new ChainLink(child, parent, names, names ? SignatureCheck.Verify(issued.RawData, issuer.PublicKey) : false);
	}

	private static bool Issued(IReadOnlyList<X509Certificate2> certificates, int child, int parent) => Link(certificates, child, parent) is
	{
		NamesMatch: true,
		SignatureValid: true,
	};

	// From the one certificate that issued none of the others, follow each issuer up; that is the order when it reaches
	// every certificate exactly once.
	private static List<int>? FindOrder(IReadOnlyList<X509Certificate2> certificates)
	{
		int count = certificates.Count;
		int?[] issuerOf = new int?[count];
		for (int child = 0; child < count; child++)
		{
			for (int parent = 0; parent < count; parent++)
			{
				if (parent != child && Link(certificates, child, parent).Holds)
				{
					issuerOf[child] = parent;
					break;
				}
			}
		}

		List<int> leaves = [.. Enumerable.Range(0, count).Where(candidate => !issuerOf.Contains(candidate))];
		if (leaves.Count != 1)
		{
			return null;
		}

		List<int> order = [];
		HashSet<int> seen = [];
		for (int? current = leaves[0]; current is { } index && seen.Add(index); current = issuerOf[index])
		{
			order.Add(index + 1);
		}

		return order.Count == count ? order : null;
	}
}
