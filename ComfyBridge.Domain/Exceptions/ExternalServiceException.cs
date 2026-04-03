namespace ComfyBridge.Domain.Exceptions;

public sealed class ExternalServiceException(string message) : Exception(message)
{
}