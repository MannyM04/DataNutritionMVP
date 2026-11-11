using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;

// 1. ZERO-ALLOCATION VALUE TYPE: Marked public so all classes can access it
public struct ColumnProfile
{
    public string Name;
    public double MinAllowed;
    public double MaxAllowed;

    public long ValidCount;
    public long MissingOrCorruptCount;
    public long OutlierCount;
    public double Sum;
    public double Min;
    public double Max;

    public ColumnProfile(string name, double minAllowed, double maxAllowed)
    {
        Name = name;
        MinAllowed = minAllowed;
        MaxAllowed = maxAllowed;
        ValidCount = 0;
        MissingOrCorruptCount = 0;
        OutlierCount = 0;
        Sum = 0;
        Min = double.MaxValue;
        Max = double.MinValue;
    }

    public void ProcessUtf8Slice(ReadOnlySpan<byte> slice)
    {
        if (slice.IsEmpty || !Utf8Parser.TryParse(slice, out double val, out int bytesConsumed) || bytesConsumed != slice.Length)
        {
            MissingOrCorruptCount++;
            return;
        }

        if (val < MinAllowed || val > MaxAllowed)
        {
            OutlierCount++;
            return;
        }

        ValidCount++;
        Sum += val;
        if (val < Min) Min = val;
        if (val > Max) Max = val;
    }

    public void PrintSummary()
    {
        double mean = ValidCount > 0 ? Sum / ValidCount : 0;
        Console.WriteLine($"[{Name.ToUpper()}]");
        Console.WriteLine($"  Valid Records      : {ValidCount:N0}");
        Console.WriteLine($"  Mean & Range       : {mean:F1} (Min: {Min} | Max: {Max})");
        Console.WriteLine($"  Missing / Corrupt  : {MissingOrCorruptCount:N0}");
        Console.WriteLine($"  Clinical Outliers  : {OutlierCount:N0}\n");
    }
}

// 2. OPTION C: EXPORT ENGINE (JSON + MARKDOWN DELIVERABLES)
public static class ExportEngine
{
    public static void GenerateDeliverables(
        string sourceFileName,
        long totalRows,
        long executionMs,
        long allocatedBytes,
        ColumnProfile[] profiles,
        string jsonPath = "nutrition_label.json",
        string markdownPath = "nutrition_report.md")
    {
        WriteJsonLabel(sourceFileName, totalRows, executionMs, allocatedBytes, profiles, jsonPath);
        WriteMarkdownReport(sourceFileName, totalRows, executionMs, allocatedBytes, profiles, markdownPath);
    }

