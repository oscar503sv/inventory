namespace Inventory.Application.Common.Validation;

public static class IdentityInputLimits
{
    public const int EmailLength = 256;
    public const int FullNameLength = 256;
    public const int UserNameLength = 128;
    public const int PasswordLength = 128;
    public const int PasswordUtf8Bytes = 72;
    public const int MinimumPasswordLength = 8;
}
