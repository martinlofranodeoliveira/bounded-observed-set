# Contract — development 0.1.0

This document defines the supported subset and public behavior. There is no claim of general CRDT or JSON-standard canonicalization compliance.

## Construction and values

`ObservedSet(string localReplica, IReadOnlyList<string> members, SetLimits? limits = null)` copies membership. Membership has 1–16 distinct IDs. Each ID matches `[A-Za-z0-9_-]{1,32}`. Comparisons are ordinal, case-sensitive. The writer must be a member. Membership and writer never change. Invalid construction throws an argument exception with a fixed message; null membership throws `ArgumentNullException`.

`SetLimits` defaults to `HistoryDots=4096`, `DistinctElements=1024`. Each must be at least 1 and no greater than its default/hard ceiling. Distinct elements count every element appearing in any retained fact, including fully removed elements. Limits may differ across replicas; they are local admission policy and are absent from snapshots.

Elements are non-null, well-formed Unicode strings with UTF-8 encoding length 0–128 bytes. Empty strings, NUL and other controls are supported. Unpaired UTF-16 surrogates and malformed UTF-8 input are invalid. No normalization, locale collation or case folding occurs.

A `Dot` consists of an exact member ID and a signed 64-bit counter in 1–9,223,372,036,854,775,807. Replica counters range from 0 through that maximum. These are causal counters, never timestamps. Each fact maps a dot to one element and a Boolean removed flag. Counters must be at least every retained dot counter for their member; gaps and counters above retained dots are permitted.

## Public operations

All mutators return `OperationResult`, with `Code`, `IsSuccess` and nullable `Dot`. Only successful `Add` returns a dot. Failure returns no dot. Codes themselves are the fixed diagnostics; no raw parser or input messages are returned.

- `Add(string? element)`: validate element; check history cap; check distinct-element cap for a historically new element; check counter exhaustion; allocate the checked next local counter and a fresh live fact. Even an already-visible element consumes a new historical dot. Failure precedence is `InvalidElement`, `HistoryLimit`, `ElementLimit`, `CounterOverflow`.
- `RemoveObserved(string? element)`: invalid element returns `InvalidElement`. Otherwise mark every locally retained live dot for that exact element removed. Unknown elements succeed without changing bytes. No new counter or fact is created.
- `Contains(string? element)`: invalid elements return false. Otherwise true iff a live fact for the exact element exists.
- `GetVisibleElements()`: return a new ordinal-sorted, distinct array of live element strings.
- `Merge(ObservedSet other)`: null throws `ArgumentNullException`. Capture one consistent source state without holding the target lock. Under the target lock, require identical membership sets; construct union of facts, require equal elements for equal dots, OR removal flags, take maximum counters, then check target history and element caps. Commit only if all checks pass. Diagnostic precedence is `IncompatibleMembership`, `DotConflict`, `HistoryLimit`, `ElementLimit`. Repeated/self merge succeeds when the current state is valid and changes no replicated evidence.
- `ExportSnapshot()`: return a fresh byte array containing canonical schema 1 JSON. No files are written. Local identity and local policy do not affect the bytes.
- `ImportSnapshot(ReadOnlySpan<byte> utf8Json)`: parse and validate a complete replacement. Size above 4,194,304 bytes returns `SnapshotTooLarge`; malformed or unsupported schema data returns `InvalidSnapshot`. A valid parsed state's membership mismatch returns `IncompatibleMembership`, then local cap failures return `HistoryLimit` or `ElementLimit`. Successful import replaces, rather than joins, state. The constructor-selected writer remains unchanged.

Ordinary validation failures, overflow and rejected merges/imports leave the target's canonical snapshot exactly unchanged. This guarantee excludes process failure and allocation failure. The core performs no file, network, clock or host-environment access.

## Snapshot schema 1

Exact empty example for membership A, B, C:

```json
{"schemaVersion":1,"members":["A","B","C"],"counters":[{"replica":"A","counter":0},{"replica":"B","counter":0},{"replica":"C","counter":0}],"facts":[]}
```

Each fact has exactly `replica`, `counter`, `element`, `removed`, for example:

