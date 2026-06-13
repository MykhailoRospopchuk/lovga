# Lovga — Code Review

> A RabbitMQ-inspired message broker written in C# (.NET 9) with gRPC transport.

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                         LovgaBroker (Docker)                    │
│                                                                 │
│  gRPC :8080                                                     │
│  ┌──────────────────┐   ┌──────────────────┐                    │
│  │ PublisherGrpcSvr │   │ SubscriberGrpcSvr│                    │
│  └────────┬─────────┘   └─────── ─┬─────────┘                   │
│           │  Publish              │  Subscribe/Unsubscribe      │
│           ▼                       ▼                             │
│       IReceiver              IBrokerManager                     │
│    (ReceiverService)       (BrokerManager)                      │
│           │                       │                             │
│           │  enqueue              │  creates per-topic          │
│           ▼                       ▼                             │
│    Channel<Message>  ──►  IMessageBroker (per topic)            │
│                              │                                  │
│                              │  dispatch loop                   │
│                              ▼                                  │
│                     ConsumerGrpcClient ──► subscriber gRPC :N   │
│                                                                 │
│  SQLite (persistence / dead-letter storage)                     │
└─────────────────────────────────────────────────────────────────┘

Publisher App ──► gRPC Publish ──► Broker ──► gRPC Notify ──► Subscriber App
                                                               (LovgaSatellite)
```

The architecture is well-thought-out for a pet project:
- **Separation of concerns** — receiving, routing, and dispatching are split across distinct services.
- **Topic isolation** — each topic gets its own `MessageBroker` instance with its own `Channel<Message>`.
- **Persistence** — SQLite is used to survive restarts; messages are loaded back on startup.
- **Dead Letter Queue** — failed deliveries are routed to a reserved topic.
- **NuGet satellite** — clean distribution model for client-side integration.

---

## What Was Done Well ✅

### 1. Good use of `System.Threading.Channels`
`Channel<Message>` is exactly the right tool here. It's lock-free, supports backpressure, and integrates perfectly with `async/await`. Using `ReadAllAsync` in a `await foreach` loop is idiomatic.

### 2. Interfaces everywhere (mostly)
`IBrokerManager`, `IChannelManger`, `IReceiver`, `IMessageBroker` — good discipline. Makes the code testable even though there are no unit tests yet.

### 3. Dead Letter Queue concept
Having a DLQ is a mark of understanding real message broker design. The interceptor that blocks direct publishing to it is a nice touch.

### 4. Docker multi-stage build
The Dockerfile is clean: separate `nuget`, `build`, `publish`, and `final` stages. Using `mcr.microsoft.com/dotnet/runtime` (not full SDK) in the final image is correct for production.

### 5. Chunked message loading on startup
`GetChunkMessagesAsync` uses cursor-based pagination (not `OFFSET`) which is efficient. The `IAsyncEnumerable` pattern is modern and correct.

### 6. `Lock` (C# 13 / .NET 9)
Using `new Lock()` instead of a plain `object` for locking is a good .NET 9 awareness — it provides better diagnostics.

---

## Bugs & Correctness Issues 🐛

### BUG 1 — `GetAwaiter()` without `.GetResult()` silently drops the task
**File:** [ConsumerGrpcClient.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/ConsumerGrpcClient.cs#L81)

```csharp
// Current — the Task is never awaited; the delete never runs!
_storageService.DeleteMessage(message.Id).GetAwaiter();

