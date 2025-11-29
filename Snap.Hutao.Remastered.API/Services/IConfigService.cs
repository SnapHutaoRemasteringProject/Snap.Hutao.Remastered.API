using System.Threading.Tasks;

namespace Snap.Hutao.Remastered.API
{
    public interface IConfigService
    {
        Task<ConfigModel> GetConfigAsync();
        Task SaveConfigAsync(ConfigModel config);
    }
}
