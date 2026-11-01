using Moka.Kubernetes.Server;

namespace Moka.Kubernetes.Tests.Server;

/// <summary>The node listing: five columns, promised and unpadded.</summary>
public sealed class NodeListingTests
{
	[Fact]
	public void Parse_ReadsEveryNode()
	{
		IReadOnlyList<NodeRow> rows = NodeListing.Parse(Fixtures.Nodes);

		Assert.Equal(3, rows.Count);
		Assert.Equal("node-a.example", rows[0].Name);
		Assert.Equal("Ready", rows[0].Status);
		Assert.Equal("control-plane", rows[0].Roles);
		Assert.Equal("23d", rows[0].Age);
		Assert.Equal("v1.30.4", rows[0].Version);
	}

	[Fact]
	public void Parse_ANotReadyNode_IsNotReady()
	{
		NodeRow row = Assert.Single(NodeListing.Parse("node-c.example   NotReady   worker   4d   v1.30.3\n"));

		Assert.False(row.Ready);
		Assert.True(NodeListing.Parse("node-a.example   Ready   <none>   23d   v1.30.4\n").Single().Ready);
	}

	[Fact]
	public void Parse_AReadyNodeThatTakesNoPods_IsStillReady()
	{
		NodeRow row = Assert.Single(NodeListing.Parse("node-a.example   Ready,SchedulingDisabled   control-plane   23d   v1.30.4\n"));

		Assert.True(row.Ready);
		Assert.Equal("Ready,SchedulingDisabled", row.Status);
	}

	[Theory]
	[InlineData("")]
	[InlineData("NAME STATUS ROLES AGE VERSION")]
	[InlineData("NAME   STATUS   ROLES   AGE   VERSION\nnode-a.example   Ready   <none>   23d   v1.30.4\n")]
	[InlineData("node-a.example   Ready\n")]
	[InlineData("some noise that is not a node at all\n")]
	public void Parse_OfAnythingThatIsNotANodeRow_ReadsNothing(string text)
	{
		NodeListing.Parse(text);
	}

	[Fact]
	public void Parse_SkipsTheHeaderAndKeepsTheRowsUnderIt()
	{
		IReadOnlyList<NodeRow> rows = NodeListing.Parse("NAME STATUS ROLES AGE VERSION\nnode-a.example   Ready   <none>   23d   v1.30.4\n");

		NodeRow row = Assert.Single(rows);
		Assert.Equal("node-a.example", row.Name);
	}
}

/// <summary>The pods listing, wide and without headers, with and without the namespace column.</summary>
public sealed class PodListingTests
{
	[Fact]
	public void Parse_ReadsTheColumnsIncludingTheRestartAgeParenthetical()
	{
		IReadOnlyList<PodRow> rows = PodListing.Parse(Fixtures.PodsWide, hasNamespace: false);

		Assert.Equal(4, rows.Count);
		PodRow running = rows[0];
		Assert.Equal("web-6799fc88d1-kx4t2", running.Name);
		Assert.Equal("1/1", running.Ready);
		Assert.Equal("Running", running.Status);
		Assert.Equal("0", running.Restarts);
		Assert.Equal("5d", running.Age);
		Assert.Equal("10.244.0.15", running.Ip);
		Assert.Equal("node-a.example", running.Node);
		Assert.Equal("", running.Namespace);

		PodRow crashing = rows[1];
		Assert.Equal("CrashLoopBackOff", crashing.Status);
		Assert.Equal("5", crashing.Restarts);
		Assert.Equal("5d", crashing.Age);
		Assert.Equal("node-b.example", crashing.Node);
	}

	[Fact]
	public void Parse_WithTheNamespaceColumn_ReadsItAsTheFirstWord()
	{
		IReadOnlyList<PodRow> rows = PodListing.Parse(Fixtures.PodsAllNamespaces, hasNamespace: true);

		Assert.Equal(3, rows.Count);
		Assert.Equal("kube-system", rows[1].Namespace);
		Assert.Equal("coredns-55cdc9b7b-8xh2r", rows[1].Name);

		// A pending pod has no node and no IP, and kubectl says <none> for both, which is honest enough to keep.
		PodRow pending = rows[2];
		Assert.Equal("Pending", pending.Status);
		Assert.Equal("<none>", pending.Node);
	}

