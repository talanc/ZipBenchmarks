using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace ZipBenchmarks;

// Findings:
// - net8 has a massive performance issue with reading zip archives streams directly with BinaryReader
// - this can be fixed wrapping stream in a BufferedStream first
// - net9 mostly fixes this issue, still not as fast as net48
// - net9 has slightly less perf than net8 in Raw/BS tests
// - still you should probably use BufferedStream as it uses less allocations overall

[SimpleJob(RuntimeMoniker.Net48)]
[SimpleJob(RuntimeMoniker.Net80, baseline: true)]
[SimpleJob(RuntimeMoniker.Net90)]
[MemoryDiagnoser]
public class ZipArchiveEntryBenchmarks
{
    public const int F = 10;
    private const int N = 30_000;

    private MemoryStream msRaw;
    private MemoryStream msZip;

    [GlobalSetup]
    public void GlobalSetup()
    {
        msRaw = new MemoryStream();
        PerformWrite(msRaw);

        msZip = new MemoryStream();
        using var zip = new ZipArchive(msZip, ZipArchiveMode.Create, true);
        for (var i = 1; i <= F; i++)
        {
            var entry = zip.CreateEntry(i.ToString());
            using var entryStream = entry.Open();
            PerformWrite(entryStream);
        }
    }

    private static int PerformRead(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.UTF8, true);
        var num = 0;
        for (var i = 1; i <= N; i++)
        {
            br.ReadSingle();
            num++;
        }
        return num;
    }

    private static void PerformWrite(Stream stream)
    {
        using var bw = new BinaryWriter(stream, Encoding.UTF8, true);
        for (var i = 1; i <= N; i++)
        {
            bw.Write((float)i);
        }
    }

    // BinaryReader performance on a simple MemoryStream
    //
    // net8: 7x faster than net48
    // net8: 1.45x faster than net9
    // net9: 3x less alloc than net8
    [Benchmark]
    public int Raw()
    {
        var num = 0;

        for (var i = 0; i < F; i++)
        {
            msRaw.Position = 0;
            num += PerformRead(msRaw);
        }

        return num;
    }

    // BinaryReader performance on a ZipArchiveEntry stream
    //
    // net9: 10x faster than net8
    // net48: 1.5x faster than net9
    // net9: 88x less alloc than net48
    [Benchmark]
    public int Zip()
    {
        msZip.Position = 0;

        var num = 0;

        using var zip = new ZipArchive(msZip, ZipArchiveMode.Read, true);
        foreach (var entry in zip.Entries)
        {
            using var entryStream = entry.Open();
            num += PerformRead(entryStream);
        }

        return num;
    }

    // BinaryReader performance on a ZipArchiveEntry stream,
    // Wraps entry stream in a BufferedStream
    //
    // net8 no longer has massive perf issue from Zip()
    // net8: 1.4x faster than net9
    [Benchmark]
    public int ZipBufferedStream()
    {
        msZip.Position = 0;

        var num = 0;

        using var zip = new ZipArchive(msZip, ZipArchiveMode.Read, true);
        foreach (var entry in zip.Entries)
        {
            using var entryStream = entry.Open();
            using var bs = new BufferedStream(entryStream);
            num += PerformRead(bs);
        }

        return num;
    }
}

public class Program
{
    static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run(typeof(Program).Assembly);
    }
}
