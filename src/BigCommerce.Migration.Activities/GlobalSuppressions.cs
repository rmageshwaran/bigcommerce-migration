using System.Diagnostics.CodeAnalysis;

// Orchestration services (activities and services) are not orchestrator functions and should use ConfigureAwait(false)
// These warnings are valid for orchestrator functions but not for activity functions and services
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "Activity functions and services should use ConfigureAwait(false) for better performance")]
[assembly: SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Number parsing in orchestration services uses invariant culture by default")]
[assembly: SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "String operations in orchestration services use invariant culture by default")]
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison", Justification = "String operations in orchestration services use default comparison")] 