namespace ComfyBridge.Domain.Exceptions;

public sealed class InputValidationException(string message) : Exception(message)
{
}