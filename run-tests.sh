#!/bin/bash

echo "🧪 BigCommerce Migration - Test Runner"
echo "======================================"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    print_error "dotnet CLI is not installed. Please install .NET 8.0 SDK"
    exit 1
fi

print_status "Building solution..."
if ! dotnet build; then
    print_error "Build failed"
    exit 1
fi

print_success "Build completed successfully"

# Run unit tests
print_status "Running unit tests..."
echo "========================"

if dotnet test src/BigCommerce.Migration.Tests.Unit/BigCommerce.Migration.Tests.Unit.csproj --logger "console;verbosity=normal" --collect:"XPlat Code Coverage"; then
    print_success "Unit tests passed"
else
    print_error "Unit tests failed"
    exit 1
fi

echo ""

# Run integration tests
print_status "Running integration tests..."
echo "=============================="

if dotnet test src/BigCommerce.Migration.Tests.Integration/BigCommerce.Migration.Tests.Integration.csproj --logger "console;verbosity=normal" --collect:"XPlat Code Coverage"; then
    print_success "Integration tests passed"
else
    print_error "Integration tests failed"
    exit 1
fi

echo ""

# Run all tests with coverage
print_status "Running all tests with coverage report..."
echo "=========================================="

if dotnet test --collect:"XPlat Code Coverage" --results-directory:"./TestResults" --logger "console;verbosity=normal"; then
    print_success "All tests passed with coverage"
else
    print_error "Some tests failed"
    exit 1
fi

echo ""
print_success "🎉 All tests completed successfully!"
print_status "Coverage reports available in ./TestResults directory"
print_warning "Run this script before every deployment to prevent regressions"

# Optional: Generate HTML coverage report if reportgenerator is installed
if command -v reportgenerator &> /dev/null; then
    print_status "Generating HTML coverage report..."
    reportgenerator -reports:"./TestResults/*/coverage.cobertura.xml" -targetdir:"./TestResults/CoverageReport" -reporttypes:Html
    print_success "HTML coverage report generated in ./TestResults/CoverageReport"
else
    print_warning "Install reportgenerator for HTML coverage reports: dotnet tool install -g dotnet-reportgenerator-globaltool"
fi