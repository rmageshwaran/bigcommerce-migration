using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.PerformanceTests.Infrastructure
{
    /// <summary>
    /// Tracks HTTP connection usage and efficiency metrics for performance testing
    /// </summary>
    public class ConnectionTracker
    {
        private readonly ConcurrentDictionary<string, ConnectionMetrics> _connectionMetrics = new();
        private readonly object _lock = new object();
        private int _totalConnections = 0;
        private int _totalRequests = 0;
        private int _reuseCount = 0;

        public int TotalConnections => _totalConnections;
        public int TotalRequests => _totalRequests;
        public int ReuseCount => _reuseCount;
        public double ReuseRatio => _totalRequests > 0 ? (double)_reuseCount / _totalRequests : 0.0;

        public void OnConnectionCreated(string connectionId)
        {
            lock (_lock)
            {
                _totalConnections++;
                _connectionMetrics.TryAdd(connectionId, new ConnectionMetrics 
                { 
                    ConnectionId = connectionId,
                    CreatedAt = DateTime.UtcNow,
                    RequestCount = 0
                });
            }
        }

        public void OnConnectionUsed(string connectionId)
        {
            lock (_lock)
            {
                _totalRequests++;
                
                if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
                {
                    metrics.RequestCount++;
                    metrics.LastUsedAt = DateTime.UtcNow;
                    
                    if (metrics.RequestCount > 1)
                    {
                        _reuseCount++;
                    }
                }
            }
        }

        public void OnConnectionClosed(string connectionId)
        {
            if (_connectionMetrics.TryGetValue(connectionId, out var metrics))
            {
                metrics.ClosedAt = DateTime.UtcNow;
            }
        }

        public ConnectionSummary GetSummary()
        {
            lock (_lock)
            {
                return new ConnectionSummary
                {
                    TotalConnections = _totalConnections,
                    TotalRequests = _totalRequests,
                    ReuseCount = _reuseCount,
                    ReuseRatio = ReuseRatio,
                    EfficiencyScore = CalculateEfficiencyScore()
                };
            }
        }

        private double CalculateEfficiencyScore()
        {
            if (_totalRequests == 0) return 0.0;
            
            // Efficiency = (Reuse Ratio * 0.7) + (Connection Utilization * 0.3)
            var connectionUtilization = _totalConnections > 0 ? (double)_totalRequests / _totalConnections : 0.0;
            var normalizedUtilization = Math.Min(connectionUtilization / 10.0, 1.0); // Normalize to 0-1 scale
            
            return (ReuseRatio * 0.7) + (normalizedUtilization * 0.3);
        }
    }

    /// <summary>
    /// Custom HttpMessageHandler that tracks connection usage for performance testing
    /// </summary>
    public class TrackedSocketsHttpHandler : DelegatingHandler
    {
        private readonly ConnectionTracker _tracker;
        private readonly ConcurrentDictionary<string, string> _requestToConnection = new();

        public TrackedSocketsHttpHandler(ConnectionTracker tracker) : base(CreateOptimizedSocketHandler())
        {
            _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }

        public TimeSpan PooledConnectionLifetime { get; set; } = TimeSpan.FromMinutes(15);
        public int MaxConnectionsPerServer { get; set; } = 10;
        public TimeSpan PooledConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(5);

        private static SocketsHttpHandler CreateOptimizedSocketHandler()
        {
            return new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                MaxConnectionsPerServer = 10,
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                EnableMultipleHttp2Connections = false, // Simplify tracking
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ResponseDrainTimeout = TimeSpan.FromSeconds(10)
            };
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestId = Guid.NewGuid().ToString("N")[..8];
            var connectionId = GetConnectionId(request);
            
            // Track connection usage - fix the logic to properly detect new connections
            var isNewConnection = !_requestToConnection.Values.Contains(connectionId);
            if (isNewConnection)
            {
                _tracker.OnConnectionCreated(connectionId);
            }
            
            _tracker.OnConnectionUsed(connectionId);
            _requestToConnection.TryAdd(requestId, connectionId);

            try
            {
                // Send the actual request using the delegated SocketsHttpHandler
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                return response;
            }
            catch (Exception)
            {
                // Even on exceptions, we track the connection usage
                throw;
            }
        }

        private string GetConnectionId(HttpRequestMessage request)
        {
            // Create a connection identifier based on scheme, host, and port
            var uri = request.RequestUri;
            if (uri == null) return "unknown";
            
            var port = uri.Port == -1 ? (uri.Scheme == "https" ? 443 : 80) : uri.Port;
            return $"{uri.Scheme}://{uri.Host}:{port}";
        }
    }

    /// <summary>
    /// Metrics for an individual connection
    /// </summary>
    public class ConnectionMetrics
    {
        public string ConnectionId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public int RequestCount { get; set; }
        
        public TimeSpan? Lifetime => ClosedAt.HasValue ? ClosedAt.Value - CreatedAt : DateTime.UtcNow - CreatedAt;
        public bool IsReused => RequestCount > 1;
    }

    /// <summary>
    /// Summary of connection tracking results
    /// </summary>
    public class ConnectionSummary
    {
        public int TotalConnections { get; set; }
        public int TotalRequests { get; set; }
        public int ReuseCount { get; set; }
        public double ReuseRatio { get; set; }
        public double EfficiencyScore { get; set; }
        
        public override string ToString()
        {
            return $"Connections: {TotalConnections}, Requests: {TotalRequests}, " +
                   $"Reuse: {ReuseRatio:P2}, Efficiency: {EfficiencyScore:P2}";
        }
    }
} 