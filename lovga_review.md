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

### ~~BUG 1 — `GetAwaiter()` without `.GetResult()` silently drops the task~~ ✅ Fixed
**File:** [ConsumerGrpcClient.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/ConsumerGrpcClient.cs)

`DeliverMessage` was changed from `bool` to `async Task<bool>`. The `finally` block now properly `await`s `DeleteMessage`. `IConsumerGrpcClient` interface updated to match.

```csharp
// Fixed — message is now deleted after delivery
public async Task<bool> DeliverMessage(Message message)
{
    ...
    finally
    {
        await _storageService.DeleteMessage(message.Id);
    }
}
```

---

### ~~BUG 2 — Deadlock risk: `.GetAwaiter().GetResult()` inside an async call~~ ✅ Fixed
**File:** [ReceiverService.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ReceiverService.cs)

`Publish` is now `async ValueTask`. The SQLite insert and channel write are both properly awaited, eliminating the thread-blocking anti-pattern.

```csharp
// Fixed
public async ValueTask Publish(Message message)
{
    var id = await _storageService.InsertMessage(message);
    message.SetId(id);
    await _messages.Writer.WriteAsync(message);
}
```

---

### ~~BUG 3 — Race condition in `BrokerManager.GetBroker`~~ ✅ Fixed
**File:** [BrokerManager.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/BrokerManager.cs)

Replaced the racy `TryGetValue` + `TryAdd` pattern with the atomic `GetOrAdd` factory. The losing thread can no longer return an orphan broker.

```csharp
// Fixed — atomic, no orphan brokers possible
return _brokers.GetOrAdd(topic, t =>
{
    using var scope = _serviceScopeFactory.CreateScope();
    var broker = scope.ServiceProvider.GetRequiredService<IMessageBroker>();
    broker.SetTopic(t);
    OnBrokerAdded?.Invoke(broker);
    return broker;
});
```

---

### ~~BUG 4 — Dead-letter check in `DispatchAsync` is wrong after removals~~ ✅ Fixed
**File:** [MessageBroker.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/MessageBroker.cs)

