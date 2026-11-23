using IEML.Core.Models;
namespace IEML.Core.Contracts;

public interface IAccountService
{
    Task<IReadOnlyList<Account>> ListAsync();
    Task<Account?> GetAsync(Guid id);
    Task<Account> AddOfflineAsync(string name);
    Task<Account> AddMicrosoftAsync();                 // interactive Microsoft login, stores account
    Task<CmlLib.Core.Auth.MSession> GetSessionAsync(Guid id); // offline or (silent -> interactive) Microsoft session
    Task SaveRefreshTokenAsync(Guid id, string token); // encrypts with DPAPI
    Task<string?> GetRefreshTokenAsync(Guid id);
    Task RemoveAsync(Guid id);
}

public interface IInstanceService
{
    Task<IReadOnlyList<InstanceConfig>> ListAsync();
    Task<InstanceConfig?> GetAsync(Guid id);
    Task<InstanceConfig> CreateAsync(InstanceConfig cfg);
    Task SaveAsync(InstanceConfig cfg);
    Task DeleteAsync(Guid id);
    Task BindAccountAsync(Guid instanceId, Guid accountId);
    string GetInstanceDir(Guid id);   // .minecraft root of that instance
}

public interface ILaunchService
{
    /// Resolve account for instance -> install version/loader -> launch.
    Task<int> LaunchAsync(Guid instanceId, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default, Guid? accountIdOverride = null, string? serverOverride = null);
}

public enum ContentKind { Mod, ResourcePack, Shader, Modpack }
public record ContentHit(string ProjectId, string Title, string Description, string? IconUrl, ContentKind Kind,
    long Downloads = 0, string? Author = null)
{
    public string DownloadsText => Downloads >= 1_000_000 ? $"{Downloads / 1_000_000.0:0.#}M 下載"
        : Downloads >= 1_000 ? $"{Downloads / 1_000.0:0.#}K 下載" : $"{Downloads} 下載";
    public string AuthorText => string.IsNullOrEmpty(Author) ? "" : "作者：" + Author;
}
/// <summary>Modrinth 搜尋條件。Sort：relevance / downloads / follows / newest / updated。Environment：null / client / server。</summary>
public record ModrinthQuery(string Text, ContentKind Kind, string? McVersion = null, string? Loader = null,
    string? Category = null, string? Environment = null, string Sort = "relevance", int Offset = 0, int Limit = 20);
public record ContentPage(IReadOnlyList<ContentHit> Hits, int Total);
public record FilterOption(string Id, string Name) { public override string ToString() => Name; }
public record UpdateInfo(string OldPath, string NewFileName, string Url, string? Sha1, string VersionName);

public interface IModrinthService
{
    Task<IReadOnlyList<ContentHit>> SearchAsync(string query, ContentKind kind, string mcVersion, LoaderType? loader);
    Task InstallAsync(Guid instanceId, string projectId, ContentKind kind, CancellationToken ct = default);
    Task<Guid> InstallModpackAsync(string projectId, CancellationToken ct = default); // returns new instance id
    // --- Update 6：篩選／排序／分頁、已安裝內容更新 ---
    Task<ContentPage> SearchAsync(ModrinthQuery q, CancellationToken ct = default);
    Task<IReadOnlyList<FilterOption>> GetCategoriesAsync(ContentKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<FilterOption>> GetLoadersAsync(ContentKind kind, CancellationToken ct = default);
    Task<IReadOnlyList<UpdateInfo>> CheckUpdatesAsync(Guid instanceId, ContentKind kind, CancellationToken ct = default);
    Task ApplyUpdateAsync(UpdateInfo update, CancellationToken ct = default);
}

public record ServerStatus(string Address, bool Online, string Motd, int Players, int MaxPlayers, string Version, int LatencyMs);
public interface IServerPingService
{
    Task<ServerStatus> PingAsync(string address, CancellationToken ct = default);   // Server List Ping, TCP
    Task<IReadOnlyList<ServerStatus>> SearchAsync(string keyword, CancellationToken ct = default);
}

public record EasyTierRoom(string NetworkName, string Secret, string? PeerUrl);
public interface IEasyTierService
{
    EasyTierRoom CreateRoom();                           // random name + secret
    Task<string> StartAsync(EasyTierRoom room, CancellationToken ct = default); // spawns easytier-core, returns virtual IP
    Task StopAsync();
    bool IsRunning { get; }
}

public record StructureHit(string Type, int X, int Z);
public interface ISeedMapService
{
    // Native: Cubiomes via P/Invoke
    IReadOnlyList<StructureHit> FindStructures(long seed, string mcVersion, string structureType, int centerX, int centerZ, int radius);
    string GetBiome(long seed, string mcVersion, int x, int z);
}

public record Track(string Path, string Title);
public interface IMusicService
{
    IReadOnlyList<Track> Playlist { get; }
    Track? Current { get; }
    event Action? Changed;
    void AddFolder(string path);
    void Play(Track? t = null); void Pause(); void Next(); void Previous();
    float Volume { get; set; }
}
