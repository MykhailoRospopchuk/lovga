#!/bin/bash

echo "================================================="
echo "        LOVGA ADVANCED STRESS TEST SUITE         "
echo "================================================="
echo "Building projects in Release configuration..."
dotnet build LovgaBroker/LovgaBroker.csproj -c Release --nologo -v q
dotnet build LovgaClient/LovgaClient.csproj -c Release --nologo -v q
dotnet build LovgaPublisher/LovgaPublisher.csproj -c Release --nologo -v q

mkdir -p logs
REPORT_FILE="logs/stress_test_report.md"

echo "# Lovga Advanced Stress Test Report" > $REPORT_FILE
echo "Generated on: $(date)" >> $REPORT_FILE
echo "" >> $REPORT_FILE

run_scenario() {
    local SCENARIO_NAME=$1
    local TOPICS=$2
    local CONCURRENCY=$3
    local MESSAGES_PER_PUB=$4
    local PAYLOAD_SIZE=$5
    local NUM_PUBLISHERS=$6
    local NUM_SUBSCRIBERS=$7
    local NUM_CONSUMER_GROUPS=$8

    echo "================================================="
    echo "Running Scenario: $SCENARIO_NAME"
    echo "================================================="
    
    # Clean storage and logs
    mkdir -p .storage_database
    rm -f .storage_database/StorageDatabase.db*
    rm -f logs/broker.log logs/client-*.log logs/publisher-*.log

    # Start Broker
    dotnet LovgaBroker/bin/Release/net9.0/LovgaBroker.dll > logs/broker.log 2>&1 &
    BROKER_PID=$!
    sleep 3 # Wait for broker to boot

    # Start Subscribers
    CLIENT_PIDS=()
    for g in $(seq 1 $NUM_CONSUMER_GROUPS); do
        for i in $(seq 1 $NUM_SUBSCRIBERS); do
            dotnet LovgaClient/bin/Release/net9.0/LovgaClient.dll --concurrency $CONCURRENCY --topics $TOPICS --group "group-$g" > "logs/client-g${g}-${i}.log" 2>&1 &
            CLIENT_PIDS+=($!)
        done
    done

    sleep 2 # Let clients connect

    # Start Publishers
    START_TIME=$(date +%s)
    PUBLISHER_PIDS=()
    for i in $(seq 1 $NUM_PUBLISHERS); do
        dotnet LovgaPublisher/bin/Release/net9.0/LovgaPublisher.dll --concurrency $CONCURRENCY --topics $TOPICS --messages $MESSAGES_PER_PUB --pipeline 10 --size $PAYLOAD_SIZE > "logs/publisher-$i.log" 2>&1 &
        PUBLISHER_PIDS+=($!)
    done

    # Wait for publishers
    for pid in "${PUBLISHER_PIDS[@]}"; do
        wait $pid
    done
    END_TIME=$(date +%s)
    DURATION=$((END_TIME - START_TIME))
    if [ "$DURATION" -eq 0 ]; then DURATION=1; fi

    TOTAL_PUB_MESSAGES=$((NUM_PUBLISHERS * CONCURRENCY * MESSAGES_PER_PUB))
    PUB_THROUGHPUT=$((TOTAL_PUB_MESSAGES / DURATION))

    echo "Publishers finished! Waiting 5 seconds for clients to settle ACKs..."
    sleep 5

    # Kill subscribers and broker cleanly with SIGINT so they print summaries if possible, otherwise SIGTERM
    for pid in "${CLIENT_PIDS[@]}"; do
        kill -INT $pid 2>/dev/null || kill $pid 2>/dev/null
    done
    kill $BROKER_PID 2>/dev/null

    # Gather client stats from the last dashboard print
    TOTAL_RECEIVED=0
    for g in $(seq 1 $NUM_CONSUMER_GROUPS); do
        for i in $(seq 1 $NUM_SUBSCRIBERS); do
            # Extract "Messages Received: X" from client logs
            RECV=$(cat "logs/client-g${g}-${i}.log" | grep "Messages Received:" | tail -n 1 | awk '{print $3}' | tr -d ',')
            if [ -n "$RECV" ]; then
                TOTAL_RECEIVED=$((TOTAL_RECEIVED + RECV))
            fi
        done
    done

    # Output to terminal
    echo "Scenario $SCENARIO_NAME Completed in ${DURATION}s."
    echo "Published: $TOTAL_PUB_MESSAGES msgs (~$PUB_THROUGHPUT msg/s)"
    echo "Received : $TOTAL_RECEIVED msgs"

    # Append to Report
    echo "## $SCENARIO_NAME" >> $REPORT_FILE
    echo "- **Topics**: $TOPICS" >> $REPORT_FILE
    echo "- **Concurrency/Process**: $CONCURRENCY" >> $REPORT_FILE
    echo "- **Message Size**: ${PAYLOAD_SIZE} bytes" >> $REPORT_FILE
    echo "- **Publishers**: $NUM_PUBLISHERS processes" >> $REPORT_FILE
    echo "- **Subscribers**: $NUM_SUBSCRIBERS processes per group" >> $REPORT_FILE
    echo "- **Consumer Groups**: $NUM_CONSUMER_GROUPS" >> $REPORT_FILE
    echo "- **Total Published**: $TOTAL_PUB_MESSAGES" >> $REPORT_FILE
    echo "- **Publish Throughput**: ~$PUB_THROUGHPUT msg/sec" >> $REPORT_FILE
    echo "- **Total Received by Consumers**: $TOTAL_RECEIVED" >> $REPORT_FILE
    echo "" >> $REPORT_FILE
}

# SCENARIO_NAME | TOPICS | CONCURRENCY | MSGS_PER_PUB | SIZE | PUBS | SUBS | GROUPS

# 1. High Throughput, small payloads, single consumer group
run_scenario "High Throughput (Small Messages)" 10 20 5000 100 4 4 1

# 2. Large Payloads, moderate throughput
run_scenario "Large Payloads (10KB Messages)" 5 10 2000 10240 2 2 1

# 3. Fan-out to Multiple Consumer Groups (Message Duplication)
run_scenario "Fan-out (Multiple Consumer Groups)" 5 20 2000 100 2 2 5

# 4. Extreme Concurrency (Many Topics, Clients, and Publishers)
run_scenario "Extreme Concurrency Cluster" 50 50 1000 100 5 5 3

# 5. Massive Publishers, Few Subscribers (Write Heavy)
run_scenario "Write Heavy Workload" 10 50 2000 50 10 1 1

echo "================================================="
echo "All Scenarios Completed!"
echo "Report saved to $REPORT_FILE"
cat $REPORT_FILE
