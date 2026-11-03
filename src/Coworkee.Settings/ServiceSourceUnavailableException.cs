namespace Coworkee.Settings;

internal sealed class ServiceSourceUnavailableException(string source) : Exception($"The service source {source} could not be asked.");
