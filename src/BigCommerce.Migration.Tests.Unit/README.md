# BigCommerce Migration - Unit Tests

## Overview

This project contains unit tests for the BigCommerce Migration system, specifically targeting the progressive discovery functionality that has been prone to regressions.

## Test Coverage

### 🎯 Progressive Discovery Tests (`EntityMigrationOrchestratorTests.cs`)

**Critical Tests for Preventing Regressions:**

1. **JsonElement Casting Tests**
   - `GetBooleanValue_WithTrue_ReturnsTrue()` - Ensures JsonElement true values are handled
   - `GetBooleanValue_WithFalse_ReturnsFalse()` - Ensures JsonElement false values are handled
   - `GetBooleanValue_WithTrueString_ReturnsTrue()` - Handles string-based boolean values
   - `GetBooleanValue_WithNull_ReturnsFalse()` - Graceful null handling
   - Parameterized tests for various string formats

2. **Chunk Size Calculation Tests**
   - `ProgressiveDiscovery_ChunkSizeCalculation_ShouldUseProductCount()` - **CRITICAL** - Prevents ChunkSize=0 regression
   - `StandardDiscovery_ChunkSizeCalculation_ShouldUseTotalCount()` - Regular discovery validation
   - `SmallDataset_ChunkSizeCalculation_ShouldDisableChunking()` - Performance optimization tests
   - `ProgressiveDiscovery_WithZeroProducts_ShouldNotProcess()` - Edge case handling

## Key Test Scenarios

### 🚨 Regression Prevention

These tests specifically prevent the issues we've encountered:

1. **JsonElement Casting Error** (Fixed in previous iterations)
   ```csharp
   // Tests ensure this works instead of throwing:
   var isProgressiveDiscovery = metadata["ProgressiveDiscovery"];
   ```

2. **ChunkSize=0 Problem** (Current fix)
   ```csharp
   // Tests ensure progressive discovery gets proper chunk size:
   // Input: TotalCount=0, ProgressiveDiscovery=true, TotalProducts=4791
   // Expected: ChunkSize=250, TotalEntities=4791
   ```

3. **Variant Dependency Issues**
   - Tests validate that product-components process before variants
   - Ensures option mappings are created for variant processing

## Running Tests

### Command Line
```bash
# Run only unit tests
dotnet test src/BigCommerce.Migration.Tests.Unit/

# Run with coverage
dotnet test src/BigCommerce.Migration.Tests.Unit/ --collect:"XPlat Code Coverage"

# Run specific test class
dotnet test src/BigCommerce.Migration.Tests.Unit/ --filter "EntityMigrationOrchestratorTests"

# Run specific test method
dotnet test src/BigCommerce.Migration.Tests.Unit/ --filter "ProgressiveDiscovery_ChunkSizeCalculation_ShouldUseProductCount"
```

### Using the Test Runner Script
```bash
# Run all tests (unit + integration)
./run-tests.sh
```

## Test Structure

### Helper Methods

- `InvokeGetBooleanValue()` - Simulates the private GetBooleanValue method
- `CalculateChunkingParameters()` - Simulates the chunking calculation logic

### Test Data

Tests use realistic data from production scenarios:
- 4791 products (actual production count)
- 250 chunk size (production configuration)
- Progressive discovery metadata structure

## Adding New Tests

When adding new functionality, create tests for:

1. **Happy Path** - Normal operation
2. **Edge Cases** - Zero counts, null values, empty collections
3. **Error Conditions** - Invalid inputs, missing metadata
4. **Regression Prevention** - Specific scenarios that have failed before

### Example Test Pattern

```csharp
[Fact]
public void NewFeature_WithSpecificCondition_ShouldBehaveCorrectly()
{
    // Arrange - Setup test data
    var input = CreateTestInput();
    
    // Act - Execute the functionality
    var result = ExecuteFeature(input);
    
    // Assert - Verify expected behavior
    result.Should().NotBeNull();
    result.SomeProperty.Should().Be(expectedValue);
}
```

## Continuous Integration

These tests should be run:
- ✅ Before every commit
- ✅ In CI/CD pipeline
- ✅ Before deployments
- ✅ After any orchestrator changes

## Troubleshooting

### Common Issues

1. **Missing Dependencies** - Ensure all project references are correct
2. **Test Failures** - Check if recent changes broke existing functionality
3. **Coverage Issues** - Add tests for new code paths

### Debug Mode

Run tests with verbose logging:
```bash
dotnet test --logger "console;verbosity=detailed"
```