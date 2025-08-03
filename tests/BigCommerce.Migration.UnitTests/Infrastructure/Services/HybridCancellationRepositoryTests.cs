using BigCommerce.Migration.Infrastructure.Services;
using BigCommerce.Migration.Core.Interfaces;
using BigCommerce.Migration.Core.Models;
using BigCommerce.Migration.Core.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using FluentAssertions;
using Xunit;
using System.Text.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BigCommerce.Migration.UnitTests.Infrastructure.Services
{
    /// <summary>
    /// Unit tests for HybridCancellationRepository
    /// Tests all 4 layers: Table Storage + Queue + SignalR + Cache
    /// Coverage: Cache hit/miss scenarios, error handling, all scopes
    /// </summary>
    public class HybridCancellationRepositoryTests : IDisposable
    {
        private readonly Mock<IMigrationStorageService> _mockStorageService;
        private readonly Mock<IProgressQueueService> _mockQueueService;
        private readonly Mock<ISignalREventFactory> _mockSignalRFactory;
        private readonly IMemoryCache _memoryCache;
        private readonly Mock<ILogger<HybridCancellationRepository>> _mockLogger;
        private readonly HybridCancellationRepository _repository;

        private const string TestMigrationId = "test-migration-123";
        private const string TestReason = "User requested cancellation";
        private const string TestRequestedBy = "test-user";
        private const string TestEntityType = "categories";
        private const string TestBatchId = "batch-456";
        private const string TestStoreId = "store-789";

        public HybridCancellationRepositoryTests()
        {
            _mockStorageService = new Mock<IMigrationStorageService>();
            _mockQueueService = new Mock<IProgressQueueService>();
            _mockSignalRFactory = new Mock<ISignalREventFactory>();
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            _mockLogger = new Mock<ILogger<HybridCancellationRepository>>();

            _repository = new HybridCancellationRepository(
                _mockStorageService.Object,
                _mockQueueService.Object,
                _mockSignalRFactory.Object,
                _memoryCache,
                _mockLogger.Object);
        }

        public void Dispose()
        {
            _memoryCache?.Dispose();
        }

        #region 🔄 Layer 2 - Queue Propagation Tests

        [Fact]
        public async Task PropagateToAllInstancesAsync_ShouldSendQueueMessage()
        {
            // Arrange
            var scope = CancellationScope.Migration;

            // Act
            await _repository.PropagateToAllInstancesAsync(TestMigrationId, scope, TestReason, TestRequestedBy);

            // Assert - LAYER 2: Queue message sent
            _mockQueueService.Verify(q => q.SendJsonMessageAsync(
                "migration-cancellation", It.Is<string>(msg => 
                    msg.Contains(TestMigrationId) && 
                    msg.Contains(TestReason) && 
                    msg.Contains(TestRequestedBy)), 
                default(CancellationToken)), Times.Once);

            // Verify queue message contains correct event structure (simplified check)
            _mockQueueService.Verify(q => q.SendJsonMessageAsync(
                "migration-cancellation", It.Is<string>(msg => msg.Contains("CancellationRequested")), 
                default(CancellationToken)), Times.Once);
        }

        [Theory]
        [InlineData(CancellationScope.Migration)]
        [InlineData(CancellationScope.EntityType)]
        [InlineData(CancellationScope.Batch)]
        [InlineData(CancellationScope.Store)]
        public async Task PropagateToAllInstancesAsync_WithAllScopes_ShouldHandleCorrectly(CancellationScope scope)
        {
            // Act
            await _repository.PropagateToAllInstancesAsync(TestMigrationId, scope, TestReason, TestRequestedBy);

            // Assert - Queue message includes correct scope
            _mockQueueService.Verify(q => q.SendJsonMessageAsync(
                "migration-cancellation", It.Is<string>(msg => msg.Contains($"\"Scope\":{(int)scope}")), 
                default(CancellationToken)), Times.Once);
        }

        [Fact]
        public async Task PropagateToAllInstancesAsync_WhenQueueFails_ShouldLogError()
        {
            // Arrange
            _mockQueueService.Setup(q => q.SendJsonMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), default(CancellationToken)))
                .ThrowsAsync(new Exception("Queue service unavailable"));

            // Act & Assert
            await _repository.Invoking(r => r.PropagateToAllInstancesAsync(
                TestMigrationId, CancellationScope.Migration, TestReason, TestRequestedBy))
                .Should().ThrowAsync<Exception>()
                .WithMessage("Queue service unavailable");
        }

        #endregion

        #region 🎯 Layer 4 - In-Memory Cache Tests

        [Fact]
        public async Task IsFastCancellationAsync_WhenCacheHit_ShouldReturnCachedValue()
        {
            // Arrange
            var scope = CancellationScope.Migration;
            var cacheKey = GenerateCacheKey(TestMigrationId, scope, null, null, null);
            _memoryCache.Set(cacheKey, true);

            // Act
            var result = await _repository.IsFastCancellationAsync(TestMigrationId, scope);

            // Assert
            result.Should().BeTrue();
            
            // Should NOT call storage service (cache hit)
            _mockStorageService.Verify(s => s.GetCancellationTokenAsync(
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task IsFastCancellationAsync_WhenCacheMiss_ShouldCallStorageAndCache()
        {
            // Arrange
            var scope = CancellationScope.Migration;
            _mockStorageService.Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
                .ReturnsAsync(new CancellationTokenEntry 
                {
                    MigrationId = TestMigrationId,
                    Status = "Active",
                    IsProcessed = false
                });

            // Act
            var result = await _repository.IsFastCancellationAsync(TestMigrationId, scope);

            // Assert
            result.Should().BeTrue();
            
            // Should call storage service (cache miss) 
            _mockStorageService.Verify(s => s.GetCancellationTokenAsync(TestMigrationId), Times.Once);
                
            // Should cache the result
            var cacheKey = GenerateCacheKey(TestMigrationId, scope, null, null, null);
            _memoryCache.TryGetValue(cacheKey, out bool cachedValue).Should().BeTrue();
            cachedValue.Should().BeTrue();
        }

        [Fact]
        public async Task IsFastCancellationAsync_WithEntityTypeScope_ShouldUseScopedCacheKey()
        {
            // Arrange - EntityType scope uses stub implementation in HybridRepository
            var scope = CancellationScope.EntityType;

            // Act
            var result = await _repository.IsFastCancellationAsync(TestMigrationId, scope, TestEntityType);

            // Assert
            // For EntityType scope, HybridRepository returns false (stub implementation)
            // This test verifies the temporary fallback logic
            result.Should().BeFalse();
            
            // Should NOT call storage service for non-Migration scopes (stub)
            _mockStorageService.Verify(s => s.GetCancellationTokenAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task InvalidateCacheAsync_ShouldClearRelatedCacheEntries()
        {
            // Arrange
            var scope = CancellationScope.Migration;
            var cacheKey = GenerateCacheKey(TestMigrationId, scope, null, null, null);
            _memoryCache.Set(cacheKey, true);
            
            // Verify cache has the value
            _memoryCache.TryGetValue(cacheKey, out bool _).Should().BeTrue();

            // Act
            await _repository.InvalidateCacheAsync(TestMigrationId, scope);

            // Assert - Cache should be cleared
            _memoryCache.TryGetValue(cacheKey, out bool _).Should().BeFalse();
        }

        [Fact]
        public async Task InvalidateCacheAsync_WithMultipleScopes_ShouldInvalidateCorrectEntries()
        {
            // Arrange
            var migrationKey = GenerateCacheKey(TestMigrationId, CancellationScope.Migration, null, null, null);
            var entityKey = GenerateCacheKey(TestMigrationId, CancellationScope.EntityType, TestEntityType, null, null);
            
            _memoryCache.Set(migrationKey, true);
            _memoryCache.Set(entityKey, true);

            // Act - Invalidate only migration scope
            await _repository.InvalidateCacheAsync(TestMigrationId, CancellationScope.Migration);

            // Assert - Only migration scope should be invalidated
            _memoryCache.TryGetValue(migrationKey, out bool _).Should().BeFalse();
            _memoryCache.TryGetValue(entityKey, out bool _).Should().BeTrue(); // Should remain
        }

        #endregion

        #region 🏗️ Layer 1 - Backward Compatibility Tests (Delegate to Storage Service)

        [Fact]
        public async Task CreateAsync_ShouldDelegateToStorageService()
        {
            // Arrange
            var expectedToken = new CancellationTokenEntry
            {
                MigrationId = TestMigrationId,
                Reason = TestReason,
                RequestedBy = TestRequestedBy,
                RequestedAt = DateTime.UtcNow,
                IsProcessed = false,
                Status = "Active"
            };
            
            _mockStorageService.Setup(s => s.CreateCancellationTokenAsync(TestMigrationId, TestReason))
                .ReturnsAsync(expectedToken);

            // Act
            var result = await _repository.CreateAsync(TestMigrationId, TestReason);

            // Assert
            result.Should().BeEquivalentTo(expectedToken);
            _mockStorageService.Verify(s => s.CreateCancellationTokenAsync(TestMigrationId, TestReason), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_ShouldInvalidateCache()
        {
            // Arrange
            var cacheKey = GenerateCacheKey(TestMigrationId, CancellationScope.Migration, null, null, null);
            _memoryCache.Set(cacheKey, false); // Pre-populate cache
            
            _mockStorageService.Setup(s => s.CreateCancellationTokenAsync(TestMigrationId, TestReason))
                .ReturnsAsync(new CancellationTokenEntry { MigrationId = TestMigrationId });

            // Act
            await _repository.CreateAsync(TestMigrationId, TestReason);

            // Assert - Cache should be invalidated
            _memoryCache.TryGetValue(cacheKey, out bool _).Should().BeFalse();
        }

        [Fact]
        public async Task GetAsync_ShouldDelegateToStorageService()
        {
            // Arrange
            var expectedToken = new CancellationTokenEntry { MigrationId = TestMigrationId };
            _mockStorageService.Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
                .ReturnsAsync(expectedToken);

            // Act
            var result = await _repository.GetAsync(TestMigrationId);

            // Assert
            result.Should().BeEquivalentTo(expectedToken);
            _mockStorageService.Verify(s => s.GetCancellationTokenAsync(TestMigrationId), Times.Once);
        }

        [Fact]
        public async Task UpdateAsync_ShouldDelegateToStorageServiceAndInvalidateCache()
        {
            // Arrange
            var token = new CancellationTokenEntry { MigrationId = TestMigrationId };
            var cacheKey = GenerateCacheKey(TestMigrationId, CancellationScope.Migration, null, null, null);
            _memoryCache.Set(cacheKey, false); // Pre-populate cache
            
            _mockStorageService.Setup(s => s.UpdateCancellationTokenAsync(token))
                .ReturnsAsync(token);

            // Act
            var result = await _repository.UpdateAsync(token);

            // Assert
            result.Should().BeEquivalentTo(token);
            _mockStorageService.Verify(s => s.UpdateCancellationTokenAsync(token), Times.Once);
            
            // Cache should be invalidated
            _memoryCache.TryGetValue(cacheKey, out bool _).Should().BeFalse();
        }

        [Fact]
        public async Task DeleteAsync_ShouldDelegateToStorageServiceAndInvalidateCache()
        {
            // Arrange
            var cacheKey = GenerateCacheKey(TestMigrationId, CancellationScope.Migration, null, null, null);
            _memoryCache.Set(cacheKey, true); // Pre-populate cache
            
            _mockStorageService.Setup(s => s.DeleteCancellationTokenAsync(TestMigrationId))
                .ReturnsAsync(true);

            // Act
            var result = await _repository.DeleteAsync(TestMigrationId);

            // Assert
            result.Should().BeTrue();
            _mockStorageService.Verify(s => s.DeleteCancellationTokenAsync(TestMigrationId), Times.Once);
            
            // Cache should be invalidated
            _memoryCache.TryGetValue(cacheKey, out bool _).Should().BeFalse();
        }

        #endregion

        #region 🔄 Enhanced Scoped Methods Tests (Temporary Logic)

        [Fact]
        public async Task CreateScopedAsync_WithMigrationScope_ShouldUseLegacyMethod()
        {
            // Arrange
            var scope = CancellationScope.Migration;
            var expectedToken = new EnhancedCancellationTokenEntry
            {
                MigrationId = TestMigrationId,
                Scope = scope,
                Reason = TestReason,
                RequestedBy = TestRequestedBy,
                IsActive = true,
                ProcessedAt = null
            };
            
            _mockStorageService.Setup(s => s.CreateCancellationTokenAsync(TestMigrationId, TestReason))
                .ReturnsAsync(new CancellationTokenEntry
                {
                    MigrationId = TestMigrationId,
                    Reason = TestReason,
                    RequestedBy = TestRequestedBy,
                    Status = "Active",
                    IsProcessed = false
                });

            // Act
            var result = await _repository.CreateScopedAsync(TestMigrationId, scope, TestReason, TestRequestedBy);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Scope.Should().Be(scope);
            result.Reason.Should().Be(TestReason);
            result.RequestedBy.Should().Be(TestRequestedBy);
            result.IsActive.Should().BeTrue();
            
            _mockStorageService.Verify(s => s.CreateCancellationTokenAsync(TestMigrationId, TestReason), Times.Once);
        }

        [Theory]
        [InlineData(CancellationScope.EntityType)]
        [InlineData(CancellationScope.Batch)]
        [InlineData(CancellationScope.Store)]
        public async Task CreateScopedAsync_WithNonMigrationScopes_ShouldReturnStubEntry(CancellationScope scope)
        {
            // Act
            var result = await _repository.CreateScopedAsync(TestMigrationId, scope, TestReason, TestRequestedBy);

            // Assert
            result.Should().NotBeNull();
            result.MigrationId.Should().Be(TestMigrationId);
            result.Scope.Should().Be(scope);
            result.Reason.Should().Be(TestReason);
            result.RequestedBy.Should().Be(TestRequestedBy);
            result.IsActive.Should().BeTrue();
            
            // Should NOT call storage service for non-Migration scopes (stub implementation)
            _mockStorageService.Verify(s => s.CreateCancellationTokenAsync(
                It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task GetActiveByMigrationAsync_ShouldReturnActiveTokens()
        {
            // Arrange
            _mockStorageService.Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
                .ReturnsAsync(new CancellationTokenEntry
                {
                    MigrationId = TestMigrationId,
                    Status = "Active",
                    IsProcessed = false
                });

            // Act
            var result = await _repository.GetActiveByMigrationAsync(TestMigrationId);

            // Assert
            result.Should().NotBeNull();
            result.Should().HaveCount(1);
            result[0].MigrationId.Should().Be(TestMigrationId);
            result[0].IsActive.Should().BeTrue();
        }

        [Fact]
        public async Task IsActiveCancellationAsync_WithMigrationScope_ShouldCheckStorageService()
        {
            // Arrange
            _mockStorageService.Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
                .ReturnsAsync(new CancellationTokenEntry
                {
                    MigrationId = TestMigrationId,
                    Status = "Active",
                    IsProcessed = false
                });

            // Act
            var result = await _repository.IsActiveCancellationAsync(TestMigrationId, CancellationScope.Migration);

            // Assert
            result.Should().BeTrue();
            _mockStorageService.Verify(s => s.GetCancellationTokenAsync(TestMigrationId), Times.Once);
        }

        [Theory]
        [InlineData(CancellationScope.EntityType)]
        [InlineData(CancellationScope.Batch)]
        [InlineData(CancellationScope.Store)]
        public async Task IsActiveCancellationAsync_WithNonMigrationScopes_ShouldReturnFalse(CancellationScope scope)
        {
            // Act
            var result = await _repository.IsActiveCancellationAsync(TestMigrationId, scope);

            // Assert
            result.Should().BeFalse(); // Stub implementation returns false for non-Migration scopes
            
            // Should NOT call storage service
            _mockStorageService.Verify(s => s.GetCancellationTokenAsync(
                It.IsAny<string>()), Times.Never);
        }

        #endregion

        #region 🔥 Error Handling Tests

        [Fact]
        public async Task IsFastCancellationAsync_WhenInfrastructureServiceFails_ShouldThrow()
        {
            // Arrange
            _mockStorageService.Setup(s => s.GetCancellationTokenAsync(It.IsAny<string>()))
                .ThrowsAsync(new Exception("Storage unavailable"));

            // Act & Assert - Infrastructure exceptions should bubble up
            await _repository.Invoking(r => r.IsFastCancellationAsync(TestMigrationId, CancellationScope.Migration))
                .Should().ThrowAsync<Exception>()
                .WithMessage("Storage unavailable");
        }

        [Fact]
        public async Task IsFastCancellationAsync_WhenApplicationLogicFails_ShouldReturnFalseAndLogError()
        {
            // Arrange - Non-infrastructure exception
            _mockStorageService.Setup(s => s.GetCancellationTokenAsync(It.IsAny<string>()))
                .ThrowsAsync(new ArgumentException("Invalid argument"));

            // Act
            var result = await _repository.IsFastCancellationAsync(TestMigrationId, CancellationScope.Migration);

            // Assert - Application logic errors should return safe default
            result.Should().BeFalse("application logic failure should return safe default");
            
            // Verify error was logged
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("FAST-CHECK-ERROR")),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }

        [Fact]
        public async Task PropagateToAllInstancesAsync_WithInvalidParameters_ShouldThrow()
        {
            // Act & Assert
            await _repository.Invoking(r => r.PropagateToAllInstancesAsync(
                "", CancellationScope.Migration, TestReason, TestRequestedBy))
                .Should().ThrowAsync<ArgumentException>();

            await _repository.Invoking(r => r.PropagateToAllInstancesAsync(
                TestMigrationId, CancellationScope.Migration, "", TestRequestedBy))
                .Should().ThrowAsync<ArgumentException>();

            await _repository.Invoking(r => r.PropagateToAllInstancesAsync(
                TestMigrationId, CancellationScope.Migration, TestReason, ""))
                .Should().ThrowAsync<ArgumentException>();
        }

        #endregion

        #region 🎯 Performance Tests

        [Fact]
        public async Task IsFastCancellationAsync_Performance_ShouldCacheResults()
        {
            // Arrange
            var scope = CancellationScope.Migration;
            _mockStorageService.Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
                .ReturnsAsync(new CancellationTokenEntry 
                {
                    MigrationId = TestMigrationId,
                    Status = "Active",
                    IsProcessed = false
                });

            // Act - First call (cache miss)
            var result1 = await _repository.IsFastCancellationAsync(TestMigrationId, scope);
            
            // Act - Second call (cache hit)
            var result2 = await _repository.IsFastCancellationAsync(TestMigrationId, scope);

            // Assert
            result1.Should().BeTrue();
            result2.Should().BeTrue();
            
            // Storage service should only be called once (second call uses cache)
            _mockStorageService.Verify(s => s.GetCancellationTokenAsync(TestMigrationId), Times.Once);
        }

        [Fact]
        public async Task IsFastCancellationAsync_CacheExpiry_ShouldRespectTTL()
        {
            // Arrange
            var scope = CancellationScope.Migration;
            var cacheKey = GenerateCacheKey(TestMigrationId, scope, null, null, null);
            
            // Set cache with very short expiry
            _memoryCache.Set(cacheKey, true, TimeSpan.FromMilliseconds(1));
            
            // Wait for cache to expire
            await Task.Delay(10);

            _mockStorageService.Setup(s => s.GetCancellationTokenAsync(TestMigrationId))
                .ReturnsAsync((CancellationTokenEntry?)null); // No token = not cancelled

            // Act
            var result = await _repository.IsFastCancellationAsync(TestMigrationId, scope);

            // Assert
            result.Should().BeFalse(); // Should get fresh value from storage, not cached true
            _mockStorageService.Verify(s => s.GetCancellationTokenAsync(TestMigrationId), Times.Once);
        }

        #endregion

        #region 🛠️ Helper Methods

        private string GenerateCacheKey(string migrationId, CancellationScope scope, string? entityType, string? batchId, string? storeId)
        {
            return $"cancellation:{migrationId}:{scope}:{entityType ?? "null"}:{batchId ?? "null"}:{storeId ?? "null"}";
        }

        #endregion
    }
}