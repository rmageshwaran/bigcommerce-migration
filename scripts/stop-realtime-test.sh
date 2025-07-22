#!/bin/bash

# Stop Real-Time Migration Progress Testing Environment
# This script cleanly stops both the backend Azure Functions and frontend dashboard

echo "🛑 Stopping BigCommerce Migration Real-Time Testing Environment..."
echo "================================================================"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to stop a service by PID file
stop_service() {
    local service_name=$1
    local pid_file=$2
    
    if [ -f "$pid_file" ]; then
        local pid=$(cat "$pid_file")
        echo -e "${YELLOW}🛑 Stopping $service_name (PID: $pid)...${NC}"
        
        if kill -TERM "$pid" 2>/dev/null; then
            echo -e "${GREEN}✅ $service_name stopped successfully${NC}"
        else
            echo -e "${RED}⚠️  $service_name may have already stopped${NC}"
        fi
        
        rm -f "$pid_file"
    else
        echo -e "${YELLOW}ℹ️  No PID file found for $service_name${NC}"
    fi
}

# Stop Azure Functions
stop_service "Azure Functions" "func.pid"

# Stop Dashboard
stop_service "Dashboard" "dashboard.pid"

# Clean up any remaining processes on the ports
echo -e "${BLUE}🧹 Cleaning up remaining processes...${NC}"

# Kill any processes on port 7071 (Azure Functions)
if lsof -Pi :7071 -sTCP:LISTEN -t >/dev/null 2>&1; then
    echo -e "${YELLOW}🛑 Stopping remaining processes on port 7071...${NC}"
    lsof -Pi :7071 -sTCP:LISTEN -t | xargs -r kill -TERM
fi

# Kill any processes on port 5173 (Dashboard)
if lsof -Pi :5173 -sTCP:LISTEN -t >/dev/null 2>&1; then
    echo -e "${YELLOW}🛑 Stopping remaining processes on port 5173...${NC}"
    lsof -Pi :5173 -sTCP:LISTEN -t | xargs -r kill -TERM
fi

# Clean up temporary files
echo -e "${BLUE}🧹 Cleaning up temporary files...${NC}"
rm -f func.log dashboard.log
rm -f src/BigCommerce.Migration.Dashboard/.env.local

echo ""
echo -e "${GREEN}✅ Real-Time Testing Environment Stopped${NC}"
echo "================================================"
echo -e "${BLUE}📋 Services stopped:${NC}"
echo "   🔧 Azure Functions (port 7071)"
echo "   📊 Dashboard (port 5173)"
echo ""
echo -e "${GREEN}🎯 Environment is now clean and ready for restart${NC}" 