// Should be:
await _storageService.DeleteMessage(message.Id);
// OR if you must be sync:
_storageService.DeleteMessage(message.Id).GetAwaiter().GetResult();
```
`.GetAwaiter()` alone returns an `TaskAwaiter` struct and throws it away. The deletion is completely skipped. This means **the SQLite database grows forever** — messages that were successfully delivered are never removed.

> [!CAUTION]
> This is in a `finally` block inside `DeliverMessage`, which is called from inside `Task.Run`. You need to make `DeliverMessage` return `Task<bool>` and properly `await` it.

---

### BUG 2 — Deadlock risk: `.GetAwaiter().GetResult()` inside an async call
**File:** [ReceiverService.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ReceiverService.cs#L26)

```csharp
// Current — blocking inside a ValueTask method
public ValueTask Publish(Message message)
{
    var id = _storageService.InsertMessage(message).GetAwaiter().GetResult();
    ...
}
```
This is called from `PublisherGrpcServer.Publish` which runs on the gRPC thread pool. While it may not deadlock in this specific context (no SynchronizationContext), it wastes a thread and is an anti-pattern. `Publish` should be `async Task` or `async ValueTask`.

---

### BUG 3 — Race condition in `BrokerManager.GetBroker`
**File:** [BrokerManager.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/BrokerManager.cs#L18-L33)

```csharp
// Two concurrent calls with the same topic can both reach here
// and create two separate MessageBroker instances
using var scope = _serviceScopeFactory.CreateAsyncScope();
broker = scope.ServiceProvider.GetRequiredService<IMessageBroker>();
broker.SetTopic(topic);
if (_brokers.TryAdd(topic, broker))   // only one wins, other is discarded
{
    OnBrokerAdded?.Invoke(broker);
}
return broker; // ← the loser returns the broker it created, not the one in the dict!
```
The losing thread returns its own freshly created broker (which is NOT in the dictionary), so messages from that thread's broker are dispatched independently and never tracked. Use `GetOrAdd` with a factory, or `Lazy<T>`.

```csharp
// Correct pattern:
return _brokers.GetOrAdd(topic, t =>
{
    using var scope = _serviceScopeFactory.CreateScope();
    var b = scope.ServiceProvider.GetRequiredService<IMessageBroker>();
    b.SetTopic(t);
    OnBrokerAdded?.Invoke(b);
    return b;
});
```

---

### BUG 4 — Dead-letter check in `DispatchAsync` is wrong after removals
**File:** [MessageBroker.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/MessageBroker.cs#L114)

```csharp
// keyToRemove.Count is the FAILED count, _subscribers.Count is CURRENT (already reduced)
if (keyToRemove.Count == _subscribers.Count)
{
    await EnqueueDeadMessage(message);
}
```
By the time this check runs, `_subscribers` has already had the failed keys removed (from `RemoveSubscriber` calls in the `foreach` above). So this condition can never be true when all subscribers fail — it compares `N` against `0`.

The check should capture the subscriber count **before** the removal loop:
```csharp
var originalCount = _subscribers.Count;
foreach (var key in keyToRemove)
    RemoveSubscriber(key);

if (keyToRemove.Count == originalCount)
    await EnqueueDeadMessage(message);
