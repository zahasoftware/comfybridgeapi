using ComfyBridge.Application.Models;

namespace ComfyBridge.Application.Contracts;

public interface IWorkflowDraftStore
{
    string SaveDraft(WorkflowAnalysisResult result);

    bool TryGetDraft(string draftId, out WorkflowAnalysisResult? result);

    bool RemoveDraft(string draftId);
}