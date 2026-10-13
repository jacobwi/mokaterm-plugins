using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Tls;

/// <summary>What a chain build's status flags mean, in sentences a person can act on.</summary>
public static class ChainProblems
{
	// Revocation is never checked, so its two flags say nothing about the chain.
	private const X509ChainStatusFlags Ignored = X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

	private static readonly (X509ChainStatusFlags Flag, string Words)[] Sentences =
	[
		(X509ChainStatusFlags.UntrustedRoot, "The chain ends at a root this machine does not trust."),
		(X509ChainStatusFlags.PartialChain, "The chain is incomplete: an intermediate certificate is missing and could not be found."),
		(X509ChainStatusFlags.NotTimeValid, "A certificate in the chain has expired or is not valid yet."),
		(X509ChainStatusFlags.NotSignatureValid, "A signature in the chain does not verify."),
		(X509ChainStatusFlags.Revoked, "A certificate in the chain was revoked."),
		(X509ChainStatusFlags.ExplicitDistrust, "A certificate in the chain is marked as distrusted on this machine."),
		(X509ChainStatusFlags.NotValidForUsage, "A certificate in the chain is not meant for TLS servers (its key usage or extended key usage says otherwise)."),
		(X509ChainStatusFlags.InvalidBasicConstraints, "A certificate that is not a CA signed another certificate in the chain."),
		(X509ChainStatusFlags.InvalidNameConstraints | X509ChainStatusFlags.HasNotSupportedNameConstraint | X509ChainStatusFlags.HasNotDefinedNameConstraint
			| X509ChainStatusFlags.HasNotPermittedNameConstraint | X509ChainStatusFlags.HasExcludedNameConstraint, "A name breaks the name constraints of a CA above it."),
		(X509ChainStatusFlags.InvalidPolicyConstraints | X509ChainStatusFlags.NoIssuanceChainPolicy, "The chain breaks a CA's certificate policy constraints."),
		(X509ChainStatusFlags.InvalidExtension, "A certificate in the chain has an extension that is not valid."),
		(X509ChainStatusFlags.HasNotSupportedCriticalExtension, "A certificate in the chain has a critical extension this machine does not understand."),
		(X509ChainStatusFlags.Cyclic, "The chain loops back on itself."),
		(X509ChainStatusFlags.HasWeakSignature, "A certificate in the chain is signed with a weak algorithm (MD5 or SHA-1)."),
		(X509ChainStatusFlags.NotTimeNested, "A certificate's validity is not inside its issuer's."),
		(X509ChainStatusFlags.CtlNotTimeValid | X509ChainStatusFlags.CtlNotSignatureValid | X509ChainStatusFlags.CtlNotValidForUsage, "A trust list used for the chain is not valid."),
	];

	/// <summary>True when nothing but the revocation flags is set.</summary>
	public static bool IsClean(X509ChainStatusFlags flags) => (flags & ~Ignored) == X509ChainStatusFlags.NoError;

	public static IReadOnlyList<string> Describe(X509ChainStatusFlags flags)
	{
		X509ChainStatusFlags remaining = flags & ~Ignored;
		List<string> sentences = [];
		foreach ((X509ChainStatusFlags flag, string words) in Sentences)
		{
			if ((remaining & flag) != 0)
			{
				sentences.Add(words);
				remaining &= ~flag;
			}
		}

		if (remaining != X509ChainStatusFlags.NoError)
		{
			sentences.Add($"The chain has other problems: {remaining}.");
		}

		return sentences;
	}
}
