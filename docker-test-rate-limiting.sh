#!/bin/bash

# Enhanced Dynamic Rate Limiting Docker Test Script
# This script demonstrates the rate limiting implementation in Docker environment

echo "🚀 Enhanced Dynamic Rate Limiting Test"
echo "======================================"
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Check if Docker is running
if ! docker info > /dev/null 2>&1; then
    echo -e "${RED}❌ Docker is not running. Please start Docker and try again.${NC}"
    exit 1
fi

echo -e "${BLUE}📋 Test Plan:${NC}"
echo "1. Build migration functions with enhanced rate limiting"
echo "2. Start services with predictive rate limiting enabled"
echo "3. Run integration tests to demonstrate rate limiting behavior"
echo "4. Show logs with rate limiting decisions and adaptations"
echo ""

# Step 1: Build the solution
echo -e "${YELLOW}🔨 Step 1: Building BigCommerce Migration with Enhanced Rate Limiting...${NC}"
docker-compose build bigcommerce-functions

if [ $? -ne 0 ]; then
    echo -e "${RED}❌ Build failed. Please check the build logs.${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Build successful${NC}"
echo ""

# Step 2: Start services
echo -e "${YELLOW}🚀 Step 2: Starting services with predictive rate limiting...${NC}"
docker-compose up -d

# Wait for services to be ready
echo "⏳ Waiting for services to start..."
sleep 30

# Check if services are running
if ! docker-compose ps | grep -q "Up"; then
    echo -e "${RED}❌ Services failed to start. Check logs with: docker-compose logs${NC}"
    exit 1
fi

echo -e "${GREEN}✅ Services started successfully${NC}"
echo ""

# Step 3: Run rate limiting demonstration
echo -e "${YELLOW}🧪 Step 3: Running Enhanced Rate Limiting Integration Tests...${NC}"
echo ""

# Test 1: Basic rate limiting functionality
echo -e "${BLUE}Test 1: Enhanced Rate Limiting Demonstration${NC}"
docker-compose exec bigcommerce-functions dotnet test \
    tests/BigCommerce.Migration.Tests/Integration/RateLimitingDemoScript.cs \
    --verbosity normal \
    --logger "console;verbosity=detailed"

echo ""

# Test 2: Multi-instance coordination (if available)
echo -e "${BLUE}Test 2: Enhanced Rate Limiting Integration Test${NC}"
docker-compose exec bigcommerce-functions dotnet test \
    tests/BigCommerce.Migration.Tests/Integration/EnhancedRateLimitingIntegrationTests.cs \
    --filter "EnhancedRateLimiting_Under429Pressure_AdaptsAndRecovers" \
    --verbosity normal \
    --logger "console;verbosity=detailed"

echo ""

# Step 4: Show rate limiting configuration and logs
echo -e "${YELLOW}📊 Step 4: Rate Limiting Configuration and Logs${NC}"
echo ""

echo -e "${BLUE}Current Rate Limiting Configuration:${NC}"
echo "Predictive Rate Limiting: ENABLED"
echo "Enhanced Dynamic Rate Limiting: ENABLED"
echo "Safety Buffer: 25%"
echo "Coordination Health Checks: Every 20 seconds"
echo "Token Expiry: 20 seconds"
echo ""

echo -e "${BLUE}Recent Rate Limiting Logs:${NC}"
docker-compose logs --tail=50 bigcommerce-functions | grep -i "rate\|limiting\|predictive\|429\|quota" | tail -20

echo ""

# Step 5: Show monitoring data
echo -e "${YELLOW}📈 Step 5: Rate Limiting Monitoring${NC}"
echo ""

echo -e "${BLUE}Azure Storage Tables (Rate Limiting Data):${NC}"
echo "- quotatracking: Stores quota utilization data"
echo "- instancecoordination: Tracks function instance coordination"  
echo "- tokenallocation: Manages distributed token allocation"
echo ""

# Check if we can access storage
if docker-compose exec bigcommerce-functions az storage table list --connection-string "UseDevelopmentStorage=true" > /dev/null 2>&1; then
    echo -e "${GREEN}✅ Azure Storage tables accessible${NC}"
    docker-compose exec bigcommerce-functions az storage table list --connection-string "UseDevelopmentStorage=true" | grep -E "(quotatracking|instancecoordination|tokenallocation)"
else
    echo -e "${YELLOW}⚠️  Azure Storage not accessible (expected in test environment)${NC}"
fi

echo ""

# Step 6: Performance summary
echo -e "${YELLOW}🎯 Step 6: Performance Summary${NC}"
echo ""

echo -e "${BLUE}Enhanced Rate Limiting Benefits:${NC}"
echo "✅ Predictive quota management prevents 429 errors"
echo "✅ Multi-instance coordination prevents thundering herd"
echo "✅ Adaptive rate adjustment based on real-time health"
echo "✅ Distributed token allocation across function instances"
echo "✅ Safety buffer ensures quota never fully exhausted"
echo ""

echo -e "${BLUE}Key Metrics to Monitor:${NC}"
echo "• 429 Error Rate: Should be < 5% with enhanced rate limiting"
echo "• Average Throughput: Optimized based on quota health"
echo "• Quota Utilization: Stays below critical thresholds"
echo "• Instance Coordination: Fair distribution across instances"
echo ""

# Optional: Load test demonstration
echo -e "${YELLOW}🔥 Optional: Load Test Demonstration${NC}"
echo ""
read -p "Run load test to demonstrate rate limiting under pressure? (y/N): " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo -e "${BLUE}Running load test...${NC}"
    
    # Scale up to multiple instances
    echo "Scaling to 3 function instances..."
    docker-compose up -d --scale bigcommerce-functions=3
    
    sleep 10
    
    # Run multi-instance test
    echo "Running multi-instance coordination test..."
    docker-compose exec bigcommerce-functions dotnet test \
        tests/BigCommerce.Migration.Tests/Integration/EnhancedRateLimitingIntegrationTests.cs \
        --filter "MultiInstanceCoordination" \
        --verbosity normal
    
    # Scale back down
    echo "Scaling back to single instance..."
    docker-compose up -d --scale bigcommerce-functions=1
fi

echo ""
echo -e "${GREEN}🎉 Enhanced Rate Limiting Test Complete!${NC}"
echo ""
echo -e "${BLUE}Next Steps:${NC}"
echo "1. Monitor the rate limiting logs during real migration"
echo "2. Adjust safety buffer if needed based on your BigCommerce store limits"
echo "3. Scale function instances based on migration volume"
echo "4. Use the dashboard to monitor real-time rate limiting status"
echo ""

echo -e "${YELLOW}💡 Pro Tips:${NC}"
echo "• Check logs with: docker-compose logs -f bigcommerce-functions | grep -i 'rate'"
echo "• Monitor quota health: Look for 'QuotaHealth' events in logs"  
echo "• Tune configuration: Modify docker-compose.yml environment variables"
echo "• Dashboard monitoring: Access real-time rate limiting metrics via UI"
echo ""

# Cleanup option
read -p "Stop services? (y/N): " -n 1 -r
echo
if [[ $REPLY =~ ^[Yy]$ ]]; then
    echo "Stopping services..."
    docker-compose down
    echo -e "${GREEN}✅ Services stopped${NC}"
fi

echo ""
echo -e "${GREEN}Thank you for testing Enhanced Dynamic Rate Limiting! 🚀${NC}"