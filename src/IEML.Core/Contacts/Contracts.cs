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
    Task<int> LaunchAsync(Guid instanceId, IProgress<(string Stage, double Percent)>? progress = null, CancellationToken ct = default, Guid? accountIdOverride = null);
}

public enum ContentKind { Mod, ResourcePack, Shader, Modpack }
public record ContentHit(string ProjectId, string Title, string Description, string? IconUrl, ContentKind Kind);

public interface IModrinthService
{
    Task<IReadOnlyList<ContentHit>> SearchAsync(string query, ContentKind kind, string mcVersion, LoaderType? loader);
    Task InstallAsync(Guid instanceId, string projectId, ContentKind kind, CancellationToken ct = default);
    Task<Guid> InstallModpackAsync(string projectId, CancellationToken ct = default); // returns new instance id
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
