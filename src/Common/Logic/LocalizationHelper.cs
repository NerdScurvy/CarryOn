using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace CarryOn.Common.Logic
{
    public static class LocalizationHelper
    {
        /// <summary>
        /// Resolves <paramref name="key"/> in the <b>calling process's</b> locale via
        /// <see cref="Lang.Get(string)"/>. Do not use for text sent to a player from the
        /// server - use the <see cref="IServerPlayer"/> overload instead, which resolves in
        /// the receiving player's language.
        /// </summary>
        public static string GetLang(string key) => Lang.Get(CarryOn.Utility.CarryErrorHelper.ResolveLangKey(key)) ?? key;

        /// <summary>
        /// Resolves <paramref name="key"/> in the <b>calling process's</b> locale via
        /// <see cref="Lang.Get(string, object[])"/>. Do not use for text sent to a player
        /// from the server - use the <see cref="IServerPlayer"/> overload instead, which
        /// resolves in the receiving player's language.
        /// </summary>
        public static string GetLang(string key, params object[] args) => Lang.Get(CarryOn.Utility.CarryErrorHelper.ResolveLangKey(key), args) ?? key;

        /// <summary>
        /// Resolves <paramref name="key"/> in <paramref name="player"/>'s language via
        /// <see cref="Lang.GetL(string, string, object[])"/>. This is the blessed server-side
        /// path for player-facing text: <see cref="Lang.Get(string, object[])"/> on the server
        /// resolves in the server process's locale, which is wrong for anything a specific
        /// player reads.
        /// <para>
        /// Routes through <see cref="CarryOn.Utility.CarryErrorHelper.ResolveLangKey(string)"/>
        /// so a bare code becomes <c>carryon:&lt;code&gt;</c> and an already-qualified key
        /// passes through untouched. A null/empty <c>LanguageCode</c> falls back to
        /// <see cref="Lang.Get(string, object[])"/>, matching
        /// <c>CarryErrorHelper.ResolveLang</c> - one fallback rule, two sites.
        /// </para>
        /// </summary>
        public static string GetLang(IServerPlayer player, string key, params object[] args)
        {
            string langKey = CarryOn.Utility.CarryErrorHelper.ResolveLangKey(key);
            string languageCode = player?.LanguageCode ?? string.Empty;
            return (string.IsNullOrEmpty(languageCode)
                ? Lang.Get(langKey, args)
                : Lang.GetL(languageCode, langKey, args)) ?? key;
        }
    }
}
