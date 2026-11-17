---
name: dotnet-performance
description: >-
  Guides high-performance, zero-allocation C# 14 and .NET 10 programming using Span<T>,
  ReadOnlySpan<T>, Memory<T>, ArrayPool<T>, RecyclableMemoryStream, BinaryPrimitives,
  ref structs, Unsafe/MemoryMarshal, and BenchmarkDotNet. Use when optimizing hot paths,
  binary serialization, file I/O, or reducing GC pressure and LOH allocations in .NET.
---

# .NET 10 & C# 14 Performance & Zero-Allocation Engineering

This skill covers techniques for writing high-throughput, low-latency, allocation-free C# 14 / .NET 10 code.

## 1. Zero-Allocation Memory Slices (`Span<T>` & `ReadOnlySpan<T>`)
- **Prefer `ReadOnlySpan<byte>` / `Span<byte>` over `byte[]` or `Stream` allocations** on hot paths.
- In C# 14, leverage first-class `Span<T>` and `ReadOnlySpan<T>` implicit conversions and `params ReadOnlySpan<T>` instead of `params T[]` to eliminate array allocations at call sites:
  ```csharp
  public void LogMetrics(params ReadOnlySpan<long> values)
  {
      foreach (var v in values) { /* zero heap allocation */ }
  }
  ```
- **Stack Allocation (`stackalloc`)**: For small fixed-size buffers (< 1 KB), allocate on the stack:
  ```csharp
  Span<byte> header = stackalloc byte[16];
  BinaryPrimitives.WriteInt32LittleEndian(header[0..4], payloadLength);
  BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], crc32);
  ```

## 2. Pooling Buffers (`ArrayPool<T>` & `MemoryPool<T>`)
- Never allocate `new byte[size]` in loops or per-message handlers, especially for buffers >= 85,000 bytes (which go directly to the Large Object Heap / LOH).
- Rent and return buffers deterministically:
  ```csharp
  byte[] rented = ArrayPool<byte>.Shared.Rent(requiredLength);
  try
  {
      Span<byte> buffer = rented.AsSpan(0, requiredLength);
      // Use buffer...
  }
  finally
  {
      ArrayPool<byte>.Shared.Return(rented);
  }
  ```

## 3. Binary Serialization & Random-Access I/O
- **Avoid `BinaryReader` / `BinaryWriter` / `MemoryStream.ToArray()`** in hot loops. Use `System.Buffers.Binary.BinaryPrimitives` directly on `Span<byte>`.
- **Use `RandomAccess` over `FileStream`** for concurrent or offset-based file reads/writes without seeking locks:
  ```csharp
  using SafeFileHandle handle = File.OpenHandle(
      path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, FileOptions.Asynchronous);

  await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, length), fileOffset, cancellationToken);
  int bytesRead = await RandomAccess.ReadAsync(handle, targetMemory, fileOffset, cancellationToken);
  ```

## 4. Struct Layout & Value Types
- Use `readonly record struct` for small, immutable value types (< 16-32 bytes) to avoid heap boxing and defensive copies.
- Annotate hot small methods with `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.
- Use `CollectionsMarshal.AsSpan(list)` or `CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists)` to avoid double dictionary lookups and enumerator allocations.

## 5. Benchmarking with BenchmarkDotNet
Always validate optimizations with `[MemoryDiagnoser]`:
```csharp
[MemoryDiagnoser]
public class SerializationBenchmarks
{
    private readonly byte[] _data = new byte[1024];

    [Benchmark(Baseline = true)]
    public int ChecksumLegacy() => /* ... */;

    [Benchmark]
    public int ChecksumSpan() => /* ... */;
}
```
