namespace Moka.Passwords.Tests;

/// <summary>Hands a generator the picks a test chose, in order, and checks each one is inside what the generator asked for.</summary>
internal sealed class ScriptedPicks(params int[] picks)
{
	private int _next;

	public int Used => _next;

	public int Pick(int count)
	{
		Assert.True(_next < picks.Length, "The generator asked for more picks than the test scripted.");
		int value = picks[_next++];
		Assert.InRange(value, 0, count - 1);
		return value;
	}
}
