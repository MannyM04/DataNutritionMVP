using System;
using System.IO;
using System.Diagnostics;

class Program
{
    static void Main()
    {
        string filePath = "large_clinical_data.csv";
        int totalRowsToGenerate = 1_000_000;

        // 1. Generating 1 Million Rows of Mock Data 
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"Generating {totalRowsToGenerate:N0} rows of clinical data...");
            using (StreamWriter writer = new StreamWriter(filePath))
            {
                writer.WriteLine("PatientID,Age,HeartRate,SystolicBP,DiastolicBP");
                Random rand = new Random(42);
                for (int i = 1; i <= totalRowsToGenerate; i++)
                {
                    writer.WriteLine($"{i},{rand.Next(18, 90)},{rand.Next(55, 110)},{rand.Next(95, 160)},{rand.Next(60, 100)}");
                }
            }
            Console.WriteLine("Generation complete.\n");
        }

        // --- TEST 1: THE STANDARD (HEAP-HEAVY) WAY ---
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBeforeStandard = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch sw1 = Stopwatch.StartNew();

        string[] lines = File.ReadAllLines(filePath);
        double totalAge1 = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] cols = lines[i].Split(',');
            totalAge1 += double.Parse(cols[1]);
        }

        sw1.Stop();
        long memAfterStandard = GC.GetTotalAllocatedBytes(precise: true);
        long standardAllocatedMB = (memAfterStandard - memBeforeStandard) / (1024 * 1024);

        Console.WriteLine($"[STANDARD] Time: {sw1.ElapsedMilliseconds} ms | Heap Allocated: {standardAllocatedMB} MB");


        // --- TEST 2: THE SPAN<T> STREAMING WAY + DATA NUTRITION PROFILER ---
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long memBeforeSpan = GC.GetTotalAllocatedBytes(precise: true);
        Stopwatch sw2 = Stopwatch.StartNew();

        double totalAge2 = 0;
        long validRows = 0;
        long missingAgeCount = 0;
        long outlierAgeCount = 0;
        double minAge = double.MaxValue;
        double maxAge = double.MinValue;

        using (StreamReader reader = new StreamReader(filePath))
        {
            string? line;
            bool isHeader = true;
            while ((line = reader.ReadLine()) != null)
            {
                if (isHeader) { isHeader = false; continue; }

                ReadOnlySpan<char> span = line.AsSpan();

                int comma1 = span.IndexOf(',');
                ReadOnlySpan<char> rest = span[(comma1 + 1)..];

                int comma2 = rest.IndexOf(',');
                ReadOnlySpan<char> ageSpan = rest[..comma2];

                // DATA NUTRITION CHECK: Safely parse without crashing on bad data
                if (double.TryParse(ageSpan, out double age))
                {
                    // Check for physiological outliers (e.g. impossible human age)
                    if (age < 0 || age > 120)
                    {
                        outlierAgeCount++;
                    }
                    else
                    {
                        totalAge2 += age;
                        validRows++;
                        if (age < minAge) minAge = age;
                        if (age > maxAge) maxAge = age;
                    }
                }
                else
                {
                    // Count missing or corrupted fields
                    missingAgeCount++;
                }
            }
        }

        sw2.Stop();
        long memAfterSpan = GC.GetTotalAllocatedBytes(precise: true);
        long spanAllocatedMB = (memAfterSpan - memBeforeSpan) / (1024 * 1024);

        Console.WriteLine($"[SPAN<T>]  Time: {sw2.ElapsedMilliseconds} ms | Heap Allocated: {spanAllocatedMB} MB\n");

        // --- DATA NUTRITION REPORT OUTPUT ---
        Console.WriteLine("=== DATA NUTRITION REPORT (AGE FIELD) ===");
        Console.WriteLine($"Valid Rows Processed: {validRows:N0}");
        Console.WriteLine($"Average Age: {totalAge2 / validRows:F1}");
        Console.WriteLine($"Min Age: {minAge} | Max Age: {maxAge}");
        Console.WriteLine($"Missing/Corrupt Values Flagged: {missingAgeCount}");
        Console.WriteLine($"Physiological Outliers Flagged: {outlierAgeCount}");

        Console.ReadLine();
    }
}