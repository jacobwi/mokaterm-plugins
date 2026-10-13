using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Tests;

/// <summary>A three-certificate chain with every key, disposed together.</summary>
internal sealed record TestChain(
	X509Certificate2 Root,
	AsymmetricAlgorithm RootKey,
	X509Certificate2 Intermediate,
	AsymmetricAlgorithm IntermediateKey,
	X509Certificate2 Leaf,
	AsymmetricAlgorithm LeafKey) : IDisposable
{
	public void Dispose()
	{
		Root.Dispose();
		RootKey.Dispose();
		Intermediate.Dispose();
		IntermediateKey.Dispose();
		Leaf.Dispose();
		LeafKey.Dispose();
	}
}
