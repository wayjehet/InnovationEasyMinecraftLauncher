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
    // 自訂範本的下拉框若沒套到 DisplayMemberPath，會直接顯示型別名稱；覆寫 ToString 讓任何地方都顯示帳號名稱
    public override string ToString() => DisplayName;
}

public class InstanceConfig
{
    public override string ToString() => Name; // 下拉框選取項顯示名稱（自訂範本不吃 DisplayMemberPath）
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
    /// <summary>這個實例記錄的伺服器清單（實例設置 →「伺服器」分頁）。</summary>
    public List<InstanceServer> Servers { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Quick-launch cards shown on the main screen: launch this instance with another account.</summary>
    public List<QuickLaunchCard> QuickCards { get; set; } = new();
}

public class InstanceServer
{
    public string Name { get; set; } = "";
    /// <summary>host 或 host:port（沒寫埠就用 SRV／25565）。</summary>
    public string Address { get; set; } = "";
}

public class QuickLaunchCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    /// <summary>主畫面卡片位置（拖拉後儲存）；null＝還沒擺過，用預設排列。</summary>
    public double? X { get; set; }
    public double? Y { get; set; }
}
