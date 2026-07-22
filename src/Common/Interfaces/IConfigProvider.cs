using CarryOn.Common.Models;

namespace CarryOn.Common.Interfaces
{
    /// <summary>Provides access to the CarryOn server configuration.</summary>
    public interface IConfigProvider
    {
        /// <summary>Gets the current CarryOn server configuration.</summary>
        CarryOnConfig Config { get; }
    }
}
