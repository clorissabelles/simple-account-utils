using Tomlyn.Model;

namespace SimpleAccountUtils;

public class AccountSettings : ITomlMetadataProvider
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? GpgKey { get; set; }

    public string? SshFileName { get; set; }
    public string? GpgFileName { get; set; }
    
    TomlPropertiesMetadata? ITomlMetadataProvider.PropertiesMetadata { get; set; }
};