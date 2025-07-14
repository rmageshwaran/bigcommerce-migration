using System.Diagnostics.CodeAnalysis;

// Infrastructure services are not orchestrator functions and should use ConfigureAwait(false)
// These warnings are valid for orchestrator functions but not for regular service classes
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Infrastructure services should use ConfigureAwait(false) for better performance")]
[assembly: SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Integer parsing in infrastructure services uses invariant culture by default")]
[assembly: SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "String operations in infrastructure services use invariant culture by default")]
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison", Justification = "String operations in infrastructure services use default comparison")] 