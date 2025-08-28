// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// Suppress CA2007 for unit tests - ConfigureAwait is not needed in test projects
[assembly: SuppressMessage("Reliability", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "ConfigureAwait is not needed in unit tests")]

// Suppress CA1307 for unit tests - StringComparison is not critical for test assertions
[assembly: SuppressMessage("Globalization", "CA1307:Specify StringComparison for clarity", Justification = "StringComparison is not critical for test assertions")]
