---
name: dotnet-testing-diagnostics
description: >-
  Guides unit, integration, and stress testing in .NET 10 using xUnit, WebApplicationFactory,
  TimeProvider, Bogus, and concurrency testing. Covers structured logging with LoggerMessage source generators
  and OpenTelemetry metrics/tracing. Use when writing tests, fixing flaky async tests, or adding observability.
---

# .NET 10 Testing, Observability & Diagnostics

This skill covers reliable testing and low-overhead observability in .NET 10.

## 1. Deterministic Async & Concurrency Testing
- **Avoid `Thread.Sleep` or arbitrary `Task.Delay` in tests**: Use `TaskCompletionSource`, `Channel<T>`, or `FakeTimeProvider` (`Microsoft.Extensions.TimeProvider.Testing`) to coordinate async assertions deterministically.
- **Temp File & Directory Isolation**: In storage tests, always create a unique temp directory per test (`Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))`) and clean it up in `IDisposable.Dispose()` / `IAsyncLifetime.DisposeAsync()`.
- **Integration Testing gRPC**: Use `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<TEntryPoint>` to test gRPC services in-memory without binding real TCP ports.

## 2. High-Performance Structured Logging
- **Never use `Console.WriteLine` in production services**: It is synchronous, globally locked on `Console.Out`, and lacks structured context.
- Use `ILogger<T>` with compile-time `[LoggerMessage]` source generators to avoid boxing and string interpolation when a log level is disabled:
  ```csharp
  public static partial class BrokerLogs
  {
      [LoggerMessage(Level = LogLevel.Information, Message = "Topic {Topic} appended message at offset {Offset} ({Bytes} bytes)")]
      public static partial void LogMessageAppended(this ILogger logger, string topic, long offset, int bytes);

      [LoggerMessage(Level = LogLevel.Warning, Message = "Corrupted record detected in {FilePath} at offset {Offset}; truncating segment")]
      public static partial void LogCorruptedRecord(this ILogger logger, string filePath, long offset);
  }
  ```

## 3. Error Handling & Result Patterns
- Use specific exception types (`InvalidDataException`, `ArgumentOutOfRangeException`, `RpcException`) rather than swallowing `catch (Exception)` silently.
- For expected domain outcomes (e.g., CRC mismatch, EOF, backpressure full), prefer returning a `readonly record struct Result<T>` or `bool TryRead(...)` over throwing exceptions on the hot path.
