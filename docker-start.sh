#!/bin/bash

# BigCommerce Migration System - Docker Quick Start Script
# This script helps you quickly start the system in different configurations

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

print_header() {
    echo -e "${BLUE}================================${NC}"
    echo -e "${BLUE}$1${NC}"
    echo -e "${BLUE}================================${NC}"
}

# Function to check prerequisites
check_prerequisites() {
    print_header "Checking Prerequisites"
    
    # Check if Docker is installed
    if ! command -v docker &> /dev/null; then
        print_error "Docker is not installed. Please install Docker first."
        exit 1
    fi
    
    # Check if Docker Compose is installed
    if ! command -v docker-compose &> /dev/null; then
        print_error "Docker Compose is not installed. Please install Docker Compose first."
        exit 1
    fi
    
    # Check if Docker daemon is running
    if ! docker info &> /dev/null; then
        print_error "Docker daemon is not running. Please start Docker first."
        exit 1
    fi
    
    print_status "All prerequisites met!"
}

# Function to show usage
show_usage() {
    echo "BigCommerce Migration System - Docker Deployment"
    echo ""
    echo "Usage: $0 [COMMAND] [OPTIONS]"
    echo ""
    echo "Commands:"
    echo "  dev                 Start development environment (default)"
    echo "  test                Run all tests"
    echo "  test-unit           Run unit tests only"
    echo "  test-orchestration  Run orchestration tests only"
    echo "  test-integration    Run integration tests only"
    echo "  test-performance    Run performance tests only"
    echo "  prod                Start production environment"
    echo "  monitoring          Start with monitoring (Prometheus + Grafana)"
    echo "  stop                Stop all services"
    echo "  clean               Stop and remove all containers and volumes"
    echo "  logs                Show logs for all services"
    echo "  logs-functions      Show logs for Functions service only"
    echo "  logs-dashboard      Show logs for Dashboard service only"
    echo "  status              Show status of all services"
    echo "  help                Show this help message"
    echo ""
    echo "Examples:"
    echo "  $0 dev              # Start development environment"
    echo "  $0 test             # Run complete test suite"
    echo "  $0 prod             # Start production environment"
    echo "  $0 monitoring       # Start with monitoring enabled"
}

# Function to start development environment
start_dev() {
    print_header "Starting Development Environment"
    print_status "Starting all development services..."
    
    docker-compose up -d
    
    print_status "Services started successfully!"
    echo ""
    print_status "Access URLs:"
    echo "  Dashboard:  http://localhost:3000"
    echo "  API:        http://localhost:7071"
    echo "  Health:     http://localhost:7071/api/health"
    echo "  Azurite:    http://localhost:10000 (Blob), 10001 (Queue), 10002 (Table)"
    echo "  SignalR:    http://localhost:8080"
    echo ""
    print_status "To view logs: $0 logs"
    print_status "To stop services: $0 stop"
}

# Function to run tests
run_tests() {
    local test_type=$1
    
    case $test_type in
        "all"|"")
            print_header "Running All Tests"
            docker-compose --profile testing up --build --abort-on-container-exit
            ;;
        "unit")
            print_header "Running Unit Tests"
            docker-compose --profile unit-tests up --build unit-tests
            ;;
        "orchestration")
            print_header "Running Orchestration Tests"
            docker-compose --profile orchestration-tests up --build orchestration-tests
            ;;
        "integration")
            print_header "Running Integration Tests"
            docker-compose --profile integration-tests up --build integration-tests
            ;;
        "performance")
            print_header "Running Performance Tests"
            docker-compose --profile performance-tests up --build performance-tests
            ;;
        *)
            print_error "Unknown test type: $test_type"
            exit 1
            ;;
    esac
}

# Function to start production environment
start_prod() {
    print_header "Starting Production Environment"
    
    # Check if .env file exists
    if [ ! -f .env ]; then
        print_warning ".env file not found. Creating from docker-env-example.env..."
        cp docker-env-example.env .env
        print_warning "Please edit .env file with your actual configuration values."
        print_warning "Opening .env file for editing..."
        ${EDITOR:-nano} .env
    fi
    
    print_status "Starting production services..."
    docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d
    
    print_status "Production services started successfully!"
    echo ""
    print_status "Access URLs:"
    echo "  Dashboard:  http://localhost:80"
    echo "  API:        http://localhost:80/api"
    echo "  Health:     http://localhost:80/api/health"
}

