using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Moq;

namespace BigCommerce.Migration.UnitTests.TestHelpers;

/// <summary>
/// Shared test helper for mocking Azure pageable responses
/// </summary>
public static class MockAsyncPageableHelper
{
    /// <summary>
    /// Creates a mock async pageable for Azure Page<T> responses
    /// </summary>
    /// <typeparam name="T">Type of items in the page</typeparam>
    /// <param name="items">Items to include in the page</param>
    /// <returns>Mock async pageable</returns>
    public static IAsyncEnumerable<Page<T>> CreateAsyncPageable<T>(IEnumerable<T> items)
    {
        return new MockAsyncPageable<T>(items);
    }

    /// <summary>
    /// Mock implementation of IAsyncEnumerable<Page<T>>
    /// </summary>
    /// <typeparam name="T">Type of items in the page</typeparam>
    private class MockAsyncPageable<T> : IAsyncEnumerable<Page<T>>
    {
        private readonly IEnumerable<T> _items;

        public MockAsyncPageable(IEnumerable<T> items)
        {
            _items = items;
        }

        public IAsyncEnumerator<Page<T>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new MockAsyncEnumerator<T>(_items);
        }
    }

    /// <summary>
    /// Mock implementation of IAsyncEnumerator<Page<T>>
    /// </summary>
    /// <typeparam name="T">Type of items in the page</typeparam>
    private class MockAsyncEnumerator<T> : IAsyncEnumerator<Page<T>>
    {
        private readonly IEnumerable<T> _items;
        private bool _hasReturned = false;

        public MockAsyncEnumerator(IEnumerable<T> items)
        {
            _items = items;
        }

        public Page<T> Current => Page<T>.FromValues(_items.ToArray(), null, Mock.Of<Response>());

        public ValueTask<bool> MoveNextAsync()
        {
            if (_hasReturned)
                return new ValueTask<bool>(false);
            
            _hasReturned = true;
            return new ValueTask<bool>(true);
        }

        public ValueTask DisposeAsync()
        {
            return new ValueTask();
        }
    }
} 