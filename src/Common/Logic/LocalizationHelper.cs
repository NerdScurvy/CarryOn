using Vintagestory.API.Config;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Logic
{
    public static class LocalizationHelper
    {
        public static string GetLang(string key) => Lang.Get(CarryOnCode(key)) ?? key;
    }
}
