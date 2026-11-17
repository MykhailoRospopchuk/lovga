---
name: dotnet-async-concurrency
description: >-
  Guides asynchronous and concurrent programming in .NET 10 using System.Threading.Channels,
  ValueTask, BackgroundService, PeriodicTimer, CancellationToken, Lock (.NET 9/10 System.Threading.Lock),
  and lock-free primitives. Use when designing producer-consumer queues, background workers,
  async I/O pipelines, or debugging deadlocks and thread-pool starvation.
---

# .NET 10 Async & Concurrency Architecture

This skill covers best practices for high-throughput concurrency and asynchronous programming in .NET 10.

## 1. Golden Rules of Async / Await
- **Never block async code synchronously**: Never call `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on incomplete tasks. Doing so causes thread-pool starvation and deadlocks.
- **Avoid `async void`**: Only use `async Task` or `async ValueTask`. `async void` crashes the process on unhandled exceptions and cannot be awaited.
- **Propagate `CancellationToken` everywhere**: Every async method and loop must accept and honor `CancellationToken`.

## 2. Producer-Consumer Pipelines with `System.Threading.Channels`
- **Never use `ConcurrentQueue<T>` + `Thread.Sleep` or `Task.Delay` polling loops**.
- Always use `Channel.CreateBounded<T>` (for backpressure) or `Channel.CreateUnbounded<T>`:
  ```csharp
  var channel = Channel.CreateBounded<Message>(new BoundedChannelOptions(10_000)
  {
      FullMode = BoundedChannelFullMode.Wait,
      SingleWriter = false,
      SingleReader = true,
      AllowSynchronousContinuations = false
  });

  // Producer:
  await channel.Writer.WriteAsync(message, ct);

  // Consumer:
  await foreach (var item in channel.Reader.ReadAllAsync(ct))
  {
      await ProcessAsync(item, ct);
  }
  ```

## 3. Modern Synchronization Primitives
- **`System.Threading.Lock` (.NET 9/10)**: Prefer the dedicated `Lock` type over `lock (object)` for faster, cleaner synchronous mutual exclusion:
  ```csharp
  private readonly Lock _sync = new();
  public void UpdateState()
  {
      lock (_sync)
      {
          // critical section
      }
  }
  ```
- **Async Mutual Exclusion**: When awaiting inside a critical section, use `SemaphoreSlim(1, 1)` with `await semaphore.WaitAsync(ct)`. Never hold a `SemaphoreSlim` forever across an infinite streaming loop if other callers need the same lock to enqueue work.
- **Periodic Loops**: Use `PeriodicTimer` instead of `Task.Delay` in `while` loops or `System.Timers.Timer` with `async void` callbacks:
  ```csharp
  using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
  while (await timer.WaitForNextTickAsync(ct))
  {
      await RunMaintenanceAsync(ct);
  }
  ```

## 4. `ValueTask` vs `Task`
- Use `ValueTask` / `ValueTask<T>` when an operation frequently completes synchronously (e.g., reading from a buffered channel or memory cache).
- **Rules for `ValueTask`**: Never await a `ValueTask` more than once, never await it concurrently, and never call `.GetAwaiter().GetResult()` before it has completed.
