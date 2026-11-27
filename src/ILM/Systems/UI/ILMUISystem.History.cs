namespace ILM.Systems.UI
{
    // The undo / redo buttons. The history itself lives in MarkingNodeToolSystem.History.cs.
    public partial class ILMUISystem
    {
        private void RegisterHistoryTriggers()
        {
            CreateTrigger("Undo", () => _tool.Undo());
            CreateTrigger("Redo", () => _tool.Redo());
        }
    }
}
