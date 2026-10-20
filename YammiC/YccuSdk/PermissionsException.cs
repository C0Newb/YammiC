namespace CNCO.YccuSdk;

public class PermissionsException : UnauthorizedAccessException
{
    public PermissionsException(string permission)
        : base($"Permission not granted: {permission}")
    {
        Permission = permission;
    }

    public string Permission { get; }
}