    private static void WriteJsonLabel(
        string sourceFileName,
        long totalRows,
        long executionMs,
        long allocatedBytes,
        ColumnProfile[] profiles,
        string jsonPath)
    {
        using FileStream fs = File.Create(jsonPath);
        var options = new JsonWriterOptions { Indented = true };
        using Utf8JsonWriter writer = new Utf8JsonWriter(fs, options);

        long totalCells = totalRows * profiles.Length;
        long totalValidCells = 0;
        for (int i = 0; i < profiles.Length; i++)
        {
            totalValidCells += profiles[i].ValidCount;
        }
        double overallHealthPct = totalCells > 0 ? (totalValidCells / (double)totalCells) * 100.0 : 0.0;

        writer.WriteStartObject();
        writer.WriteString("schemaVersion", "1.0.0");
        writer.WriteString("generatedAtUtc", DateTime.UtcNow.ToString("O"));
        writer.WriteString("datasetSource", sourceFileName);

        writer.WriteStartObject("engineTelemetry");
        writer.WriteNumber("totalRowsProcessed", totalRows);
        writer.WriteNumber("executionTimeMs", executionMs);
        writer.WriteNumber("heapAllocatedBytes", allocatedBytes);
        writer.WriteNumber("heapAllocatedKB", Math.Round(allocatedBytes / 1024.0, 2));
        writer.WriteNumber("overallHealthScorePct", Math.Round(overallHealthPct, 2));
        writer.WriteEndObject();

        writer.WriteStartArray("columnProfiles");
        for (int i = 0; i < profiles.Length; i++)
        {
            ColumnProfile col = profiles[i];
            long colTotal = col.ValidCount + col.MissingOrCorruptCount + col.OutlierCount;
            double mean = col.ValidCount > 0 ? col.Sum / col.ValidCount : 0;
            double validityPct = colTotal > 0 ? (col.ValidCount / (double)colTotal) * 100.0 : 0;
            string status = validityPct >= 99.5 ? "PASS" : (validityPct >= 98.0 ? "WARN" : "FAIL");

            writer.WriteStartObject();
            writer.WriteString("columnName", col.Name);
            writer.WriteString("qualityGateStatus", status);
            writer.WriteNumber("validityScorePct", Math.Round(validityPct, 2));
            writer.WriteNumber("validCount", col.ValidCount);
            writer.WriteNumber("missingOrCorruptCount", col.MissingOrCorruptCount);
            writer.WriteNumber("physiologicalOutlierCount", col.OutlierCount);

            writer.WriteStartObject("bounds");
            writer.WriteNumber("minAllowed", col.MinAllowed);
            writer.WriteNumber("maxAllowed", col.MaxAllowed);
            writer.WriteEndObject();

            writer.WriteStartObject("statistics");
            writer.WriteNumber("mean", Math.Round(mean, 2));
            writer.WriteNumber("minObserved", col.ValidCount > 0 ? col.Min : 0);
            writer.WriteNumber("maxObserved", col.ValidCount > 0 ? col.Max : 0);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteMarkdownReport(
        string sourceFileName,
        long totalRows,
        long executionMs,
        long allocatedBytes,
        ColumnProfile[] profiles,
        string markdownPath)
    {
        long totalCells = totalRows * profiles.Length;
        long totalValidCells = 0;
        long totalMissingCells = 0;
        long totalOutlierCells = 0;

        for (int i = 0; i < profiles.Length; i++)
        {
            totalValidCells += profiles[i].ValidCount;
            totalMissingCells += profiles[i].MissingOrCorruptCount;
            totalOutlierCells += profiles[i].OutlierCount;
        }

        double overallHealthPct = totalCells > 0 ? (totalValidCells / (double)totalCells) * 100.0 : 0.0;
        string overallBadge = overallHealthPct >= 99.5 ? "PASS" : (overallHealthPct >= 98.0 ? "WARN (REQUIRES CLEANING)" : "FAIL (QUARANTINE)");

        var sb = new StringBuilder(2048);
        sb.AppendLine("# Clinical Data Nutrition Label & Audit Report");
        sb.AppendLine();
        sb.AppendLine($"* **Source Dataset:** `{sourceFileName}`");
        sb.AppendLine($"* **Audit Timestamp (UTC):** `{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC`");
        sb.AppendLine($"* **Overall Dataset Health Score:** **{overallHealthPct:F2}%** (`{overallBadge}`)");
        sb.AppendLine();
        sb.AppendLine("## 1. Engine Telemetry");
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("| :--- | :--- |");
        sb.AppendLine($"| **Total Records Audited** | `{totalRows:N0}` rows (`{totalCells:N0}` clinical data cells) |");
        sb.AppendLine($"| **Cold-Start Execution Time** | `{executionMs} ms` |");
        sb.AppendLine($"| **Heap Memory Footprint** | `{allocatedBytes / 1024.0:F2} KB` (`{allocatedBytes:N0} bytes`) |");
        sb.AppendLine($"| **Total Clean Cells** | `{totalValidCells:N0}` |");
        sb.AppendLine($"| **Total Missing / Corrupt Cells** | `{totalMissingCells:N0}` |");
        sb.AppendLine($"| **Total Physiological Outliers** | `{totalOutlierCells:N0}` |");
        sb.AppendLine();
        sb.AppendLine("## 2. Column-by-Column Nutrition Breakdown");
        sb.AppendLine("| Clinical Field | Gate | Validity % | Valid Rows | Missing / Corrupt | Outliers | Mean | Observed Range | Allowed Bounds |");
        sb.AppendLine("| :--- | :---: | ---: | ---: | ---: | ---: | ---: | :---: | :---: |");

        for (int i = 0; i < profiles.Length; i++)
        {
            ColumnProfile col = profiles[i];
            long colTotal = col.ValidCount + col.MissingOrCorruptCount + col.OutlierCount;
            double mean = col.ValidCount > 0 ? col.Sum / col.ValidCount : 0;
            double validityPct = colTotal > 0 ? (col.ValidCount / (double)colTotal) * 100.0 : 0;
            string status = validityPct >= 99.5 ? "PASS" : (validityPct >= 98.0 ? "WARN" : "FAIL");

            sb.AppendLine($"| **{col.Name}** | `{status}` | `{validityPct:F2}%` | `{col.ValidCount:N0}` | `{col.MissingOrCorruptCount:N0}` | `{col.OutlierCount:N0}` | `{mean:F1}` | `{col.Min} - {col.Max}` | `[{col.MinAllowed}, {col.MaxAllowed}]` |");
        }

        File.WriteAllText(markdownPath, sb.ToString());
    }
}

// 3. OPTION B: 3-TIER BENCHMARKDOTNET SUITE
[MemoryDiagnoser]
public class ClinicalParserBenchmarks
{
    private readonly string _filePath = "dirty_clinical_data.csv";
    private readonly int _totalRows = 1_000_000;

    [GlobalSetup]
    public void Setup() => Program.EnsureDatasetExists(_filePath, _totalRows);

    [Benchmark(Baseline = true)]
    public void Tier1_NaiveReadAllLinesSplit()
    {
        long validRows = 0;
        string[] lines = File.ReadAllLines(_filePath);
        for (int i = 1; i < lines.Length; i++)
        {
            string[] parts = lines[i].Split(',');
            if (parts.Length > 1 && double.TryParse(parts[1], out _)) validRows++;
        }
    }

    [Benchmark]
    public void Tier2_StreamReaderSpan()
    {
        long validRows = 0;
        using StreamReader reader = new StreamReader(_filePath);
        string? line;
        bool isHeader = true;
        while ((line = reader.ReadLine()) != null)
        {
            if (isHeader) { isHeader = false; continue; }
            ReadOnlySpan<char> span = line.AsSpan();
            int c1 = span.IndexOf(',');
            ReadOnlySpan<char> rest = span[(c1 + 1)..];
            int c2 = rest.IndexOf(',');
            if (double.TryParse(rest[..c2], out _)) validRows++;
        }
    }

    [Benchmark]
    public void Tier3_Utf8ZeroAllocBytes()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            using FileStream fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.SequentialScan);
            int bytesBuffered = 0;
            int bytesRead;
            bool isHeader = true;

            while ((bytesRead = fs.Read(buffer, bytesBuffered, buffer.Length - bytesBuffered)) > 0)
            {
                int totalBytes = bytesBuffered + bytesRead;
                ReadOnlySpan<byte> chunk = buffer.AsSpan(0, totalBytes);
                int lineEnd;
                while ((lineEnd = chunk.IndexOf((byte)'\n')) != -1)
                {
                    ReadOnlySpan<byte> line = chunk[..lineEnd];
                    if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
                    if (isHeader) isHeader = false;
                    else if (!line.IsEmpty)
                    {
                        int c1 = line.IndexOf((byte)',');
                        ReadOnlySpan<byte> rest = line[(c1 + 1)..];
                        int c2 = rest.IndexOf((byte)',');
                        Utf8Parser.TryParse(rest[..c2], out double _, out _);
                    }
                    chunk = chunk[(lineEnd + 1)..];
                }
                chunk.CopyTo(buffer);
                bytesBuffered = chunk.Length;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

// 4. MAIN ENTRY POINT & FULL 4-COLUMN AUDIT RUNNER
public class Program
{
    static void Main(string[] args)
    {
        // Pass "--benchmark" to run BenchmarkDotNet; otherwise run the full audit + export
        if (args.Length > 0 && args[0] == "--benchmark")
        {
            BenchmarkRunner.Run<ClinicalParserBenchmarks>();
            return;
        }

        RunFullAudit("dirty_clinical_data.csv", 1_000_000);
    }

    public static void EnsureDatasetExists(string filePath, int totalRows)
    {
        if (File.Exists(filePath)) return;

        Console.WriteLine($"Generating {totalRows:N0} rows of sabotaged clinical data...");
        using StreamWriter writer = new StreamWriter(filePath);
        writer.WriteLine("PatientID,Age,HeartRate,SystolicBP,DiastolicBP");
        Random rand = new Random(42);

        for (int i = 1; i <= totalRows; i++)
        {
            if (i % 100 == 0)
                writer.WriteLine($"{i},N/A,,ERR,80");
            else if (i % 250 == 0)
                writer.WriteLine($"{i},999,420,-15,350");
            else
                writer.WriteLine($"{i},{rand.Next(18, 90)},{rand.Next(55, 110)},{rand.Next(95, 160)},{rand.Next(60, 100)}");
        }
        Console.WriteLine("Sabotaged dataset ready.\n");
    }

    static void RunFullAudit(string filePath, int totalRows)
    {
        EnsureDatasetExists(filePath, totalRows);

        var ageProfile = new ColumnProfile("Age (Years)", 0, 120);
        var hrProfile = new ColumnProfile("Heart Rate (BPM)", 30, 220);
        var sysProfile = new ColumnProfile("Systolic BP (mmHg)", 70, 250);
        var diaProfile = new ColumnProfile("Diastolic BP (mmHg)", 40, 150);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBefore = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch sw = Stopwatch.StartNew();

        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);

        try
        {
            using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.SequentialScan);
            int bytesBuffered = 0;
            int bytesRead;
            bool isHeader = true;

            while ((bytesRead = fs.Read(buffer, bytesBuffered, buffer.Length - bytesBuffered)) > 0)
            {
                int totalBytes = bytesBuffered + bytesRead;
                ReadOnlySpan<byte> chunk = buffer.AsSpan(0, totalBytes);

                int lineEnd;
                while ((lineEnd = chunk.IndexOf((byte)'\n')) != -1)
                {
                    ReadOnlySpan<byte> line = chunk[..lineEnd];
                    if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];

                    if (isHeader)
                    {
                        isHeader = false;
                    }
                    else if (!line.IsEmpty)
                    {
                        ProcessRow(line, ref ageProfile, ref hrProfile, ref sysProfile, ref diaProfile);
                    }
                    chunk = chunk[(lineEnd + 1)..];
                }
                chunk.CopyTo(buffer);
                bytesBuffered = chunk.Length;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        sw.Stop();
        long memAfter = GC.GetTotalAllocatedBytes(precise: true);
        long allocatedBytes = memAfter - memBefore;
        double allocatedKB = allocatedBytes / 1024.0;
        long allocatedMB = allocatedBytes / (1024 * 1024);

        Console.WriteLine("==================================================");
        Console.WriteLine("UTF-8 ZERO-ALLOCATION AUDIT COMPLETE");
        Console.WriteLine($"Execution Time : {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Heap Allocated : {allocatedMB} MB ({allocatedKB:F2} KB / {allocatedBytes:N0} bytes)");
        Console.WriteLine("==================================================\n");

        ageProfile.PrintSummary();
        hrProfile.PrintSummary();
        sysProfile.PrintSummary();
        diaProfile.PrintSummary();

        ColumnProfile[] allProfiles = { ageProfile, hrProfile, sysProfile, diaProfile };
        ExportEngine.GenerateDeliverables(filePath, totalRows, sw.ElapsedMilliseconds, allocatedBytes, allProfiles);

        Console.WriteLine("==================================================");
        Console.WriteLine("DELIVERABLES EXPORTED:");
        Console.WriteLine($"  JSON Label      : {Path.GetFullPath("nutrition_label.json")}");
        Console.WriteLine($"  Markdown Report : {Path.GetFullPath("nutrition_report.md")}");
        Console.WriteLine("==================================================");
    }

    static void ProcessRow(
        ReadOnlySpan<byte> line,
        ref ColumnProfile age,
        ref ColumnProfile hr,
        ref ColumnProfile sys,
        ref ColumnProfile dia)
    {
        int c1 = line.IndexOf((byte)',');
        ReadOnlySpan<byte> rest = line[(c1 + 1)..];

        int c2 = rest.IndexOf((byte)',');
        age.ProcessUtf8Slice(rest[..c2]);
        rest = rest[(c2 + 1)..];

        int c3 = rest.IndexOf((byte)',');
        hr.ProcessUtf8Slice(rest[..c3]);
        rest = rest[(c3 + 1)..];

        int c4 = rest.IndexOf((byte)',');
        sys.ProcessUtf8Slice(rest[..c4]);
        dia.ProcessUtf8Slice(rest[(c4 + 1)..]);
    }
}