# Function to start monitoring
start_monitoring() {
    print_header "Starting Environment with Monitoring"
    
    print_status "Starting services with Prometheus and Grafana..."
    docker-compose -f docker-compose.yml -f docker-compose.prod.yml --profile monitoring up -d
    
    print_status "Services with monitoring started successfully!"
    echo ""
    print_status "Access URLs:"
    echo "  Dashboard:   http://localhost:3000"
    echo "  API:         http://localhost:7071"
    echo "  Prometheus:  http://localhost:9090"
    echo "  Grafana:     http://localhost:3001 (admin/admin123)"
}

# Function to stop services
stop_services() {
    print_header "Stopping All Services"
    print_status "Stopping services..."
    
    docker-compose down
    docker-compose -f docker-compose.yml -f docker-compose.prod.yml down
    
    print_status "All services stopped successfully!"
}

# Function to clean up
clean_up() {
    print_header "Cleaning Up All Resources"
    print_warning "This will remove all containers, volumes, and networks!"
    
    read -p "Are you sure? (y/N): " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        print_status "Cleaning up..."
        
        docker-compose down -v --remove-orphans
        docker-compose -f docker-compose.yml -f docker-compose.prod.yml down -v --remove-orphans
        
        # Remove named volumes
        docker volume rm bigcommerce-azurite-data bigcommerce-functions-logs bigcommerce-test-results 2>/dev/null || true
        
        print_status "Cleanup completed!"
    else
        print_status "Cleanup cancelled."
    fi
}

# Function to show logs
show_logs() {
    local service=$1
    
    case $service in
        "functions")
            print_header "Functions Service Logs"
            docker-compose logs -f bigcommerce-functions
            ;;
        "dashboard")
            print_header "Dashboard Service Logs"
            docker-compose logs -f bigcommerce-dashboard
            ;;
        "")
            print_header "All Services Logs"
            docker-compose logs -f
            ;;
        *)
            print_error "Unknown service: $service"
            exit 1
            ;;
    esac
}

# Function to show status
show_status() {
    print_header "Service Status"
    
    print_status "Docker Compose Services:"
    docker-compose ps
    
    echo ""
    print_status "Resource Usage:"
    docker stats --no-stream --format "table {{.Container}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.NetIO}}"
    
    echo ""
    print_status "Health Checks:"
    if curl -f http://localhost:7071/api/health &>/dev/null; then
        echo "  ✅ Functions API: Healthy"
    else
        echo "  ❌ Functions API: Unhealthy or not running"
    fi
    
    if curl -f http://localhost:3000 &>/dev/null; then
        echo "  ✅ Dashboard: Healthy"
    else
        echo "  ❌ Dashboard: Unhealthy or not running"
    fi
}

# Main script logic
main() {
    case ${1:-dev} in
        "dev"|"development")
            check_prerequisites
            start_dev
            ;;
        "test")
            check_prerequisites
            run_tests "all"
            ;;
        "test-unit")
            check_prerequisites
            run_tests "unit"
            ;;
        "test-orchestration")
            check_prerequisites
            run_tests "orchestration"
            ;;
        "test-integration")
            check_prerequisites
            run_tests "integration"
            ;;
        "test-performance")
            check_prerequisites
            run_tests "performance"
            ;;
        "prod"|"production")
            check_prerequisites
            start_prod
            ;;
        "monitoring")
            check_prerequisites
            start_monitoring
            ;;
        "stop")
            stop_services
            ;;
        "clean")
            clean_up
            ;;
        "logs")
            show_logs ""
            ;;
        "logs-functions")
            show_logs "functions"
            ;;
        "logs-dashboard")
            show_logs "dashboard"
            ;;
        "status")
            show_status
            ;;
        "help"|"-h"|"--help")
            show_usage
            ;;
        *)
            print_error "Unknown command: $1"
            echo ""
            show_usage
            exit 1
            ;;
    esac
}

# Run main function with all arguments
main "$@" 