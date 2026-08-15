namespace Trumax.View.ViewModels
{
    public class SchemaRequest : Login
    {
        public string? DatabaseName { get; set; }

        public string? TableName { get; set; }
    }
}
