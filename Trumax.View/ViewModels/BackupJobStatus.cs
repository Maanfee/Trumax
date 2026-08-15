namespace Trumax.View.ViewModels
{
    public class BackupJobStatus
    {
        public int PercentComplete { get; set; }
        public bool Completed { get; set; }
        public bool Failed { get; set; }
        public string? ErrorMessage { get; set; }
        public List<string> TempFilePaths { get; set; } = new(); // به‌جای TempFilePath تکی
        public string? DatabaseName { get; set; }
    }
}
