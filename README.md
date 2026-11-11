# DataNutritionMVP — The Zero-Allocation Clinical CSV Profiling Engine

A high-throughput, zero-allocation clinical data auditing engine built in C# (.NET 10). Designed to ingest, validate, and profile multi-million-row clinical datasets in real time without triggering Garbage Collector (GC) pauses or heap memory spikes.

## Architectural Progression (1,000,000 Clinical Records)

| Architecture Tier | Parsing Strategy | Cold-Start Execution | Steady-State (`BenchmarkDotNet`) | Heap Allocated | GC Pressure (`Gen0`) |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Tier 1: Naive Baseline** | `File.ReadAllLines` + `string.Split(',')` | `561.0 ms` | `283.14 ms` (±3.09 ms) | `312,070.97 KB` (~312.1 MB) | Heavy (`Gen0`/`Gen1`/`Gen2`) |
| **Tier 2: UTF-16 Span Stream** | `StreamReader.ReadLine` + `ReadOnlySpan<char>` | `340.0 ms` | `81.27 ms` (±0.71 ms) | `62,404.65 KB` (~62.4 MB) | `7,571.43` per 1k ops |
| **Tier 3: UTF-8 Zero-Alloc** | `FileStream` + `ArrayPool<byte>` + `Utf8Parser` | **`257.0 ms`** | **`29.98 ms` (±0.53 ms)** | **`2.50 KB` (~0 MB)** | **`0` (Zero GC Sweeps)** |

---

## Verified Hardware Telemetry (`BenchmarkDotNet`)

~~~text
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9818)
AMD Ryzen 5 5625U with Radeon Graphics 2.30GHz, 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.403
  [Host]     : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
~~~

| Method | Mean | Error | StdDev | Ratio | Gen0 | Gen1 | Gen2 | Allocated | Alloc Ratio |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `Tier1_NaiveReadAllLinesSplit` | 283.14 ms | 3.695 ms | 3.086 ms | 1.00 | 22000.0000 | 2000.0000 | 2000.0000 | 312070.97 KB | 1.000 |
| `Tier2_StreamReaderSpan` | 81.27 ms | 0.912 ms | 0.712 ms | 0.29 | 7571.4286 | - | - | 62404.65 KB | 0.200 |
| **`Tier3_Utf8ZeroAllocBytes`** | **29.98 ms** | **0.563 ms** | **0.526 ms** | **0.11** | **-** | **-** | **-** | **2.50 KB** | **0.000** |

### Key Systems Engineering Takeaways
* **9.4x Faster Throughput (~33.3M rows/sec):** Bypassing UTF-16 string transcoding (`StreamReader`) and parsing IEEE 754 floating-point values directly from raw UTF-8 byte slices via `System.Buffers.Text.Utf8Parser` reduced steady-state execution time by **89.4%** compared to `File.ReadAllLines` (`283.14 ms` to `29.98 ms`) and **63.1%** compared to `ReadOnlySpan<char>` (`81.27 ms` to `29.98 ms`).
* **99.999% Heap Allocation Reduction:** Renting a single reusable 64 KB buffer from `ArrayPool<byte>.Shared` and accumulating column telemetry inside stack-allocated value types (`struct ColumnProfile`) reduced heap allocation by **124,828x** (`312.1 MB` to `2.5 KB`).
* **Zero GC Collections:** Eliminating heap allocations completely suppressed Garbage Collector sweeps across `Gen0`, `Gen1`, and `Gen2`, ensuring deterministic latency for real-time clinical ingestion pipelines.

---

## Automated Deliverables (`ExportEngine`)

On every audit run, the engine serializes stack-allocated column profiles into two zero-overhead artifacts:
1. **`nutrition_label.json`:** Machine-readable JSON telemetry generated via `Utf8JsonWriter` for downstream API and pipeline integration.
2. **`nutrition_report.md`:** Executive Markdown summary with automated physiological boundary gates (`PASS` / `WARN` / `FAIL`).

### Sample Output (`nutrition_report.md`)
* **Overall Dataset Health Score:** **99.05%** (`WARN (REQUIRES CLEANING)`)

| Clinical Field | Gate | Validity % | Valid Rows | Missing / Corrupt | Outliers | Mean | Observed Range | Allowed Bounds |
| :--- | :---: | ---: | ---: | ---: | ---: | ---: | :---: | :---: |
| **Age (Years)** | `WARN` | `98.80%` | `988,000` | `10,000` | `2,000` | `53.5` | `18 - 89` | `[0, 120]` |
| **Heart Rate (BPM)** | `WARN` | `98.80%` | `988,000` | `10,000` | `2,000` | `82.0` | `55 - 109` | `[30, 220]` |
| **Systolic BP (mmHg)** | `WARN` | `98.80%` | `988,000` | `10,000` | `2,000` | `127.0` | `95 - 159` | `[70, 250]` |
| **Diastolic BP (mmHg)** | `PASS` | `99.80%` | `998,000` | `0` | `2,000` | `79.5` | `60 - 99` | `[40, 150]` |

---

## CLI Usage

~~~powershell
# Run the full 4-column UTF-8 audit and export JSON + Markdown deliverables
dotnet run -c Release --project DataNutritionMVP

# Run the 3-tier BenchmarkDotNet hardware verification suite
dotnet run -c Release --project DataNutritionMVP -- --benchmark
~~~
