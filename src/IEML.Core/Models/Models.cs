using System.Text.Json.Serialization;
namespace IEML.Core.Models;

public enum AccountType { Offline, Microsoft }
public enum LoaderType { Vanilla, Fabric, Forge, NeoForge, Quilt }

public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    public AccountType Type { get; set; }
    /// <summary>DPAPI-encrypted, Base64. Never store plaintext tokens.</summary>
    public string? ProtectedRefreshToken { get; set; }
    [JsonIgnore] public string DisplayName => Type == AccountType.Microsoft ? $"{Name}（正版）" : $"{Name}（離線）";
}

public class InstanceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Instance";
    public string McVersion { get; set; } = "";
    public LoaderType Loader { get; set; } = LoaderType.Vanilla;
    public string? LoaderVersion { get; set; }
    public string? JavaPath { get; set; }
    public int MaxMemoryMb { get; set; } = 4096;
    public string? ExtraJvmArgs { get; set; }
    /// <summary>Which account launches this instance.</summary>
    public Guid? AccountId { get; set; }
    /// <summary>Optional: auto-join server on launch.</summary>
    public string? AutoJoinServer { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Quick-launch cards shown on the main screen: launch this instance with another account.</summary>
    public List<QuickLaunchCard> QuickCards { get; set; } = new();
}

public class QuickLaunchCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    /// <summary>主畫面卡片位置（拖拉後儲存）；null＝還沒擺過，用預設排列。</summary>
    public double? X { get; set; }
    public double? Y { get; set; }
}
