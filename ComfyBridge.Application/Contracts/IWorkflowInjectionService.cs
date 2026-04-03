using System.Text.Json.Nodes;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Application.Contracts;

public interface IWorkflowInjectionService
{
    JsonNode InjectInputs(WorkflowTemplate template, JsonObject inputs);
}