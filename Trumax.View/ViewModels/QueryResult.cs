namespace Trumax.View.ViewModels
{
    public class QueryResult
    {
        public List<string> Columns { get; set; } = new();
        public List<List<object?>> Rows { get; set; } = new();
        public long ElapsedMs { get; set; }
        public bool Truncated { get; set; }
    }
}
