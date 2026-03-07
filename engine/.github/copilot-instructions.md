# Copilot Instructions

## Project Guidelines
- For Sims4 mounting tests, avoid synthetic/fake package generation and prefer tests that load real package fixtures.

## Performance Optimization
- Prefer high-performance parsing with ImHex-matching data structures.
- Use struct/offset decoding with `MemoryMarshal.Read` while minimizing allocations.