namespace Moka.Certificates.ServerFiles;

/// <summary>A block nginx opened with <c>{</c>, and for a server block its names and certificates.</summary>
internal sealed class NginxBlock(string name)
{
	public string Name { get; } = name;

	public List<string> Names { get; } = [];

	public List<string> Certificates { get; } = [];
}
