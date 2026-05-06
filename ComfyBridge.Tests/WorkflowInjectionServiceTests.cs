using System.Text.Json.Nodes;
using ComfyBridge.Application.Services;
using ComfyBridge.Domain.Models;

namespace ComfyBridge.Tests;

public sealed class WorkflowInjectionServiceTests
{
    [Fact]
    public void InjectInputs_AcceptsImageType_AsString()
    {
        var service = new WorkflowInjectionService();
        var template = BuildTemplate("image");
        var inputs = new JsonObject { ["image"] = "uploaded-frame.png" };

        var result = service.InjectInputs(template, inputs);

        var imageValue = result["1"]?["inputs"]?["image"]?.GetValue<string>();
        Assert.Equal("uploaded-frame.png", imageValue);
    }

    [Fact]
    public void InjectInputs_AcceptsFileType_AsString()
    {
        var service = new WorkflowInjectionService();
        var template = BuildTemplate("file");
        var inputs = new JsonObject { ["image"] = "uploaded-frame.png" };

        var result = service.InjectInputs(template, inputs);

        var imageValue = result["1"]?["inputs"]?["image"]?.GetValue<string>();
        Assert.Equal("uploaded-frame.png", imageValue);
    }

    private static WorkflowTemplate BuildTemplate(string inputType)
    {
        var workflow = JsonNode.Parse("""
        {
          "1": {
            "class_type": "LoadImage",
            "inputs": {
              "image": "default.png"
            }
          }
        }
        """)!;

        return new WorkflowTemplate
        {
            Name = "i2v-test",
            Version = "1.0",
            Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["image"] = inputType
            },
            Mapping = new Dictionary<string, WorkflowInputMapping>(StringComparer.OrdinalIgnoreCase)
            {
                ["image"] = new WorkflowInputMapping
                {
                    NodeId = "1",
                    Field = "image"
                }
            },
            Workflow = workflow
        };
    }
}