```json
{"replica":"A","counter":1,"element":"blue","removed":true}
```

Every object must have exactly its specified fields. Unknown, missing and duplicate **decoded** property names are rejected at every object level. Thus `counter` and `\u0063ounter` in the same object are duplicates. Names are case-sensitive. No comments, trailing commas, BOM, trailing documents or coercion are supported. Nesting is bounded to eight levels.

`schemaVersion` is the integer 1. All counters are JSON numbers spelled as digits only: no sign, fraction, exponent, quotes or Boolean. Normal JSON leading-zero rules apply. Values must fit signed Int64. Fact counters must be positive. Each counter entry names a known member, and there is exactly one entry per member. Each fact names a known member and a unique dot. Duplicate dots reject even when their fields agree. A counter below a retained dot rejects. `removed` must be a JSON Boolean. Members and strings must obey the construction/value rules.

Hard-limit excess inside the serialized schema—more than 4,096 facts, more than 1,024 historical distinct elements, or membership outside 1–16—is `InvalidSnapshot`, not a request to enlarge an instance. A well-formed state within those hard ceilings can still fail a receiver's smaller local caps with `HistoryLimit` or `ElementLimit`. Malformed snapshots return a fixed code; where multiple structural defects coexist, no finer error precedence is promised.

Canonical exports are compact UTF-8 with no BOM, indentation or trailing newline. Root property order is `schemaVersion`, `members`, `counters`, `facts`. Members and counter entries are ordered by replica ID using `StringComparer.Ordinal`. Counter-object order is `replica`, `counter`. Facts are ordered by replica ID ordinally, then numerically by counter. Fact-object order is `replica`, `counter`, `element`, `removed`. Strings use the .NET 10 System.Text.Json default Utf8JsonWriter encoder. Import accepts alternate field/array orders and valid escapes, but exports normalize them to this form. This is not RFC 8785 canonical JSON. Every accepted state exports below the input ceiling, including worst-case control-character escaping.

## Causality and accepted-join laws

1. A adds x as A:1. B receives A:1 and removes x. When A receives B, A:1 remains recorded as removed and x is absent.
2. A and B independently add x as A:1 and B:1. A removes its observed A:1. Joining B leaves x visible through B:1.
3. B removes observed A:1 while A adds a new A:2 for x. Their join retains removed A:1 and live A:2; A:2 is the sole live A-dot.

For consistent dot assignments, fixed membership and states whose necessary joins are all accepted within caps, union/OR/max gives idempotence, commutativity and associativity. Replicas that accept the same accumulated facts and counters export identical bytes. No convergence is promised when required merges are rejected, identity ownership is violated, or imports replace newer state with older state.

### Semântica causal em português

Cada adição recebe uma tag única formada por ID da réplica e contador causal. Toda adição, mesmo de um valor já visível, cria uma tag nova. Remover marca somente as tags ativas já observadas localmente. Uma adição concorrente desconhecida não é alvo da remoção e sobrevive à mesclagem.

Se B observou A:1 e a removeu, a união conserva A:1 como removida. Se B:1 foi criada independentemente, ela permanece ativa. Se A cria A:2 enquanto B remove A:1, somente A:2 permanece ativa entre essas duas tags. Nenhum relógio participa dessas decisões.

Fatos removidos e seus elementos continuam consumindo os limites históricos. Não há poda silenciosa. Ao exceder limites, a operação inteira é recusada sem alterar fatos, contadores ou bytes canônicos anteriores. As leis de convergência valem apenas para mesclagens aceitas, dentro dos limites e com atribuições consistentes de tags.

## Concurrency and trust

State access on one instance is serialized by an internal lock. A merge captures and releases the source lock before taking the target lock: no opposite-order nested locks. A source may advance after capture. Successful import linearizes at replacement after parsing and can replace operations that finished during parsing. This is explicit replacement semantics, not a compare-and-swap recovery API.

Require one authoritative writer per replica ID across instances and recoveries. The lock protects only one object. Snapshots contain no authentication, confidentiality, durable fencing or rollback evidence. Dot conflicts detect some misuse, not all cloned histories. Do not feed untrusted snapshots into a privileged application without separate authorization and transport decisions.
