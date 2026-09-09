# Transport and generated-code review

This review follows baseline `afd0588` and concentrates on socket lifecycle, packet ownership, transmissions, relay topology, concurrent authentication, and emitted RPC code.

## Fixes

| Area | Failure | Correction |
| --- | --- | --- |
| Socket lifecycle | Restart reused a disposed receive core; concurrent starts could replace the socket under an existing receiver. | Serialize start/stop and give each new receive core its own UDP client and address-family snapshot. |
| Socket boundary | A header-sized protocol packet was treated as a local health-check echo. | Restrict health-check handling to packets shorter than the protocol header. |
| Receive buffers | An exception after sharing a receive buffer skipped the ownership check before reuse. | Perform the check in `finally`, including exceptional receive paths. |
| Packet shutdown | Pending requests and late response work could retain queued buffers after the sender stopped. | Close packet and relay queues, complete pending packet requests, return leases, and reject subsequent enqueues. Complete packet waiters outside the collection lock. |
| Transmission waits | Error responses were mistaken for abandoned waits, allowing the same response lease to be returned twice. Short timeouts waited for the entire polling interval. | Track whether the response came from the task independently of its status, and calculate waits from monotonic elapsed time. Apply the same timeout correction to control packets. |
| State publication | Connection state and authentication references were read across threads without explicit publication. | Use volatile state/reference access and atomic authentication installation. |
| Authentication | Concurrent requests could replace the established identity. The client cached rejected tokens and reported success on a retry. | Validate each received token, install the first identity atomically, reject a different public key, and cache only accepted client tokens. |
| Relay topology | Failed setup left a new hop published; cleanup retained hops beyond a broken link. Assignment arrays could mutate cached keys. | Serialize topology changes, publish only after setup succeeds, recheck the snapshot after awaiting, remove dependent outer hops, and copy assignment key material. |
| Relay accounting | Wrong source endpoints and failed authentication consumed relay points. | Charge points only on accepted delivery or forwarding paths. |
| Dispatch failures | Service-factory exceptions and missing stream services could bypass cleanup. Throwing custom responders or unknown data kinds could retain request buffers. | Include service resolution in exception cleanup, close failed streams, and release failed or unsupported synchronous requests. |
| Generated filters | DI filters referenced a nonexistent context property. Continuations ignored their context argument. | Resolve DI through the server connection and pass each continuation's context to the next filter and handler. |
| Generated signatures | A shared type descriptor could lose a method's nullable return annotation. | Derive each return type from that method's Roslyn symbol, preserving nullable and nested type information. |

Class/method filter ordering was checked and covered by regression tests; the existing sorting step remains in use. Historical commented-out code was preserved.

## Validation

Added 24 cases in `TransportDeepReviewTest`, plus stronger relay-point assertions in existing protocol tests. They exercise actual UDP health checks, socket restart and concurrent starts, header-sized ping dispatch, response lease counts, zero-duration waits, invalid/failed relay assignments, key isolation, broken relay chains, concurrent authentication, shutdown queue ownership, service-factory failures, missing cleanup paths, DI and context-replacing filters, and generated return-nullability metadata.

Full solution builds passed in Debug and Release with zero warnings and errors. All 268 tests passed in each configuration, with none skipped. Generated backend output was also inspected to verify DI resolution and context flow through three filters. Existing tests continue to cover multi-hop relay transfers, streams, acknowledgments, cancellation, and parallel send/dispose races.

The existing warmed microbenchmark completed on .NET 10.0.12. Measured ping creation, unmatched-response handling, 1 KiB encryption, duplicate ACK handling, repeated receiver disposal, and unchanged empty-circuit cleanup each allocated 0 bytes per operation. Creating and disposing a receiver without a callback allocated 176 bytes per operation. These measurements cover the named operations, not whole network requests or throughput under load; this run does not establish a before/after speedup.

These tests cover selected interleavings and local UDP behavior. They do not prove absence of all races, nor replace prolonged packet-loss, reordering, cross-platform socket, or production-load testing. Authentication failures now have an explicit distinction: invalid signatures return `InvalidData`, while a different identity on an authenticated connection returns `InvalidOperation`.
