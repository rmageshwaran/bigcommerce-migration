using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Performance", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Test code - synchronization context is not a concern in test scenarios")]
[assembly: SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Test infrastructure - catching all exceptions for measurement purposes")] 