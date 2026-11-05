# Design — development 0.1.0

## Evidence rather than time

An add creates `(replica ID, positive Int64 counter)`. Each dot permanently owns exactly one ordinal element string. Each fact also has a monotone removed bit. Visibility is existential over nonremoved facts. Removal sets that bit on all currently observed live dots for one element; it does not issue a wildcard against future or concurrent additions.

For compatible valid states, the candidate join is fact union, removal OR for equal dots, and componentwise maximum counters. Assigning two elements to the same dot is not resolved by a tie-breaker: it is evidence of an unsupported identity history and returns `DotConflict`. Identical dot/element assignments cannot reveal all identity cloning, so absence of a conflict does not establish writer safety.

The core preserves facts even after removal. Distinct-element accounting also includes those facts. This trades eventual exhaustion for inspectable causal evidence. No tombstone garbage collection is offered because closed membership alone does not establish that every replica and every retained snapshot has safely passed a causal frontier.

## State, identity and policy are separate

Snapshots contain replicated evidence and counters, not a local writer. Otherwise receivers with equivalent evidence but different identities would export different bytes. The constructor selects the authoritative writer explicitly. Membership is immutable and sorted. Lower local limits are admission policy, not replicated metadata.

A state import is replacement, not a join. It can forget evidence if used incorrectly. The caller must coordinate recovery, exclusive writer ownership and nonrollback durable state externally. This library neither authenticates snapshots nor fences writers.

## Atomic admission

Source merge state is copied under the source lock, which is released before acquiring the target lock. The target then copies its current state, constructs the complete candidate, checks dot conflicts and local caps, and commits one reference. At most 8,192 facts are held in a merge candidate. Invalid candidates cannot alter counters, facts or canonical output. Self-merge is safe; a concurrent call may advance the receiver after the capture, but union cannot undo that progress.

Import parsing happens before the target lock. A successful import linearizes at replacement and can replace work that completed while parsing; this is intentional replacement semantics. Failed imports never commit. Other public operations serialize their state access with the instance lock. These guarantees are per instance, not a multi-replica transaction or a crash durability guarantee. Process termination and out-of-memory failures are outside the result-code guarantee.

No visibility index is cached. With at most 4,096 facts, direct scans keep invariants small. Add and remove are O(H); merge is O(H + I), plus distinct-element accounting. Canonical export sorts O(H log H) and serializes under the lock. Parsing and serialization allocate bounded buffers. The core has no file, environment, clock or network access.

## JSON boundaries

System.Text.Json provides tokenization and writing, not permissive object binding. The codec explicitly checks required/unknown fields, decoded duplicate names, array cardinalities, lexical integer syntax, membership, dot uniqueness, counter lower bounds, UTF-8 and Unicode scalar validity. Parser messages never cross the public API.

Hard bounds are 16 members, 32 ASCII bytes per ID, 4,096 facts, 1,024 distinct element strings and 128 UTF-8 bytes per element. A single element can require 768 JSON bytes when 128 one-byte controls each become a six-byte escape. Including a 32-byte replica, a 19-digit counter, field names, punctuation and the removal bit, a fact is below 900 bytes. Fact arrays plus member/counter metadata remain below 3.7 MB; the 4 MiB input ceiling therefore accepts every exported valid state. A regression test exercises all 4,096 dots with maximal control escaping.

Canonical means this schema's property order, ordinal member/dot order, compact .NET 10 Utf8JsonWriter output, default encoder and no BOM/newline. It does not mean RFC 8785 or cross-runtime JSON canonicalization. Import accepts alternate property/array ordering and escape spellings, then exports the documented form. Snapshot equality is byte equality of exports, not arbitrary input encodings.

## Testing strategy

Named regressions check the three distinct causal scenarios: observed A:1 removal, concurrent B:1 survival, and unobserved A:2 survival during A:1 removal. Join tests check all six merge orders, self/repeat merge, commutativity and associativity for accepted consistent states. Cap tests compare exact before/after bytes, including counters. Parser tests include decoded escaped duplicate fields, malformed Unicode and counter overflow.

The independent oracle stores event receipts rather than a dot/removal dictionary. A removal event carries IDs of observed live add events; a replica receives whole event histories. The expected live adds are reconstructed by subtracting targeted event IDs. It enumerates 4,096 four-action experiments and checks visibility, fact removal evidence and causal counters against parsed production output. This is a bounded exhaustive domain, not a proof for arbitrary histories. Concurrency tests exercise serialized adds and opposite-direction merges with a timeout guard.

## References and scope

- Shapiro, Preguiça, Baquero and Zawirski, *A comprehensive study of Convergent and Commutative Replicated Data Types*: https://inria.hal.science/inria-00555588/document . The relevant principle is unique causal add tags and removal of observed tags only. An author-hosted copy is available at https://pages.lip6.fr/Marc.Shapiro/papers/Comprehensive-CRDTs-RR7506-2011-01.pdf . No paper pseudocode or external implementation is copied here.
- System.Text.Json overview: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/overview
- dotnet test and Microsoft.Testing.Platform: https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test

This is a bounded closed-membership experiment, not a complete collaboration protocol. Accepted joins have the described algebra; rejecting required joins at finite caps prevents a general unconditional convergence claim.
