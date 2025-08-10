# 🚀 Docker Performance Optimization Guide

## Overview
This guide documents the complete Docker configuration updates made to support the new high-performance parallel processing architecture for BigCommerce migrations.

## 🎯 Performance Goals
- **Target**: Reduce brand migration time from 132 seconds back to ~30 seconds
- **Approach**: Configuration-driven parallel processing with Docker environment variables
- **Key Improvement**: Replace hardcoded values with configurable performance settings

## 📊 Configuration Changes Summary

### Development Environment (`docker-compose.yml`)

#### 🔥 **Brand Migration Optimization (Target: ~30 seconds)**
```yaml
# OLD - Sequential processing (slow)
- AppSettings__BatchSize=10
- AppSettings__MaxConcurrency=3

# NEW - Parallel processing (fast)
- ParallelProcessing__subBatchConfigurations__brands__subBatchSize=25        # 5x larger batches
- ParallelProcessing__subBatchConfigurations__brands__maxConcurrency=20      # 6.7x more concurrent
- ParallelProcessing__subBatchConfigurations__brands__processSubBatchesSequentially=false  # Parallel mode
```

#### ⚡ **BigCommerce API Rate Limits**
```yaml
# OLD - Conservative limits
- BigCommerce__RateLimitRequestsPerSecond=14

# NEW - Higher throughput
- BigCommerce__RateLimitRequestsPerSecond=25    # 79% increase
- BigCommerce__DefaultRateLimit=25
- BigCommerce__ConcurrentRequests=20           # 20 concurrent API calls
```

#### 🏥 **Health Check Optimization**
```yaml
# OLD - Slow monitoring
interval: 30s
retries: 5

# NEW - Fast monitoring
interval: 15s      # 2x faster monitoring
retries: 3         # Faster recovery
start_period: 30s  # Extended startup time
```

### Production Environment (`docker-compose.prod.yml`)

#### 🏭 **Production-Optimized Settings**
```yaml
# Conservative but fast production settings
- ParallelProcessing__subBatchConfigurations__brands__subBatchSize=20        # Slightly conservative
- ParallelProcessing__subBatchConfigurations__brands__maxConcurrency=15      # Production-safe concurrency
- ParallelProcessing__subBatchConfigurations__brands__subBatchDelayMs=50     # Small delay for stability
```

#### 💪 **Resource Scaling**
```yaml
# OLD - Insufficient for parallel processing
resources:
  limits:
    cpus: '2.0'
    memory: 4G

# NEW - Supports 15-20 concurrent requests
resources:
  limits:
    cpus: '4.0'      # 2x CPU for parallel processing
    memory: 8G       # 2x memory for concurrent operations
  reservations:
    cpus: '1.0'      # Higher baseline
    memory: 2G       # Higher baseline
```

## 🔧 Architecture Benefits

### 1. **Configuration-Driven Performance**
- ✅ No more hardcoded chunk sizes
- ✅ Entity-specific optimization (brands vs products vs variants)
- ✅ Environment-specific tuning (dev vs prod)
- ✅ Runtime configuration changes without code deployment

### 2. **Parallel Processing Pipeline**
```
OLD FLOW (Sequential):
Chunk 0: [Brand1, Brand2, Brand3, Brand4, Brand5] → Process sequentially → 25 seconds
Chunk 1: [Brand6, Brand7, Brand8, Brand9, Brand10] → Process sequentially → 25 seconds
Total: 50+ seconds per chunk × 4 chunks = 200+ seconds

NEW FLOW (Parallel):
Chunk 0: [25 brands] → Process 20 concurrent → 6-8 seconds
Chunk 1: [25 brands] → Process 20 concurrent → 6-8 seconds  
Chunk 2: [25 brands] → Process 20 concurrent → 6-8 seconds
Chunk 3: [25 brands] → Process 20 concurrent → 6-8 seconds
Total: ~30 seconds (chunks run in parallel)
```

### 3. **Resource Optimization**
- **CPU**: Increased from 2.0 to 4.0 cores for parallel processing
- **Memory**: Increased from 4G to 8G for concurrent operations
- **Network**: 20 concurrent API connections vs previous 3

## 🚨 Critical Environment Variables

### Required for All Environments
```yaml
# Core parallel processing
- ParallelProcessing__maxConcurrentBatches=4
- ParallelProcessing__chunkingThreshold=100
- ParallelProcessing__defaultChunkSize=50

# BigCommerce API optimization
- BigCommerce__DefaultRateLimit=25
- BigCommerce__ConcurrentRequests=20
```

