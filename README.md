\# DataNutritionMVP — The Zero-Allocation Clinical CSV Profiling Engine



A high-throughput, zero-allocation clinical data auditing engine built in C# (.NET 10). Designed to ingest, validate, and profile multi-million-row clinical datasets in real time without triggering Garbage Collector (GC) pauses or heap memory spikes.



\## Architectural Progression (1,000,000 Clinical Records)



| Architecture Tier | Parsing Strategy | Cold-Start Execution | Steady-State (`BenchmarkDotNet`) | Heap Allocated | GC Pressure (`Gen0`) |

| :--- | :--- | :--- | :--- | :--- | :--- |

| \*\*Tier 1: Naive Baseline\*\* | `File.ReadAllLines` + `string.Split(',')` | `561.0 ms` | `283.14 ms` (±3.09 ms) | `312,070.97 KB` (\~312.1 MB) | Heavy (`Gen0`/`Gen1`/`Gen2`) |

| \*\*Tier 2: UTF-16 Span Stream\*\* | `StreamReader.ReadLine` + `ReadOnlySpan<char>` | `340.0 ms` | `81.27 ms` (±0.71 ms) | `62,404.65 KB` (\~62.4 MB) | `7,571.43` per 1k ops |

| \*\*Tier 3: UTF-8 Zero-Alloc\*\* | `FileStream` + `ArrayPool<byte>` + `Utf8Parser` | \*\*`257.0 ms`\*\* | \*\*`29.98 ms` (±0.53 ms)\*\* | \*\*`2.50 KB` (\~0 MB)\*\* | \*\*`0` (Zero GC Sweeps)\*\* |



\---



\## Verified Hardware Telemetry (`BenchmarkDotNet`)



```text

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)

AMD Ryzen 5 5625U with Radeon Graphics 2.30GHz, 1 CPU, 12 logical and 6 physical cores

.NET SDK 10.0.403

&#x20; \[Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

&#x20; DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

