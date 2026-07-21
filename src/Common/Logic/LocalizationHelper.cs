using Vintagestory.API.Config;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Logic
{
    public static class LocalizationHelper
    {
        public static string GetLang(string key) => Lang.Get(GetCarryCode(key)) ?? key;

        public static string GetLang(string key, params object[] args) => Lang.Get(GetCarryCode(key), args) ?? key;
    }
}