### Brand-Specific (Critical for Performance)
```yaml
# Chunking configuration
- ParallelProcessing__subBatchConfigurations__brands__chunkSize=50
- ParallelProcessing__subBatchConfigurations__brands__fetchBatchSize=50

# Parallel processing
- ParallelProcessing__subBatchConfigurations__brands__subBatchSize=25
- ParallelProcessing__subBatchConfigurations__brands__maxConcurrency=20
- ParallelProcessing__subBatchConfigurations__brands__processSubBatchesSequentially=false
```

## 📈 Expected Performance Impact

### Brand Migration (193 brands)
| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| **Total Time** | 132 seconds | ~30 seconds | **77% faster** |
| **Concurrent Requests** | 3 | 20 | **567% increase** |
| **Sub-batch Size** | 5 | 25 | **400% increase** |
| **Processing Mode** | Sequential | Parallel | **Architectural improvement** |
| **API Rate Limit** | 14 req/sec | 25 req/sec | **79% increase** |

### Resource Utilization
| Resource | Before | After | Scaling |
|----------|--------|-------|---------|
| **CPU Cores** | 2.0 | 4.0 | **2x** |
| **Memory** | 4GB | 8GB | **2x** |
| **Network Connections** | 3 concurrent | 20 concurrent | **6.7x** |

## 🛠️ Deployment Commands

### Development Deployment
```bash
# Build and deploy with new configuration
docker-compose down
docker-compose build --no-cache bigcommerce-functions
docker-compose up -d

# Verify health
docker logs bigcommerce-functions --tail 50
curl http://localhost:7071/api/health
```

### Production Deployment
```bash
# Deploy with production optimizations
docker-compose -f docker-compose.yml -f docker-compose.prod.yml down
docker-compose -f docker-compose.yml -f docker-compose.prod.yml build --no-cache
docker-compose -f docker-compose.yml -f docker-compose.prod.yml up -d

# Monitor performance
docker stats bigcommerce-functions-prod
curl http://localhost/api/health
```

## 🔍 Monitoring & Validation

### Key Metrics to Monitor
1. **Migration Time**: Should drop from 132s to ~30s for brands
2. **CPU Usage**: Should utilize more cores efficiently
3. **Memory Usage**: Should remain stable under 8GB
4. **API Rate Limits**: Should maintain <90% usage
5. **Concurrent Connections**: Should see up to 20 active connections

### Health Check Validation
```bash
# Check container health
docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

# Monitor resource usage
docker stats --format "table {{.Container}}\t{{.CPUPerc}}\t{{.MemUsage}}\t{{.NetIO}}"

# Check application logs for performance metrics
docker logs bigcommerce-functions | grep -E "(migration|concurrent|performance)"
```

## ⚠️ Troubleshooting

### Common Issues

1. **High Memory Usage**
   - Reduce `maxConcurrency` from 20 to 15
   - Increase `subBatchDelayMs` from 0 to 50

2. **API Rate Limit Errors**
   - Reduce `BigCommerce__ConcurrentRequests` from 20 to 15
   - Increase `subBatchDelayMs` to add throttling

3. **Container Startup Issues**
   - Increase `start_period` in health check
   - Check logs for configuration validation errors

4. **Network Connection Errors**
   - Verify `BigCommerce__ConcurrentRequests` matches container limits
   - Check Docker network configuration

## 📝 Configuration Validation

Before deployment, verify these critical settings:

```yaml
# ✅ Parallel processing enabled
processSubBatchesSequentially: false

# ✅ High concurrency configured  
maxConcurrency: 20 (dev) / 15 (prod)

# ✅ Large sub-batches
subBatchSize: 25 (dev) / 20 (prod)

# ✅ Sufficient resources
cpus: '4.0'
memory: 8G

# ✅ API limits increased
BigCommerce__ConcurrentRequests: 20
```

## 🎉 Success Criteria

The optimization is successful when:
- ✅ Brand migration time ≤ 35 seconds (target: ~30s)
- ✅ CPU utilization: 50-80% of available cores
- ✅ Memory usage: <6GB stable
- ✅ API rate limit usage: <90%
- ✅ Zero timeout or connection errors
- ✅ All 193 brands migrate successfully

---

*Generated for BigCommerce Migration System Docker Optimization*
*Target Performance: 30-second brand migrations with 20 concurrent API requests*