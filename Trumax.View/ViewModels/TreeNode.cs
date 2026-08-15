namespace Trumax.View.ViewModels
{
    public class TreeNode
    {
        public string Id { get; set; } = string.Empty;   // شناسه یکتا برای هر گره
        public string Text { get; set; } = string.Empty; // متنی که نمایش داده می‌شود
        public TreeNodeType NodeType { get; set; }
        public bool HasChildren { get; set; }
    }
}
