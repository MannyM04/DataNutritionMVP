using System;
using System.IO;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;

// ZERO-ALLOCATION VALUE TYPE: Lives entirely on the CPU stack
struct ColumnProfile
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

    // Parses directly from raw UTF-8 bytes (Zero string/UTF-16 conversion)
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

class Program
{
    static void Main()
    {
        string filePath = "dirty_clinical_data.csv";
        int totalRows = 1_000_000;

        // 1. Generate 1M Rows of Sabotaged Data (if not already on disk)
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Generating {totalRows:N0} rows of sabotaged clinical data...");
            using (StreamWriter writer = new StreamWriter(filePath))
            {
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
            }
            Console.WriteLine("Sabotaged dataset ready.\n");
        }

        // 2. Initialize Stack-Allocated Column Profilers
        var ageProfile = new ColumnProfile("Age (Years)", 0, 120);
        var hrProfile = new ColumnProfile("Heart Rate (BPM)", 30, 220);
        var sysProfile = new ColumnProfile("Systolic BP (mmHg)", 70, 250);
        var diaProfile = new ColumnProfile("Diastolic BP (mmHg)", 40, 150);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBefore = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch sw = Stopwatch.StartNew();

        // 3. TRUE ZERO-ALLOCATION UTF-8 BYTE STREAMING
        // Rent a single 64 KB byte buffer from the shared pool instead of allocating strings
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);

        try
        {
            using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);

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

                    // Trim trailing '\r' on Windows CRLF files
                    if (line.Length > 0 && line[^1] == (byte)'\r')
                    {
                        line = line[..^1];
                    }

                    if (isHeader)
                    {
                        isHeader = false;
                    }
                    else if (!line.IsEmpty)
                    {
                        ProcessRow(line, ref ageProfile, ref hrProfile, ref sysProfile, ref diaProfile);
                    }

                    // Advance past the current line
                    chunk = chunk[(lineEnd + 1)..];
                }

                // Copy any unfinished partial row at the end of the 64KB chunk back to the front of the buffer
                chunk.CopyTo(buffer);
                bytesBuffered = chunk.Length;
            }
        }
        finally
        {
            // Always return the rented buffer to the pool
            ArrayPool<byte>.Shared.Return(buffer);
        }

        sw.Stop();
        long memAfter = GC.GetTotalAllocatedBytes(precise: true);
        long allocatedBytes = memAfter - memBefore;
        double allocatedKB = allocatedBytes / 1024.0;
        long allocatedMB = allocatedBytes / (1024 * 1024);

        Console.WriteLine("==================================================");
        Console.WriteLine($"UTF-8 ZERO-ALLOCATION AUDIT COMPLETE");
        Console.WriteLine($"Execution Time : {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Heap Allocated : {allocatedMB} MB ({allocatedKB:F2} KB / {allocatedBytes:N0} bytes)");
        Console.WriteLine("==================================================\n");

        ageProfile.PrintSummary();
        hrProfile.PrintSummary();
        sysProfile.PrintSummary();
        diaProfile.PrintSummary();

        Console.ReadLine();
    }

    static void ProcessRow(
        ReadOnlySpan<byte> line,
        ref ColumnProfile age,
        ref ColumnProfile hr,
        ref ColumnProfile sys,
        ref ColumnProfile dia)
    {
        // Slice past PatientID using raw ASCII comma byte
        int c1 = line.IndexOf((byte)',');
        ReadOnlySpan<byte> rest = line[(c1 + 1)..];

        // Slice Age
        int c2 = rest.IndexOf((byte)',');
        age.ProcessUtf8Slice(rest[..c2]);
        rest = rest[(c2 + 1)..];

        // Slice HeartRate
        int c3 = rest.IndexOf((byte)',');
        hr.ProcessUtf8Slice(rest[..c3]);
        rest = rest[(c3 + 1)..];

        // Slice SystolicBP & DiastolicBP
        int c4 = rest.IndexOf((byte)',');
        sys.ProcessUtf8Slice(rest[..c4]);
        dia.ProcessUtf8Slice(rest[(c4 + 1)..]);
    }
}