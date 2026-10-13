namespace Moka.Network.Tool;

/// <summary>The two ports magic packets go to: 9 (discard), which most cards and tools use, and 7 (echo).</summary>
public enum WakePort
{
	Discard = 9,
	Echo = 7,
}
