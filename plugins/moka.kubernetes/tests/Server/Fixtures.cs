namespace Moka.Kubernetes.Tests.Server;

/// <summary>
/// The answers the tests feed the parsers, shaped exactly as kubectl prints them: real columns, the wrinkle of a
/// parenthesised restart age, and the honest failures a server without a cluster answers with.
/// </summary>
public static class Fixtures
{
	public const string VersionWithServer =
		"""
		{
		  "clientVersion": {
		    "major": "1",
		    "minor": "31",
		    "gitVersion": "v1.31.1",
		    "gitCommit": "7f11a5f4b8b0dbb0d58c9f2d3a8f8e0a5a4b3c2d",
		    "buildDate": "2024-09-11T21:26:32Z",
		    "goVersion": "go1.22.4",
		    "compiler": "gc",
		    "platform": "linux/amd64"
		  },
		  "kustomizeVersion": "v5.4.2",
		  "serverVersion": {
		    "major": "1",
		    "minor": "30",
		    "gitVersion": "v1.30.4",
		    "gitCommit": "a9d5c62c45t5c1e4b7d8e9f0a1b2c3d4e5f60718",
		    "buildDate": "2024-08-13T07:28:49Z",
		    "goVersion": "go1.22.5",
		    "compiler": "gc",
		    "platform": "linux/amd64"
		  }
		}
		""";

	public const string VersionClientOnly =
		"""
		{
		  "clientVersion": {
		    "major": "1",
		    "minor": "28",
		    "gitVersion": "v1.28.9",
		    "buildDate": "2024-05-25T18:36:36Z"
		  }
		}
		""";

	/// <summary>What kubectl 1.28 answers when the cluster is reachable but deprecates this form.</summary>
	public const string VersionDeprecatedNotice =
		"""
		WARNING: This version information is deprecated and will be removed in a future release. View the current version with kubectl version --short.
		{
		  "clientVersion": { "gitVersion": "v1.28.9" },
		  "serverVersion": { "gitVersion": "v1.29.2" }
		}
		""";

	/// <summary>The JSON still arrives on stdout with the error on stderr; the exit status is not 0.</summary>
	public const string NoKubeconfigError =
		"error: no configuration has been provided, try setting KUBERNETES_MASTER environment variable";

	public const string ConnectionRefusedError =
		"The connection to the server localhost:8080 was refused - specify the right host or port";

	public const string UnauthorizedError =
		"error: You must be logged in to the server (Unauthorized)";

	public const string ForbiddenError =
		"Error from server (Forbidden): pods is forbidden: User \"deploy\" cannot list resource \"pods\" in API group \"\" at the cluster scope";

	public const string NoSuchHostError =
		"Unable to connect to the server: dial tcp: lookup kubernetes.default on 10.0.0.53: no such host";

	public const string ContextNotFoundError =
		"error: context was not found for specified context: staging";

	public const string CertificateError =
		"Unable to connect to the server: x509: certificate signed by unknown authority";

	public const string NotFoundError =
		"Error from server (NotFound): pods \"web-6799fc88d1-kx4t2\" not found";

	public const string ClusterInfo =
		"""
		Kubernetes control plane is running at https://192.0.2.10:6443
		CoreDNS is running at https://192.0.2.10:6443/api/v1/namespaces/kube-system/services/kube-dns:dns/proxy

		To further debug and diagnose cluster problems, use 'kubectl cluster-info dump'.
		""";

	public const string Nodes =
		"""
		node-a.example   Ready    control-plane   23d   v1.30.4
		node-b.example   Ready    <none>          23d   v1.30.4
		node-c.example   NotReady worker          4d    v1.30.3
		""";

	/// <summary>The listing without a namespace column, with the restart-age wrinkle in the middle row.</summary>
	public const string PodsWide =
		"""
		web-6799fc88d1-kx4t2          1/1   Running            0        5d    10.244.0.15   node-a.example   <none>           <none>
		worker-7d9f4c8b6c-q2r7v       0/1   CrashLoopBackOff   5 (2m ago)   5d    10.244.1.22   node-b.example   <none>           <none>
		job-scan-28fhk                0/1   Completed          0        8m    10.244.2.9    node-c.example   <none>           <none>
		redis-74dbcd9c7-abcde         2/2   Running            12       40d   10.244.0.31   node-a.example   <none>           <none>
		""";

