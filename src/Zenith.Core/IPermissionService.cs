using System.Threading.Tasks;

namespace Zenith.Core;

public interface IPermissionService
{
    Task<bool> CheckScreenCapturePermissionAsync();
    Task<bool> RequestScreenCapturePermissionAsync();
}
