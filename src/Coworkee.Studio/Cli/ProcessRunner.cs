using System.Diagnostics;

namespace Coworkee.Studio.Cli;

/// <summary>Starts a process and hands every line of stdout (false) and stderr (true) to <paramref name="output"/>; throws Win32Exception when the program is missing.</summary>
public delegate Task<int> ProcessRunner(ProcessStartInfo start, Action<string, bool> output, CancellationToken cancellationToken);
