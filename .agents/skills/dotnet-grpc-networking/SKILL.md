---
name: dotnet-grpc-networking
description: >-
  Guides high-performance gRPC services, Protobuf contract design, bi-directional streaming,
  Kestrel HTTP/2 and HTTP/3 tuning, SocketsHttpHandler pooling, and System.IO.Pipelines in .NET 10.
  Use when building or debugging gRPC servers/clients, streaming brokers, backpressure, or network protocols.
---

# .NET 10 gRPC & High-Throughput Networking

This skill covers building resilient, high-throughput gRPC and network services in .NET 10.

## 1. ProtobufZero-Copy & Serialization Best Practices
- **Use `Google.Protobuf.UnsafeByteOperations.UnsafeWrap(ReadOnlyMemory<byte>)`** when constructing `ByteString` from pooled or read-only buffers to avoid copying byte arrays twice.
- **Avoid `ByteString.CopyFrom(byte[])`** when you only need a slice or already hold a `ReadOnlyMemory<byte>`.
- Design `.proto` contracts with explicit ack/offset fields, batch envelopes, and error codes rather than empty `google.protobuf.Empty` responses when reliability is required.

## 2. Bi-Directional & Server Streaming Concurrency
- **`IServerStreamWriter<T>` is NOT thread-safe**: Only one task may call `WriteAsync` on an `IServerStreamWriter<T>` at a time. Serialize writes through a single consumer loop reading from a `Channel<T>`.
- **Decouple Producer and Consumer Locks**: Never hold a shared storage or topic lock across the entire lifetime of a server-streaming RPC (`while (!ct.IsCancellationRequested)`), as that blocks publishers from writing new messages to the topic!
- **Graceful Stream Termination**: Always pass `context.CancellationToken` to `requestStream.MoveNext(ct)` and `responseStream.WriteAsync(msg, ct)`. Catch `OperationCanceledException` and `RpcException(StatusCode.Cancelled)` cleanly when clients disconnect.

## 3. gRPC Client Channel Pooling & Resilience
- **`GrpcChannel` Reuse**: `GrpcChannel` is expensive to create and thread-safe. Create and cache one `GrpcChannel` per endpoint (or use `AddGrpcClient<T>()` with `IHttpClientFactory`), never `GrpcChannel.ForAddress(...)` per message.
- **Enable Multiple HTTP/2 Connections**: Under high concurrency (>100 concurrent streams), enable `SocketsHttpHandler.EnableMultipleHttp2Connections = true`:
  ```csharp
  var handler = new SocketsHttpHandler
  {
      EnableMultipleHttp2Connections = true,
      KeepAlivePingDelay = TimeSpan.FromSeconds(30),
      KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
      PooledConnectionIdleTimeout = TimeSpan.InfiniteTimeSpan
  };
  var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpHandler = handler });
  ```
