namespace Trumax.View.ViewModels
{
    public class Login
    {
        public Login()
        {
            Authentications = new List<Authentication>
            {
                new() { Id = 1, Name = "Windows Authentication" },
                new() { Id = 2, Name = "SQL Authentication" },
            };
        }

        public IEnumerable<Authentication> Authentications { get; set; }

        public int? IdAuthentication { get; set; } = 1;

        // **********************************************

        public string? ServerName { get; set; } = "localhost";
        
        public string? UserName { get; set; }
        
        public string? Password { get; set; }

    }
}
