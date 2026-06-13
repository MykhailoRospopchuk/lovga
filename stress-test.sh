#!/bin/bash

# Configuration
BROKER_PORT=8080
TOPICS=5
CONCURRENCY=10
MESSAGES=1000
PIPELINE=10
PAYLOAD_SIZE=100
CLIENT_PROCESSES=3
PUBLISHER_PROCESSES=3

echo "================================================="
echo "        LOVGA STRESS TEST COORDINATOR           "
echo "================================================="
echo "Building projects in Release configuration..."
dotnet build LovgaBroker/LovgaBroker.csproj -c Release
dotnet build LovgaClient/LovgaClient.csproj -c Release
dotnet build LovgaPublisher/LovgaPublisher.csproj -c Release

# Create storage dir if it doesn't exist
mkdir -p .storage_database
rm -f .storage_database/StorageDatabase.db

# Create logs directory and clean previous logs
mkdir -p logs
rm -f logs/broker.log logs/client-*.log logs/publisher-*.log

# Start the Broker
echo "Starting Broker..."
dotnet LovgaBroker/bin/Release/net9.0/LovgaBroker.dll > logs/broker.log 2>&1 &
BROKER_PID=$!
sleep 3 # Wait for broker to boot

# Start Subscribers (Clients)
echo "Starting $CLIENT_PROCESSES subscriber processes (Total connections: $((CLIENT_PROCESSES * CONCURRENCY)))..."
CLIENT_PIDS=()
for i in $(seq 1 $CLIENT_PROCESSES); do
    dotnet LovgaClient/bin/Release/net9.0/LovgaClient.dll --concurrency $CONCURRENCY --topics $TOPICS > "logs/client-$i.log" 2>&1 &
    CLIENT_PIDS+=($!)
done

sleep 1

# Start Publishers
echo "Starting $PUBLISHER_PROCESSES publisher processes (Total load: $((PUBLISHER_PROCESSES * CONCURRENCY * MESSAGES)) messages)..."
PUBLISHER_PIDS=()
for i in $(seq 1 $PUBLISHER_PROCESSES); do
    dotnet LovgaPublisher/bin/Release/net9.0/LovgaPublisher.dll --concurrency $CONCURRENCY --topics $TOPICS --messages $MESSAGES --pipeline $PIPELINE --size $PAYLOAD_SIZE > "logs/publisher-$i.log" 2>&1 &
    PUBLISHER_PIDS+=($!)
done

echo "Waiting for publishers to complete..."
for pid in "${PUBLISHER_PIDS[@]}"; do
    wait $pid
done

echo "Publishers finished! Waiting 5 seconds for clients to settle ACKs..."
sleep 5

# Kill subscribers and broker
echo "Stopping subscribers..."
for pid in "${CLIENT_PIDS[@]}"; do
    kill $pid 2>/dev/null
done

echo "Stopping broker..."
kill $BROKER_PID 2>/dev/null

echo "Stress test finished!"
echo "-------------------------------------------------"
echo "Publisher 1 Summary Output:"
cat logs/publisher-1.log | grep -A 15 "SUMMARY" || tail -n 15 logs/publisher-1.log
echo "-------------------------------------------------"
echo "Client 1 Dashboard Snapshot:"
tail -n 15 logs/client-1.log