`originalCount` is now captured **before** the `try/finally` block (ensuring it's in scope in `finally`), then compared against `keyToRemove.Count` after the removal loop.

```csharp
// Fixed — count captured before removals, so comparison is correct
var originalCount = _subscribers.Count;
try { ... }
finally
{
    foreach (var key in keyToRemove) RemoveSubscriber(key);
    if (keyToRemove.Count == originalCount)
        await EnqueueDeadMessage(message);
}
```

---

### ~~BUG 5 — `ShutdownAsync` blocked with `.GetResult()` in channel manager~~ ✅ Fixed
**File:** [ChannelManger.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ChannelManger.cs)

Replaced with fire-and-forget via `Task.Run`. The gRPC handler thread returns immediately; shutdown completes asynchronously on the thread pool.

```csharp
// Fixed — no longer blocks the caller
_ = Task.Run(() => holder.channel.ShutdownAsync());
```

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

### 4. ~~`BrokerWorker.OnBrokerAdded` passes `CancellationToken.None`~~ ✅ Fixed
**File:** [BrokerWorker.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/BackgroundServices/BrokerWorker.cs)

The `CancellationToken` from `ExecuteAsync` is now stored as `_cancellationToken` and passed to all dynamically-added broker dispatchers. Graceful shutdown now correctly stops them.

```csharp
// Fixed
private CancellationToken _cancellationToken = CancellationToken.None;

protected override async Task ExecuteAsync(CancellationToken cancellationToken)
{
    _cancellationToken = cancellationToken;
    ...
}
// In OnBrokerAdded:
_dispatchers.TryAdd(broker.Topic, broker.DispatchAsync(_cancellationToken));
```

### 5. ~~`PublisherGrpcServer` awaits nothing and always returns `Success = true`~~ ✅ Fixed
**File:** [PublisherGrpcServer.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/PublisherGrpcServer.cs)

`Publish` is now `async` and properly `await`s `_receiver.Publish(...)`. Errors propagate through gRPC's exception handling instead of being silently swallowed.

### 6. No message acknowledgment (at-most-once delivery)
The broker deletes a message from SQLite as soon as `client.Notify(...)` returns, without waiting for the consumer to confirm actual processing. If the consumer crashes after receiving but before processing, the message is lost. This is an "at-most-once" guarantee. For a learning project this is fine, but worth being explicit about.

### 7. `Grpc.Core` is deprecated (soft)
The project uses the legacy `Grpc.Core` NuGet package for both server and client. Microsoft recommends migrating to `Grpc.AspNetCore` (for server) and `Grpc.Net.Client` (for client). The mix is already partially there (`Grpc.Net.Client` is referenced but only `Grpc.Core.Channel` is actually used). A full migration to `Grpc.AspNetCore` would simplify DI, logging, and health checks significantly.

---

## Minor / Style Notes 📝

| Location | Issue | Status |
|---|---|---|
| [ChannelManger.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ChannelManger.cs) | Typo in class name: `ChannelManger` → `ChannelManager` | ⏳ Open |
| [MessageBroker.cs L83](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/MessageBroker.cs#L83) | `$"{counter++}/{_queues.Reader.Count}"` — `Count` on an unbounded channel is always 0 after read; use a separate counter | ⏳ Open |
| [SubscriberGrpcServer.cs L67](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/SubscriberGrpcServer.cs#L67) | `$"{0} attempt..."` — the placeholder `{0}` was a string, not a format arg | ✅ Fixed |
| [QueueTopic.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaCommon/Constants/QueueTopic.cs) | `static readonly string` could be `const string` | ✅ Fixed |
| [README.md L31](file:///home/mykhailo/dev/pet/lovga/lovga/README.md#L31) | Hardcoded `/home/mykhailo/dev/pet/lovga/lovga` path — use `$(pwd)` | ⏳ Open |
| [LovgaPublisher/Program.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaPublisher/Program.cs) | Both `reply2` and `reply3` print `"Error2"` — copy-paste artifact | ✅ Fixed |
| [DataContext.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Storage/DataContext.cs) | `CreatedAt INTEGER` — consider adding a `Delivered` or `Status` column for true at-least-once | ⏳ Open |
| General | No unit tests exist — even a handful for `MessageBroker` logic would be valuable | ⏳ Open |
| General | `ILogger` is injected into `ConsumerGrpcClient` but is a `Transient` service — fine, but watch for excessive allocations under load | ⏳ Open |

---

## Priority Fix List

| Priority | Issue | File | Status |
|---|---|---|---|
| 🔴 Critical | `GetAwaiter()` without `.GetResult()` — deletes never run | [ConsumerGrpcClient.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/ConsumerGrpcClient.cs) | ✅ Fixed |
| 🔴 Critical | Race condition in `GetBroker` — can create orphan brokers | [BrokerManager.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/BrokerManager.cs) | ✅ Fixed |
| 🟠 High | Dead-letter condition always false | [MessageBroker.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/MessageBroker.cs) | ✅ Fixed |
| 🟠 High | `.GetAwaiter().GetResult()` blocking in async path | [ReceiverService.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ReceiverService.cs) | ✅ Fixed |
| 🟠 High | `ShutdownAsync` blocking on gRPC handler thread | [ChannelManger.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ChannelManger.cs) | ✅ Fixed |
| 🟡 Medium | `CancellationToken.None` for dynamic dispatchers | [BrokerWorker.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/BackgroundServices/BrokerWorker.cs) | ✅ Fixed |
| 🟡 Medium | `PublisherGrpcServer` discards the publish `ValueTask` | [PublisherGrpcServer.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/PublisherGrpcServer.cs) | ✅ Fixed |
| 🟡 Medium | Static state in `SatelliteExtensions` and `SubscriberGrpcClient` | [SatelliteExtensions.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaSatellite/API/SatelliteExtensions.cs) | ⏳ Open |
| 🟢 Low | Typo `ChannelManger` | [ChannelManger.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/Services/ChannelManger.cs) | ⏳ Open |
| 🟢 Low | Log format string bug `{0}` | [SubscriberGrpcServer.cs](file:///home/mykhailo/dev/pet/lovga/lovga/LovgaBroker/GrpcServices/SubscriberGrpcServer.cs) | ✅ Fixed |

---

## Summary

This is a genuinely solid pet project. The core idea — per-topic `Channel<T>` dispatch loops driven by background workers, with SQLite persistence and a separate satellite NuGet for clients — maps cleanly to real message broker concepts. The layering (gRPC layer → service layer → storage layer) is clean and well-structured for its size.

**All critical and high-priority correctness bugs have been fixed** (commit `34355de`):
- SQLite deletes now actually run (`DeliverMessage` is properly `async`)
- `GetBroker` is race-free via `GetOrAdd`
- Dead-letter queue now correctly detects all-subscribers-failed
- `ReceiverService.Publish` is fully async
- `ChannelManger.ShutDownChannel` no longer blocks the gRPC thread pool
- Dynamic dispatchers now respect the shutdown `CancellationToken`
- `PublisherGrpcServer` now awaits the publish before returning

**Remaining open items** (all low risk for a pet project):
- `ChannelManger` class/interface typo rename (high churn, cosmetic)
- `MessageBroker` log counter (`_queues.Reader.Count` always 0 on unbounded channel)
- Static state in `SubscriberGrpcClient` and `SatelliteExtensions` (architectural refactor)
- `README.md` hardcoded path
- No unit tests

For a learning project targeting skill growth, this demonstrates a strong grasp of: async/await, DI, gRPC, Docker, NuGet packaging, and concurrent data structures. Great work.
