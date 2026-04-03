namespace ComfyBridge.Domain.Exceptions;

public sealed class TemplateNotFoundException(string templateName, string? version = null)
    : Exception(version is null
        ? $"Template '{templateName}' was not found."
        : $"Template '{templateName}' with version '{version}' was not found.")
{
}