```

---

### BUG 5 — `ShutdownAsync` blocked with `.GetResult()` in channel manager
**File:** [ChannelManger.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ChannelManger.cs#L60)

```csharp
holder.channel.ShutdownAsync().GetAwaiter().GetResult();
```
This blocks a thread pool thread. Since `UnregisterConsumer` can be called from a gRPC handler, this can starve the thread pool under load. Make `ShutDownChannel` async and propagate it properly.

---

### BUG 6 — `SubscriberGrpcClient` uses `static` state — breaks multi-instance scenarios
**File:** [SubscriberGrpcClient.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaSatellite/GrpcClientServices/SubscriberGrpcClient.cs#L9)

```csharp
private static readonly string SubscriberId = Guid.NewGuid().ToString();
private static readonly HashSet<string> ActiveTopics = new();
```
All DI-injected instances share the **same** `SubscriberId` and `ActiveTopics`. This is registration-lifetime confusion — `SubscriberGrpcClient` is registered as `Scoped` in `TestProject`, but behaves as `Singleton` internally. If a user tries to subscribe from two different scopes (e.g. two HTTP requests simultaneously), the state becomes inconsistent.

---

## Design Concerns ⚠️

### 1. Subscriber registers BEFORE its gRPC server is ready
**Files:** [SubscriberGrpcServer.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/SubscriberGrpcServer.cs#L49-L58) + [GrpcServerHostedService.cs (Satellite)](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaSatellite/HostedServices/GrpcServerHostedService.cs)

The broker checks `ChannelExists` before accepting a subscription (correct!), but the satellite's gRPC server is started by `GrpcServerHostedService` which is an `IHostedService`. `AddHostedService` services start **after** `app.Run()` blocks, but the user calls `Subscribe()` from an HTTP endpoint. There is a window where a subscriber might try to subscribe before its own consumer gRPC server is actually listening.

### 2. `IConsumerGrpcClient` interface leaks `ConsumerGrpcClient` internals
**File:** [IMessageBroker.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Interfaces/IMessageBroker.cs#L11)

```csharp
bool Subscribe(string subscriberId, IConsumerGrpcClient consumerGrpcClient);
```
`IMessageBroker` depends on `IConsumerGrpcClient`, which is a gRPC transport detail. The broker shouldn't know about gRPC — it's a message router. A more decoupled design would use a generic `IMessageHandler` or `IDeliveryTarget` abstraction.

### 3. `SatelliteExtensions` uses static mutable state
**File:** [SatelliteExtensions.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaSatellite/API/SatelliteExtensions.cs)

All configuration is in `static` fields. This makes it impossible to configure two different broker connections in the same process, hard to test, and thread-unsafe during initialization (no lock on `_configuredChannel = true`). Consider an `IOptions<SatelliteOptions>` pattern instead.

### 4. `BrokerWorker.OnBrokerAdded` passes `CancellationToken.None`
**File:** [BrokerWorker.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/BackgroundServices/BrokerWorker.cs#L44)

```csharp
_dispatchers.TryAdd(broker.Topic, broker.DispatchAsync(CancellationToken.None));
```
Dynamically added brokers (after startup) ignore the cancellation token, so graceful shutdown won't stop them. Store the `CancellationToken` from `ExecuteAsync` and pass it here.

### 5. `PublisherGrpcServer` awaits nothing and always returns `Success = true`
**File:** [PublisherGrpcServer.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/PublisherGrpcServer.cs#L21-L34)

The `ValueTask` returned by `_receiver.Publish(...)` is discarded. If the channel is full or an error occurs, the publisher is told "success" anyway.

### 6. No message acknowledgment (at-most-once delivery)
The broker deletes a message from SQLite as soon as `client.Notify(...)` returns, without waiting for the consumer to confirm actual processing. If the consumer crashes after receiving but before processing, the message is lost. This is an "at-most-once" guarantee. For a learning project this is fine, but worth being explicit about.

### 7. `Grpc.Core` is deprecated (soft)
The project uses the legacy `Grpc.Core` NuGet package for both server and client. Microsoft recommends migrating to `Grpc.AspNetCore` (for server) and `Grpc.Net.Client` (for client). The mix is already partially there (`Grpc.Net.Client` is referenced but only `Grpc.Core.Channel` is actually used). A full migration to `Grpc.AspNetCore` would simplify DI, logging, and health checks significantly.

---

## Minor / Style Notes 📝

| Location | Issue |
|---|---|
| [ChannelManger.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ChannelManger.cs) | Typo in class name: `ChannelManger` → `ChannelManager` |
| [MessageBroker.cs L82](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/MessageBroker.cs#L82) | `$"{counter++}/{_queues.Reader.Count}"` — `Count` on an unbounded channel is always 0 after read; use a separate counter |
| [SubscriberGrpcServer.cs L67](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/SubscriberGrpcServer.cs#L67) | `$"{0} attempt..."` — the placeholder `{0}` is a string, not a format arg; should be `{(result ? "Success" : "Failed")}` |
| [QueueTopic.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaCommon/Constants/QueueTopic.cs) | `static readonly string` could be `const string` |
| [README.md L31](file:///home/mykhailo/dev/pet/lovga/lovga/README.md#L31) | Hardcoded `/home/mykhailo/dev/pet/lovga/lovga` path — use `$(pwd)` |
| [LovgaPublisher/Program.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaPublisher/Program.cs) | Both `reply2` and `reply3` print `"Error2"` — copy-paste artifact |
| [DataContext.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Storage/DataContext.cs) | `CreatedAt INTEGER` — consider adding a `Delivered` or `Status` column for true at-least-once |
| General | No unit tests exist — even a handful for `MessageBroker` logic would be valuable |
| General | `ILogger` is injected into `ConsumerGrpcClient` but is a `Transient` service — fine, but watch for excessive allocations under load |

---

## Priority Fix List

| Priority | Issue | File |
|---|---|---|
| 🔴 Critical | `GetAwaiter()` without `.GetResult()` — deletes never run | [ConsumerGrpcClient.cs L81](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/ConsumerGrpcClient.cs#L81) |
| 🔴 Critical | Race condition in `GetBroker` — can create orphan brokers | [BrokerManager.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/BrokerManager.cs) |
| 🟠 High | Dead-letter condition always false | [MessageBroker.cs L114](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/MessageBroker.cs#L114) |
| 🟠 High | `.GetAwaiter().GetResult()` blocking in async path | [ReceiverService.cs L26](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ReceiverService.cs#L26) |
| 🟡 Medium | `CancellationToken.None` for dynamic dispatchers | [BrokerWorker.cs L44](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/BackgroundServices/BrokerWorker.cs#L44) |
| 🟡 Medium | `PublisherGrpcServer` discards the publish `ValueTask` | [PublisherGrpcServer.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/PublisherGrpcServer.cs) |
| 🟡 Medium | Static state in `SatelliteExtensions` and `SubscriberGrpcClient` | [SatelliteExtensions.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaSatellite/API/SatelliteExtensions.cs) |
| 🟢 Low | Typo `ChannelManger` | [ChannelManger.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ChannelManger.cs) |
| 🟢 Low | Log format string bug `{0}` | [SubscriberGrpcServer.cs L67](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/SubscriberGrpcServer.cs#L67) |

---

## Summary

This is a genuinely solid pet project. The core idea — per-topic `Channel<T>` dispatch loops driven by background workers, with SQLite persistence and a separate satellite NuGet for clients — maps cleanly to real message broker concepts. The layering (gRPC layer → service layer → storage layer) is clean and well-structured for its size.

The most important thing to fix are the **three correctness bugs** (the silent `GetAwaiter()`, the `GetBroker` race, and the dead-letter check). Everything else is polish. For a learning project targeting skill growth, this already demonstrates a strong grasp of: async/await, DI, gRPC, Docker, NuGet packaging, and concurrent data structures. Great work.