	/// <summary>The same listing with -A, a namespace column first.</summary>
	public const string PodsAllNamespaces =
		"""
		NAMESPACE    NAME                     READY   STATUS             RESTARTS      AGE   IP            NODE            NOMINATED_NODE   READINESS_GATES
		default      web-6799fc88d1-kx4t2     1/1     Running            0             5d    10.244.0.15   node-a.example   <none>           <none>
		kube-system  coredns-55cdc9b7b-8xh2r  1/1     Running            0             23d   10.244.0.4    node-a.example   <none>           <none>
		monitoring   prometheus-0             2/2     Pending            0             2m    <none>        <none>          <none>           <none>
		""";

	public const string PodsHeaderOnly =
		"NAME                         READY   STATUS    RESTARTS   AGE   IP       NODE     NOMINATED_NODE   READINESS_GATES";

	/// <summary>A line too short to be a pod row whole, which the reader skips rather than guesses at.</summary>
	public const string PodsBrokenLine = "half-a-row 1/1 Running";

	public const string Deployments =
		"""
		web          3/3   3   3   5d2h
		worker       1/4   4   1   12d
		""";

	public const string DeploymentsAllNamespaces =
		"""
		NAMESPACE     NAME          READY   UP-TO-DATE   AVAILABLE   AGE
		default       web           3/3     3            3           5d2h
		monitoring    prometheus    1/1     1            1           40d
		""";

	/// <summary>One pod's JSON record, trimmed to the parts the reader walks but keeping their shapes: a waiting container, a running one, conditions, QoS.</summary>
	public const string PodJson =
		"""
		{
		  "metadata": {
		    "name": "web-6799fc88d1-kx4t2",
		    "namespace": "default"
		  },
		  "spec": {
		    "nodeName": "node-a.example",
		    "restartPolicy": "Always",
		    "containers": [
		      { "name": "app", "image": "registry.example/web:1.4.2" },
		      { "name": "metrics", "image": "registry.example/metrics:0.9" }
		    ]
		  },
		  "status": {
		    "phase": "Running",
		    "podIP": "10.244.0.15",
		    "startTime": "2026-10-27T08:14:02Z",
		    "qosClass": "Burstable",
		    "conditions": [
		      { "type": "PodScheduled", "status": "True" },
		      { "type": "Ready", "status": "False", "reason": "ContainersNotReady", "message": "containers with unready status: [app]" }
		    ],
		    "containerStatuses": [
		      {
		        "name": "app",
		        "ready": false,
		        "restartCount": 5,
		        "state": {
		          "waiting": {
		            "reason": "CrashLoopBackOff",
		            "message": "back-off 5m0s restarting failed container"
		          }
		        },
		        "lastState": {
		          "terminated": { "reason": "Error", "exitCode": 1 }
		        }
		      },
		      {
		        "name": "metrics",
		        "ready": true,
		        "restartCount": 0,
		        "state": {
		          "running": { "startedAt": "2026-10-27T08:14:04Z" }
		        }
		      }
		    ]
		  }
		}
		""";

	/// <summary>A pod still being scheduled: no node, no IP, no container statuses at all.</summary>
	public const string PodJsonPending =
		"""
		{
		  "metadata": { "name": "prometheus-0", "namespace": "monitoring" },
		  "spec": {
		    "restartPolicy": "Always",
		    "containers": [
		      { "name": "server", "image": "prom/prometheus:v2.54.1" },
		      { "name": "config-reloader", "image": "prom/configmap-reloader:v0.8.0" }
		    ]
		  },
		  "status": {
		    "phase": "Pending",
		    "qosClass": "Burstable",
		    "conditions": [
		      { "type": "PodScheduled", "status": "False", "reason": "Unschedulable", "message": "0/3 nodes are available: 3 Insufficient memory." }
		    ]
		  }
		}
		""";

	/// <summary>Log lines as kubectl prints them with --timestamps: RFC 3339 with nanoseconds, a space, then the line.</summary>
	public const string LogTail =
		"""
		2026-11-01T09:00:00.123456789Z starting worker version 1.4.2
		2026-11-01T09:00:00.246802468Z connected to redis at 10.244.0.31:6379
		2026-11-01T09:00:02.000000001Z job 1 done in 1.7 s
		""";

	/// <summary>A log line kubectl writes when the container prints nothing timestampable.</summary>
	public const string LogLineWithoutTime = "just words on a line";
}
