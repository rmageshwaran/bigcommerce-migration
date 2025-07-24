#!/bin/bash

# Real-Time Migration Progress E2E Testing Script
# This script starts both the backend Azure Functions and frontend dashboard
# for comprehensive real-time progress testing

set -e  # Exit on any error

echo "🚀 Starting BigCommerce Migration Real-Time Progress Testing..."
echo "============================================================="

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to check if a port is in use
check_port() {
    if lsof -Pi :$1 -sTCP:LISTEN -t >/dev/null 2>&1; then
        return 0  # Port is in use
    else
        return 1  # Port is free
    fi
}

# Function to wait for service to be ready
wait_for_service() {
    local url=$1
    local service_name=$2
    local max_attempts=30
    local attempt=1
    
    echo -e "${YELLOW}Waiting for $service_name to be ready...${NC}"
    
    while [ $attempt -le $max_attempts ]; do
        if curl -s "$url" >/dev/null 2>&1; then
            echo -e "${GREEN}✅ $service_name is ready!${NC}"
            return 0
        fi
        
        echo -e "${YELLOW}⏳ Attempt $attempt/$max_attempts - waiting for $service_name...${NC}"
        sleep 2
        ((attempt++))
    done
    
    echo -e "${RED}❌ $service_name failed to start after $max_attempts attempts${NC}"
    return 1
}

# Check if we're in the right directory
if [ ! -f "src/BigCommerce.Migration.Functions/BigCommerce.Migration.Functions.csproj" ]; then
    echo -e "${RED}❌ Error: Please run this script from the root project directory${NC}"
    exit 1
fi

echo -e "${BLUE}📋 Pre-flight Checks${NC}"
echo "================================"

# Check if Azure Functions port is available
if check_port 7071; then
    echo -e "${YELLOW}⚠️  Port 7071 is already in use (Azure Functions might be running)${NC}"
    echo "   Attempting to continue..."
else
    echo -e "${GREEN}✅ Port 7071 is available${NC}"
fi

# Check if Dashboard port is available  
if check_port 5173; then
    echo -e "${YELLOW}⚠️  Port 5173 is already in use (Dashboard might be running)${NC}"
    echo "   Attempting to continue..."
else
    echo -e "${GREEN}✅ Port 5173 is available${NC}"
fi

# Create temporary environment file for dashboard
echo -e "${BLUE}🔧 Setting up Dashboard Environment${NC}"
echo "================================"

cat > src/BigCommerce.Migration.Dashboard/.env.local << EOF
# Development Environment Configuration for Real-Time Testing
VITE_API_BASE_URL=http://localhost:7071/api
VITE_API_KEY=development-key
VITE_SIGNALR_HUB_URL=http://localhost:7071/api/negotiate
VITE_RECONNECT_ATTEMPTS=5
VITE_CONNECTION_TIMEOUT=30000
VITE_ENABLE_NOTIFICATIONS=true
VITE_ENABLE_REALTIME=true
VITE_ENABLE_DEBUG_LOGGING=true
VITE_DEFAULT_REFRESH_INTERVAL=10000
VITE_POLLING_INTERVAL=5000
VITE_MAX_EVENT_HISTORY=100
VITE_REQUIRE_API_KEY=false
EOF

echo -e "${GREEN}✅ Dashboard environment configured${NC}"

# Step 1: Start Azure Functions Backend
echo -e "${BLUE}🚀 Starting Azure Functions Backend${NC}"
echo "================================"

cd src/BigCommerce.Migration.Functions

# Check if func CLI is available
if ! command -v func &> /dev/null; then
    echo -e "${RED}❌ Azure Functions Core Tools not found!${NC}"
    echo "   Please install: npm install -g azure-functions-core-tools@4 --unsafe-perm true"
    exit 1
fi

echo -e "${YELLOW}⚙️  Starting Azure Functions host...${NC}"

# Start Functions in background
nohup func start --port 7071 > ../../func.log 2>&1 &
FUNC_PID=$!
echo $FUNC_PID > ../../func.pid

echo -e "${GREEN}✅ Azure Functions started (PID: $FUNC_PID)${NC}"
echo "   Log file: func.log"

# Wait for Functions to be ready
cd ../../
if wait_for_service "http://localhost:7071/api/signalr/info" "Azure Functions"; then
    echo -e "${GREEN}✅ Azure Functions SignalR hub is ready${NC}"
else
    echo -e "${RED}❌ Azure Functions failed to start properly${NC}"
    exit 1
fi

# Step 2: Start Dashboard Frontend
echo -e "${BLUE}🎨 Starting Dashboard Frontend${NC}"
echo "================================"

cd src/BigCommerce.Migration.Dashboard

# Check if npm is available
if ! command -v npm &> /dev/null; then
    echo -e "${RED}❌ npm not found! Please install Node.js${NC}"
    exit 1
fi

# Install dependencies if needed
if [ ! -d "node_modules" ]; then
    echo -e "${YELLOW}📦 Installing dashboard dependencies...${NC}"
    npm install
fi

echo -e "${YELLOW}⚙️  Starting Dashboard dev server...${NC}"

# Start Dashboard in background
nohup npm run dev > ../../dashboard.log 2>&1 &
DASHBOARD_PID=$!
echo $DASHBOARD_PID > ../../dashboard.pid

echo -e "${GREEN}✅ Dashboard started (PID: $DASHBOARD_PID)${NC}"
echo "   Log file: dashboard.log"

# Wait for Dashboard to be ready  
cd ../../
if wait_for_service "http://localhost:3000" "Dashboard"; then
    echo -e "${GREEN}✅ Dashboard is ready${NC}"
else
    echo -e "${RED}❌ Dashboard failed to start properly${NC}"
    exit 1
fi

# Display final status
echo ""
echo -e "${GREEN}🎉 REAL-TIME TESTING ENVIRONMENT READY!${NC}"
echo "============================================================="
echo -e "${BLUE}📍 Service Endpoints:${NC}"
echo "   🔧 Azure Functions:     http://localhost:7071"
echo "   📊 Dashboard:           http://localhost:3000"
echo "   🔌 SignalR Hub:         http://localhost:7071/api/negotiate"
echo "   📋 SignalR Info:        http://localhost:7071/api/signalr/info"
echo ""
echo -e "${BLUE}📋 Testing Instructions:${NC}"
echo "   1. Open http://localhost:3000 in your browser"
echo "   2. Check SignalR connection status in dashboard"
echo "   3. Start a migration to see real-time progress"
echo "   4. Watch for real-time updates in the progress bar"
echo ""
echo -e "${BLUE}📊 Monitoring:${NC}"
echo "   📄 Backend logs:  tail -f func.log"
echo "   📄 Frontend logs: tail -f dashboard.log"
echo "   🔍 Browser DevTools for SignalR connection status"
echo ""
echo -e "${BLUE}🛑 To stop services:${NC}"
echo "   ./scripts/stop-realtime-test.sh"
echo ""
echo -e "${YELLOW}🧪 Ready for E2E Real-Time Progress Testing!${NC}" 