using System.Collections.Concurrent;
using ComfyBridge.Application.Contracts;
using ComfyBridge.Application.Models;

namespace ComfyBridge.Application.Services;

public sealed class InMemoryWorkflowDraftStore : IWorkflowDraftStore
{
    private readonly ConcurrentDictionary<string, WorkflowAnalysisResult> _drafts = new(StringComparer.OrdinalIgnoreCase);

    public string SaveDraft(WorkflowAnalysisResult result)
    {
        var id = Guid.NewGuid().ToString("N");
        _drafts[id] = result;
        return id;
    }

    public bool TryGetDraft(string draftId, out WorkflowAnalysisResult? result)
    {
        if (_drafts.TryGetValue(draftId, out var found))
        {
            result = found;
            return true;
        }

        result = null;
        return false;
    }

    public bool RemoveDraft(string draftId) => _drafts.TryRemove(draftId, out _);
}