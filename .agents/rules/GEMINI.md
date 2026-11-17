# Lovga — High-Performance .NET 10 / C# 14 Engineering Rules

You are working on **Lovga**, a distributed, low-latency, log-based message broker and pub/sub streaming system written in **C# 14** targeting **.NET 10 (`net10.0`)**.

## Core Engineering Mandates

1. **Target Framework & Language**:
   - Always target `net10.0` and `LangVersion` `14.0` (or `latest`).
   - Enable `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>`.
   - Prefer `sealed` classes by default unless inheritance is explicitly designed.
   - Use C# 14 idioms: primary constructors, `field` keyword in properties, collection expressions (`[]`), `params ReadOnlySpan<T>`, and `System.Threading.Lock`.

2. **Zero-Allocation & High-Performance Storage/Networking**:
   - **No hot-path heap allocations**: Use `Span<byte>`, `ReadOnlySpan<byte>`, `Memory<byte>`, `ReadOnlyMemory<byte>`, and `ArrayPool<byte>.Shared` for binary framing, serialization, and file I/O.
   - **Use `BinaryPrimitives`** (`System.Buffers.Binary.BinaryPrimitives`) for reading/writing integers and headers in little-endian format rather than `BinaryReader`/`BinaryWriter` or `BitConverter` array allocations.
   - **Use `RandomAccess` (`SafeFileHandle`)** for offset-based segment reads and writes where possible, avoiding shared `FileStream.Seek` race conditions.
   - **Use `System.IO.Hashing.Crc32`** on `ReadOnlySpan<byte>` for fast, allocation-free hardware-accelerated CRC32 checksums.

3. **Async & Concurrency Safety**:
   - **Never block async threads**: Forbidden to call `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, or `Thread.Sleep()` inside async flows.
   - **Use `System.Threading.Channels`** (`Channel.CreateBounded<T>` / `Channel.CreateUnbounded<T>`) for producer-consumer queues and subscriber fan-out instead of `ConcurrentQueue<T>` + polling loops.
   - **Never hold a topic write lock across a long-lived subscriber stream**: Writers and readers must be decoupled so slow or active consumers never block producers.
   - **Propagate `CancellationToken`** to every async call (`MoveNext`, `WriteAsync`, `ReadAsync`, `FlushAsync`, `WaitForNextTickAsync`).

4. **Observability & Error Handling**:
   - Use `ILogger<T>` (and `[LoggerMessage]` source-generated logging on hot paths) instead of `Console.WriteLine`.
   - Never silently swallow exceptions with empty `catch (Exception)` blocks.
