using System.Diagnostics.CodeAnalysis;

// Functions project contains both orchestrator functions and regular functions
// The ConfigureAwait warnings are valid for orchestrator functions but not for regular functions/services
// We suppress these globally since we have custom analyzers to detect violations in orchestrator functions specifically
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Regular functions and services should use ConfigureAwait(false), but orchestrator functions should not use ConfigureAwait(false) - this is enforced by custom analyzers")]
[assembly: SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Number parsing in functions uses invariant culture by default")]
[assembly: SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "String operations in functions use invariant culture by default")]
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison", Justification = "String operations in functions use default comparison")] 