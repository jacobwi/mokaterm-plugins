# Next: to be planned

# Built for 0.2.9: Kubernetes 1.0.0

The manager the Mokaterm plan promised as the first big plugin: a session tool for SSH with a
cluster overview, pods with describe-lite detail, tail logs that follow by polling, a one-shot
command runner over exec (not a terminal, said plainly), and confirmed rollout restart, undo and
status. On the kubernetes-1.0.0 branch, awaiting the gallery release.

# Released: Network Toolkit 1.2.0

Planned after 1.1.0 ships. The theme is the network toolkit's second identity: today every probe runs from the user's
own computer, and most of these add the server's vantage point over an SSH session.

1. **TCP ping and TCP traceroute.** Many paths rate-limit or drop ICMP; a SYN to a port with timing, and a TTL walk of
   it, see through that. One probe-kind abstraction under both, shared with the ICMP ones.
2. **Resolver race.** One query to several resolvers at once: who answered fastest, and with what. The client and the
   resolver list already exist; this is the UI on top.
3. **Custom ports in the port check.** The known-ports list stays, beside a box for a handful of named ports of the
   user's own. Still a check, not a scan: no ranges, a small cap.
4. **MTU path discovery.** A DF-ping walk that finds where a path clamps packets, the size it clamps at, and the hop
   that did it. Pairs with ping; pure socket work.
5. **A session tool.** The same probes from the server: its DNS resolution, its reach of a peer, its listening ports
   (`ss` where it exists), an mtr-style path *from* it. `AddSessionTool`, the extension point the sample plugin shows;
   the OriginNote ("Runs from this computer" / "Runs from the Mokaterm server") becomes a real choice rather than a
   fact of life.
6. **TCP dumper.** Packet capture on the server through the session: `tcpdump` (or `tcpdump -w -` parsed live) streamed
   into a filterable table — time, host and port pairs, protocol, length, flags — with a filter line and a Stop button,
   the way the log pages follow the journal. On the desktop host, a connection table of what this machine is doing now
   instead: states, peers and the owning process, refreshed while visible. Capturing on the user's own machine would
   need privileges and a driver; capturing on the server needs neither.
7. **DNS mapper.** How a name resolves, drawn: the delegation walk (root → TLD → authoritative, each server named), the
   CNAME chain as a tree with every record type at each step, a PTR sweep of a subnet into names, and the resolver
   matrix (what each resolver says, side by side). Export as text.
8. **The toolkit's UI.** One results surface the probes share (today each section draws its own), the recent-targets
   bar everywhere an address is typed, and the same list/detail rhythm the rest of Mokaterm uses.

The app side of this window is Mokaterm 0.2.6, whose plan (a general pass on the app's UI among it) lives in the
Mokaterm repository's `docs/next-release.md`.
