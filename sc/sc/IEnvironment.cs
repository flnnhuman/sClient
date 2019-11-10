using System.Threading.Tasks;

namespace sc
{
    public interface IEnvironment
    {
        Task<MyTheme> GetOperatingSystemTheme();
    }

    public enum MyTheme { Light, Dark }
}
