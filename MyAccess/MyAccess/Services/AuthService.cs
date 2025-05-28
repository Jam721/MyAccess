namespace MyAccess.Services;

public class AuthService
{
    public event Action OnUserChanged;
    
    public User CurrentUser { get; private set; }

    public void Login(string username, string role)
    {
        CurrentUser = new User { Username = username, Role = role };
        OnUserChanged?.Invoke();
    }

    public void Logout()
    {
        CurrentUser = null;
        OnUserChanged?.Invoke();
    }
}

public class User
{
    public string Username { get; set; }
    public string Role { get; set; } // "admin", "reader"
}