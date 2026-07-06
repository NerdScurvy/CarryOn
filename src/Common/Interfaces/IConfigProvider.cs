using CarryOn.Common.Models;

namespace CarryOn.Common.Interfaces
{
    public interface IConfigProvider
    {
        CarryOnConfig Config { get; }
    }
}
