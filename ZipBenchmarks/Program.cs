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

[SimpleJob(RuntimeMoniker.Net48, baseline: true)]
[SimpleJob(RuntimeMoniker.Net70)]
[SimpleJob(RuntimeMoniker.Net80)]
[MemoryDiagnoser]
public class ZipArchiveEntryBenchmarks
{
    [Params(1, 5, 50)]
    public int F { get; set; }

    private const int N = 30_000;

    private MemoryStream msRaw;
    private MemoryStream msZip;
    private MemoryStream msZipText;

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

        msZipText = new MemoryStream();
        using var zipText = new ZipArchive(msZipText, ZipArchiveMode.Create, true);
        for (var i = 1; i <= F; i++)
        {
            var entry = zip.CreateEntry(i.ToString());
            using var entryStream = entry.Open();
            using var sw = new StreamWriter(entryStream, Encoding.UTF8, 0x2000, true);
            for (var j = 1; j <= N; j++)
            {
                sw.WriteLine(j.ToString());
            }
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

    private static void InternalCopyTo(Stream source, Stream destination, byte[] array)
    {
        int count;
        while ((count = source.Read(array, 0, array.Length)) != 0)
        {
            destination.Write(array, 0, count);
        }
    }

    // BinaryReader performance on a simple MemoryStream
    //
    // net7 is 3x faster than net48
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
    // net48 is 11x (!!!) faster than net7
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

    // Demonstrates that the massive performance issue affects BinaryReader and not StreamReader
    [Benchmark]
    public int ZipText()
    {
        msZipText.Position = 0;

        var num = 0;

        using var zip = new ZipArchive(msZip, ZipArchiveMode.Read, true);
        foreach (var entry in zip.Entries)
        {
            using var entryStream = entry.Open();
            using var sr = new StreamReader(entryStream, Encoding.UTF8, false, 0x2000, true);
            while (sr.ReadLine() != null)
            {
                num++;
            }
        }

        return num;
    }

    // BinaryReader performance on a ZipArchiveEntry stream,
    // Wraps entry stream in a BufferedStream
    //
    // net7 is 2x faster than net48, no longer any massive slowdown
    // net7 also uses 5x more memory than just Zip
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

    // BinaryReader performance on a ZipArchiveEntry stream,
    // Copies the contents to a local MemoryStream
    // 
    // net7, net48: Faster than ZipBufferedStream
    // net7: Uses less memory than ZipBufferedStream
    // net48: Allocates twice as much memory as ZipBufferedStream
    [Benchmark]
    public int ZipInnerMemoryStream()
    {
        msZip.Position = 0;

        var num = 0;

        var msEntry = new MemoryStream();

        using var zip = new ZipArchive(msZip, ZipArchiveMode.Read, true);
        foreach (var entry in zip.Entries)
        {
            using var entryStream = entry.Open();

            msEntry.Position = 0;
            entryStream.CopyTo(msEntry);
            msEntry.Position = 0;

            num += PerformRead(msEntry);
        }

        return num;
    }

    // BinaryReader performance on a ZipArchiveEntry stream
    // Copies the contents to a local MemoryStream
    // Copy uses a local buffer
    //
    // About as fast as ZipInnerMemoryStream
    // Allocates half as much as ZipInnerMemoryStream
    [Benchmark]
    public int ZipInnerMemoryStreamAndBuffer()
    {
        msZip.Position = 0;

        var num = 0;

        var msEntry = new MemoryStream();
        var buffer = new byte[0x2000];

        using var zip = new ZipArchive(msZip, ZipArchiveMode.Read, true);
        foreach (var entry in zip.Entries)
        {
            using var entryStream = entry.Open();

            msEntry.Position = 0;
            InternalCopyTo(entryStream, msEntry, buffer);
            msEntry.Position = 0;

            num += PerformRead(msEntry);
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
