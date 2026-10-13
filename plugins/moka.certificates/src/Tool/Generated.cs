using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Tool;

/// <summary>One Generate run's output and the objects the PFX export needs, handed from the worker back to the section, which then owns them.</summary>
internal sealed record Generated(GeneratedOutput Output, AsymmetricAlgorithm? Key, X509Certificate2? Certificate, List<X509Certificate2> Chain);
