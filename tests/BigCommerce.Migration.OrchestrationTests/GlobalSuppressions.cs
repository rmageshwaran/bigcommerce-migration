using System.Diagnostics.CodeAnalysis;

// Orchestration tests are not orchestrator functions and should use ConfigureAwait(false) for better performance
// Test projects can suppress these code analysis warnings as they are not production code
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Orchestration tests should use ConfigureAwait(false) for better performance")]
[assembly: SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Test projects use invariant culture by default")]
[assembly: SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "Test projects use invariant culture by default")]
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison", Justification = "Test projects use default string comparison")] 