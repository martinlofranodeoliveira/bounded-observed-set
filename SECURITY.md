# Security policy

## Reporting

Report suspected vulnerabilities through GitHub private vulnerability reporting:

https://github.com/martinlofranodeoliveira/bounded-observed-set/security/advisories/new

Do not disclose exploitable details in a public issue. Include the development version, a minimal synthetic reproduction, expected behavior, actual behavior and relevant resource-bound observations. Do not include secrets or real personal data. No response-time SLA is promised. General contact is available through https://lofranotecnologia.com.br.

## Scope and limits

The documented line is development version 0.1.0. There is no production-readiness or security-certification claim.

The core treats snapshots as untrusted structured data and enforces size, schema, Unicode, counter and cardinality bounds. It returns fixed diagnostic codes rather than input or parser text. It performs no file or network operations and includes no telemetry.

This is not an authorization or synchronization protocol. Snapshots have no authenticity, integrity signature, encryption or provenance. An authorized-looking member ID is not an authenticated actor. A malicious but structurally valid snapshot can mark known facts removed or exhaust counters/history. Keep authorization and transport security outside this library.

One authoritative writer per replica ID, durable recovery coordination and rollback prevention are caller obligations. An internal instance lock does not fence cloned identities. Dot conflicts detect only some inconsistent histories. Imports deliberately replace state and can regress evidence if misused. Resource bounds limit accepted state size, not all process-wide allocations, concurrent instance counts or caller behavior. Process termination and allocation failure are outside atomic result-code guarantees.
