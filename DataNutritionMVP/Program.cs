using System;
using System.IO;
using System.Diagnostics;

// ZERO-ALLOCATION VALUE TYPE: Lives entirely on the stack
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

    // Takes a ReadOnlySpan<char> slice directly from the line without string allocation
    public void ProcessSlice(ReadOnlySpan<char> slice)
    {
        if (slice.IsEmpty || !double.TryParse(slice, out double val))
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

        // 1. Generate 1M Rows with Injected Corruption & Physiological Outliers
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
                    {
                        // Inject missing cells and corrupted text strings every 100th row
                        writer.WriteLine($"{i},N/A,,ERR,80");
                    }
                    else if (i % 250 == 0)
                    {
                        // Inject impossible physiological outliers every 250th row
                        writer.WriteLine($"{i},999,420,-15,350");
                    }
                    else
                    {
                        writer.WriteLine($"{i},{rand.Next(18, 90)},{rand.Next(55, 110)},{rand.Next(95, 160)},{rand.Next(60, 100)}");
                    }
                }
            }
            Console.WriteLine("Sabotaged dataset ready.\n");
        }

        // 2. Initialize Stack-Allocated Column Profilers with Physiological Boundaries
        var ageProfile = new ColumnProfile("Age (Years)", 0, 120);
        var hrProfile = new ColumnProfile("Heart Rate (BPM)", 30, 220);
        var sysProfile = new ColumnProfile("Systolic BP (mmHg)", 70, 250);
        var diaProfile = new ColumnProfile("Diastolic BP (mmHg)", 40, 150);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBefore = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch sw = Stopwatch.StartNew();

        // 3. Single-Pass Zero-Allocation Span<T> Stream
        using (StreamReader reader = new StreamReader(filePath))
        {
            string? line;
            bool isHeader = true;

            while ((line = reader.ReadLine()) != null)
            {
                if (isHeader) { isHeader = false; continue; }

                ReadOnlySpan<char> span = line.AsSpan();

                // Slice past PatientID
                int c1 = span.IndexOf(',');
                ReadOnlySpan<char> rest = span[(c1 + 1)..];

                // Slice Age
                int c2 = rest.IndexOf(',');
                ageProfile.ProcessSlice(rest[..c2]);
                rest = rest[(c2 + 1)..];

                // Slice HeartRate
                int c3 = rest.IndexOf(',');
                hrProfile.ProcessSlice(rest[..c3]);
                rest = rest[(c3 + 1)..];

                // Slice SystolicBP & DiastolicBP
                int c4 = rest.IndexOf(',');
                sysProfile.ProcessSlice(rest[..c4]);
                diaProfile.ProcessSlice(rest[(c4 + 1)..]);
            }
        }

        sw.Stop();
        long memAfter = GC.GetTotalAllocatedBytes(precise: true);
        long allocatedMB = (memAfter - memBefore) / (1024 * 1024);

        Console.WriteLine("==================================================");
        Console.WriteLine($"FULL DATASET NUTRITION AUDIT COMPLETE");
        Console.WriteLine($"Execution Time : {sw.ElapsedMilliseconds} ms");
        Console.WriteLine($"Heap Allocated : {allocatedMB} MB");
        Console.WriteLine("==================================================\n");

        ageProfile.PrintSummary();
        hrProfile.PrintSummary();
        sysProfile.PrintSummary();
        diaProfile.PrintSummary();

        Console.ReadLine();
    }
}