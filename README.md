# Lovga Message Broker

The reason for this project is to learn something new by reinventing the wheel—specifically, a light, concurrent, persistent message broker built on gRPC and SQLite.

## Project Structure

The project consists of the following components:
- **LovgaBroker**: The core message broker service. It persists messages to SQLite, manages topic subscription streams, and dispatches messages concurrently.
- **LovgaCommon**: Protobuf definitions and common contracts shared between components.
- **LovgaSatellite**: A client integration package for target applications to easily connect to the broker.
- **LovgaClient**: High-concurrency subscriber application used for load testing.
- **LovgaPublisher**: High-concurrency publisher application used for load testing.
- **TestProject**: Satellite API integration testing endpoint.

---

## 🚀 How to Run the Broker

You can run the broker natively on your local system or inside a Docker container.

### Option A: Run Natively
To quickly spin up the broker locally:
```bash
dotnet run --project LovgaBroker/LovgaBroker.csproj
```

### Option B: Run in Docker
1. Pack `LovgaCommon` into the local nuget directory:
   ```bash
   dotnet pack LovgaCommon/LovgaCommon.csproj --output .local_nuget/
   ```
2. Build the Docker image:
   ```bash
   docker build . --tag broker
   ```
3. Run the container (mounting the persistence volume):
   ```bash
   docker run --name "broker" -p 8080:8080 -v $(pwd)/.storage_database:/.storage_database broker
   ```

---

## 🔥 Performance & Load Testing

We have built-in high-concurrency testing suites inside `LovgaClient` (subscribers) and `LovgaPublisher` (publishers) to stress-test the broker across multiple topics and connections.

### 1. Interactive Config Mode
Run either application without arguments to launch a guided, interactive configuration menu:
```bash
# Start the subscriber client
dotnet run --project LovgaClient/LovgaClient.csproj

# Start the load publisher
dotnet run --project LovgaPublisher/LovgaPublisher.csproj
```

### 2. Command-Line CLI Mode (Automated Run)
You can customize the load parameters directly using command line arguments (this mode starts execution automatically without prompt blocks).

#### Parameters for `LovgaClient` (Subscribers)
* `--host <string>`: Broker host (default: `localhost`)
* `--port <int>`: Broker port (default: `8080`)
* `--concurrency <int>`: Number of parallel connection streams (default: `10`)
* `--topics <int>`: Number of active topics to divide subscribers into (default: `5`)
* `--topic-base <string>`: Base name for topics (default: `bobr-topic`)
* `--legacy`: Fallback to legacy single-stream test mode

*Example:* Start 20 client subscribers across 5 topics:
```bash
dotnet run --project LovgaClient/LovgaClient.csproj -- --concurrency 20 --topics 5
```

#### Parameters for `LovgaPublisher` (Publishers)
* `--host <string>`: Broker host (default: `localhost`)
* `--port <int>`: Broker port (default: `8080`)
* `--concurrency <int>`: Number of concurrent publishing tasks (default: `10`)
* `--topics <int>`: Number of target topics to distribute messages to (default: `5`)
* `--messages <int>`: Number of messages to publish per concurrent worker (default: `1000`)
* `--delay <int>`: Delay (ms) between publishes (default: `0` for max load)
* `--size <int>`: Payload padding size in bytes (default: `100`)
* `--topic-base <string>`: Base name for topics (default: `bobr-topic`)
* `--legacy`: Fallback to legacy single-publisher test mode

*Example:* Publish 50,000 messages total (50 workers × 1000 messages each) across 10 topics with 100-byte payloads and no delay:
```bash
dotnet run --project LovgaPublisher/LovgaPublisher.csproj -- --concurrency 50 --topics 10 --messages 1000 --delay 0 --size 100
```

---

## 📊 Live Metrics & Latency Tracking

Both client and publisher feature interactive, real-time terminal dashboards:

* **End-to-End Latency Tracking**: The publisher automatically embeds high-resolution timestamps (`DateTime.UtcNow.Ticks`) into the message payload. When the client receives a message, it parses the payload and calculates the precise end-to-end processing and dispatch latency (Min/Max/Avg) in milliseconds.
* **Throughput Measurement**: Computes exact incoming/outgoing message counts and actual message throughput per second (`msg/sec`).