	[Fact]
	public void Parse_TheStatesThatNeedLookingAt_AreNotSettled()
	{
		IReadOnlyList<PodRow> rows = PodListing.Parse(Fixtures.PodsWide, hasNamespace: false);

		Assert.True(rows[0].Settled);   // Running
		Assert.False(rows[1].Settled);  // CrashLoopBackOff
		Assert.True(rows[2].Settled);   // Completed
		Assert.True(rows[3].Settled);   // Running
		Assert.False(PodListing.Parse("p 0/1 Pending 0 1s <none> <none>\n", hasNamespace: false).Single().Settled);
		Assert.False(PodListing.Parse("p 0/1 ImagePullBackOff 0 1s 10.0.0.1 node\n", hasNamespace: false).Single().Settled);
		Assert.True(PodListing.Parse("p 0/1 ContainerCreating 0 1s <none> <none>\n", hasNamespace: false).Single().Settled);
		Assert.True(PodListing.Parse("p 1/1 Succeeded 0 1s 10.0.0.1 node\n", hasNamespace: false).Single().Settled);
	}

	[Fact]
	public void Parse_ARestartAgeWithoutTheParentheticalStillCountsTheRestarts()
	{
		PodRow row = PodListing.Parse("web 1/1 Running 12 40d 10.244.0.31 node-a.example <none> <none>\n", hasNamespace: false).Single();

		Assert.Equal("12", row.Restarts);
		Assert.Equal("40d", row.Age);
	}

	[Theory]
	[InlineData("")]
	[InlineData(Fixtures.PodsHeaderOnly)]
	[InlineData(Fixtures.PodsBrokenLine)]
	public void Parse_OfLinesThatAreNotPodRows_ReadsNothing(string text)
	{
		Assert.Empty(PodListing.Parse(text, hasNamespace: false));
	}

	[Fact]
	public void Parse_WithoutTheTwoTrailingColumns_StillReadsThroughTheNode()
	{
		PodRow row = PodListing.Parse("web 1/1 Running 0 5d 10.244.0.15 node-a.example\n", hasNamespace: false).Single();

		Assert.Equal("node-a.example", row.Node);
	}
}

/// <summary>The deployments listing, five columns or six with a namespace.</summary>
public sealed class DeploymentListingTests
{
	[Fact]
	public void Parse_ReadsTheCounts()
	{
		IReadOnlyList<DeploymentRow> rows = DeploymentListing.Parse(Fixtures.Deployments, hasNamespace: false);

		Assert.Equal(2, rows.Count);
		Assert.Equal("web", rows[0].Name);
		Assert.Equal("3/3", rows[0].Ready);
		Assert.Equal("3", rows[0].UpToDate);
		Assert.Equal("3", rows[0].Available);
		Assert.Equal("5d2h", rows[0].Age);
		Assert.Equal("", rows[0].Namespace);
	}

	[Fact]
	public void Parse_WithTheNamespaceColumn_ReadsItFirst()
	{
		IReadOnlyList<DeploymentRow> rows = DeploymentListing.Parse(Fixtures.DeploymentsAllNamespaces, hasNamespace: true);

		Assert.Equal(2, rows.Count);
		Assert.Equal("monitoring", rows[1].Namespace);
		Assert.Equal("prometheus", rows[1].Name);
	}

	[Fact]
	public void Parse_KnowsWhichDeploymentsStandFullyReady()
	{
		IReadOnlyList<DeploymentRow> rows = DeploymentListing.Parse(Fixtures.Deployments, hasNamespace: false);

		Assert.True(rows[0].FullyReady);
		Assert.False(rows[1].FullyReady);
	}

	[Theory]
	[InlineData("")]
	[InlineData("NAME READY UP-TO-DATE AVAILABLE AGE")]
	[InlineData("web 3/3")]
	public void Parse_OfLinesThatAreNotDeployments_ReadsNothing(string text)
	{
		Assert.Empty(DeploymentListing.Parse(text, hasNamespace: false));
	}
}
