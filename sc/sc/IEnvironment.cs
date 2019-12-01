using System.Threading.Tasks;

namespace sc
{
    public interface IEnvironment
    {
        Task<Theme> GetOperatingSystemTheme();
    }

    public enum Theme
    {
        Light,
        Dark
    }
}