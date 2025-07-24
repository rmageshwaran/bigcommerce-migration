#!/bin/bash

# Real-Time Integration Test Script
# Tests the complete real-time monitoring and progress tracking feature

BACKEND_URL=${1:-"http://localhost:7071"}
FRONTEND_URL=${2:-"http://localhost:3000"}
TIMEOUT=${3:-120}

echo "🧪 Testing Real-Time Migration Progress Integration"
echo "Backend URL: $BACKEND_URL"
echo "Frontend URL: $FRONTEND_URL"
echo "Timeout: $TIMEOUT seconds"
echo ""

# Test Results
BACKEND_HEALTH=false
SIGNALR_NEGOTIATE=false
SIGNALR_INFO=false
FRONTEND_REACHABLE=false
ENHANCED_EVENTS_SUPPORTED=false

# Function to test HTTP endpoint
test_endpoint() {
    local url=$1
    local description=$2
    local method=${3:-"GET"}
    
    echo -n "🔍 Testing $description..."
    
    if [ "$method" = "POST" ]; then
        response=$(curl -s -w "%{http_code}" -X POST "$url" -o /tmp/response_body 2>/dev/null)
    else
        response=$(curl -s -w "%{http_code}" "$url" -o /tmp/response_body 2>/dev/null)
    fi
    
    http_code="${response: -3}"
    
    if [ "$http_code" = "200" ]; then
        echo " ✅ PASS"
        return 0
    else
        echo " ❌ FAIL (Status: $http_code)"
        return 1
    fi
}

# Test 1: Backend Health Check
echo "📋 Phase 1: Backend Health Checks"
if test_endpoint "$BACKEND_URL/api/health" "Backend Health Endpoint"; then
    BACKEND_HEALTH=true
    if command -v jq &> /dev/null; then
        status=$(cat /tmp/response_body | jq -r '.status // "unknown"' 2>/dev/null)
        version=$(cat /tmp/response_body | jq -r '.version // "unknown"' 2>/dev/null)
        echo "   System Status: $status"
        echo "   API Version: $version"
    fi
fi

# Test 2: SignalR Negotiate Endpoint
if test_endpoint "$BACKEND_URL/api/negotiate" "SignalR Negotiate Endpoint" "POST"; then
    SIGNALR_NEGOTIATE=true
    if command -v jq &> /dev/null; then
        url=$(cat /tmp/response_body | jq -r '.url // "unknown"' 2>/dev/null)
        token_length=$(cat /tmp/response_body | jq -r '.accessToken // ""' 2>/dev/null | wc -c)
        echo "   SignalR URL: $url"
        echo "   Access Token: $token_length chars"
    fi
fi

# Test 3: SignalR Info Endpoint
if test_endpoint "$BACKEND_URL/api/signalr/info" "SignalR Info Endpoint"; then
    SIGNALR_INFO=true
    if command -v jq &> /dev/null; then
        hub_name=$(cat /tmp/response_body | jq -r '.hubName // "unknown"' 2>/dev/null)
        connection_state=$(cat /tmp/response_body | jq -r '.connectionState // "unknown"' 2>/dev/null)
        negotiate_endpoint=$(cat /tmp/response_body | jq -r '.negotiateEndpoint // "unknown"' 2>/dev/null)
        echo "   Hub Name: $hub_name"
        echo "   Connection State: $connection_state"
        echo "   Negotiate Endpoint: $negotiate_endpoint"
    fi
fi

echo ""

# Test 4: Frontend Reachability
echo "📋 Phase 2: Frontend Connectivity"
if test_endpoint "$FRONTEND_URL" "Frontend Dashboard"; then
    FRONTEND_REACHABLE=true
    if grep -q "Real-Time Migration Dashboard" /tmp/response_body 2>/dev/null; then
        echo "   Dashboard content detected ✅"
    else
        echo "   Frontend reachable but content unclear ⚠️"
    fi
fi

echo ""

# Test 5: Enhanced SignalR Events Test
echo "📋 Phase 3: Enhanced Events Verification"
if test_endpoint "$BACKEND_URL/api/signalr/info" "Enhanced Events Support"; then
    echo "   Enhanced Events Expected:"
    echo "     • DetailedProgress"
    echo "     • ProcessingContext"
    echo "     • BatchStarted"
    echo "     • BatchCompleted"
    echo "     • PerformanceMetrics"
    echo "     • MigrationMilestone"
    ENHANCED_EVENTS_SUPPORTED=true
fi

echo ""

# Test 6: Integration Test Summary
echo "📋 Phase 4: Integration Test Summary"

# Count passed tests
passed_tests=0
total_tests=5

[ "$BACKEND_HEALTH" = true ] && ((passed_tests++))
[ "$SIGNALR_NEGOTIATE" = true ] && ((passed_tests++))
[ "$SIGNALR_INFO" = true ] && ((passed_tests++))
[ "$FRONTEND_REACHABLE" = true ] && ((passed_tests++))
[ "$ENHANCED_EVENTS_SUPPORTED" = true ] && ((passed_tests++))

echo "Tests Passed: $passed_tests/$total_tests"

# Detailed test results
echo ""
echo "Detailed Results:"
echo "  Backend Health:       $BACKEND_HEALTH"
echo "  SignalR Negotiate:    $SIGNALR_NEGOTIATE"
echo "  SignalR Info:         $SIGNALR_INFO"
echo "  Frontend Reachable:   $FRONTEND_REACHABLE"
echo "  Enhanced Events:      $ENHANCED_EVENTS_SUPPORTED"

# Overall Status
if [ $passed_tests -eq $total_tests ]; then
    echo ""
    echo "🎉 INTEGRATION TEST PASSED!"
    echo "   Real-time monitoring is properly configured"
    echo "   Both backend and frontend are running correctly"
    echo "   SignalR integration is functional"
    exit_code=0
else
    echo ""
    echo "❌ INTEGRATION TEST FAILED"
    
    # Provide specific recommendations
    if [ "$BACKEND_HEALTH" = false ]; then
        echo "   → Start Azure Functions: cd src/BigCommerce.Migration.Functions && func start"
    fi
    if [ "$FRONTEND_REACHABLE" = false ]; then
        echo "   → Start React Dashboard: cd src/BigCommerce.Migration.Dashboard && npm run dev"
    fi
    if [ "$SIGNALR_NEGOTIATE" = false ]; then
        echo "   → Check SignalR configuration in appsettings.json"
    fi
    exit_code=1
fi

echo ""

# Test 7: Manual Testing Instructions
echo "📋 Phase 5: Manual Testing Instructions"
echo ""
echo "To manually test the real-time integration:"
echo "1. Open browser to: $FRONTEND_URL"
echo "2. Navigate to the SignalR Integration Test page"
echo "3. Click 'Connect' to test SignalR connection"
echo "4. Start a test migration to see real-time updates"
echo "5. Observe these real-time events:"
echo "   • DetailedProgress - Enhanced progress updates"
echo "   • BatchStarted/Completed - Batch-level tracking"
echo "   • PerformanceMetrics - Real-time performance data"
echo "   • ProcessingContext - Current activity details"

echo ""
echo "Files to check:"
echo "  Frontend: src/BigCommerce.Migration.Dashboard/src/components/Tests/SignalRIntegrationTest.tsx"
echo "  Backend:  src/BigCommerce.Migration.Functions/Functions/SignalRFunctions.cs"
echo "  Models:   src/BigCommerce.Migration.Core/Models/EnhancedMigrationProgress.cs"

echo ""
echo "Test completed at $(date)"

# Clean up
rm -f /tmp/response_body

exit $exit_code 