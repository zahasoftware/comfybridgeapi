namespace ComfyBridge.Domain.Exceptions;

public sealed class ComfyBridgeException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
}