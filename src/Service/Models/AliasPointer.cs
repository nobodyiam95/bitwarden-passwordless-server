namespace Passwordless.Service.Models;

public class AliasPointer : PerTenant
{
    public string UserId { get; set; }
    public string Alias { get; set; }
    public string Plaintext { get; set; }
}