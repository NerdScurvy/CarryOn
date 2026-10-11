using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.Utility;
using Vintagestory.API.Config;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// The wire side of the per-player locale fix. CarryOn authors its toast text on the
    /// server (<c>CarryErrorHelper.SendError</c> resolves the lang entry and passes it as
    /// <c>Message</c>), so the only place the player's language can be honoured is the
    /// server call itself - the client shows a non-null <c>Message</c> verbatim. Every row
    /// therefore drives a real <c>SendError</c> and asserts on the text the player reads
    /// off the vanilla wire, not on the code that carried it.
    /// <para>
    /// The two keys are chosen on purpose: <c>entity-out-of-reach</c> ships in both
    /// <c>en</c> and <c>de</c>, so it is what proves the fix; <c>block-out-of-reach</c>
    /// ships in <c>en</c> only, so it is what proves the fallback contract did not
    /// regress. A row that used an untranslated key for the first job would pass whether
    /// or not the fix exists.
    /// </para>
    /// </summary>
    [Trait("area", "Smoke")]
    [Trait("summary", "Per-player locale toast rows: CarryOn toasts carry server-resolved text in the player's own language, and an untranslated key still falls back to English rather than raw key text.")]
    public class PlayerLanguageToastScenarios : CarryOnScenarioBase
    {
        private const string German = "de";
        private const string TranslatedCode = FailureCodes.EntityOutOfReach;
        private const string UntranslatedCode = FailureCodes.BlockOutOfReach;

        /// <summary>
        /// A German player reads the German text. This is the red row: before
        /// <c>SendError</c> resolved in the player's language the toast carried
        /// <c>Lang.Get(...)</c> from the server's own locale, which on an English server
        /// is the English sentence. The premise asserts the two locales actually differ,
        /// so a later translation edit that emptied <c>de</c> fails here with a named
        /// cause instead of letting the row pass vacuously.
        /// </summary>
        [Trait("summary", "A German player reads a key that ships in both locales as the German text on the vanilla wire, after a premise that the two locales differ.")]
        [Trait("pins", "The server resolves the toast in the player's own language rather than the server's locale.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed before SendError resolved in the player's language, when the toast carried the server's own locale text.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SendError_GermanPlayer_ToastTextIsGerman()
        {
            string key = CarryErrorHelper.ResolveLangKey(TranslatedCode);
            string english = Lang.Get(key);
            string german = Lang.GetL(German, key);
            Assert.True(german != english,
                $"Row premise: {key} must differ between en and de for this row to prove the fix - "
                + $"en is \"{english}\", {German} is \"{german}\"");

            ITestPlayer player = await JoinPlayer("Tester");
            UseLanguage(player, German);

            await World.Ticks(8);
            player.Client.Clear();

            CarryErrorHelper.SendError(player.Player, TranslatedCode);

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            var toasts = wire.ErrorsWithCode(TranslatedCode);
            Assert.True(toasts.Count > 0, wire.Describe());
            Assert.True(toasts[0].HasServerText,
                "CarryOn toasts carry resolved server text, not a bare code - " + wire.Describe());
            Assert.Equal(german, toasts[0].VerbatimText);
        }

        /// <summary>
        /// A player on the server's own language still reads exactly what it read before
        /// the fix: <c>Lang.GetL</c> and <c>Lang.Get</c> agree when the two locales agree,
        /// so this pins the non-regression half rather than only the new behaviour.
        /// </summary>
        [Trait("summary", "A player on the server's own locale reads exactly what they read before the fix, pinning the non-regression half.")]
        [Trait("pins", "Per-player resolution does not change behavior when the player's locale matches the server's.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SendError_ServerLocalePlayer_ToastTextUnchanged()
        {
            string key = CarryErrorHelper.ResolveLangKey(TranslatedCode);
            string english = Lang.Get(key);

            ITestPlayer player = await JoinPlayer("Tester");
            Assert.Equal(Lang.CurrentLocale, player.Player.LanguageCode);

            await World.Ticks(8);
            player.Client.Clear();

            CarryErrorHelper.SendError(player.Player, TranslatedCode);

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            var toasts = wire.ErrorsWithCode(TranslatedCode);
            Assert.True(toasts.Count > 0, wire.Describe());
            Assert.Equal(english, toasts[0].VerbatimText);
        }

        /// <summary>
        /// A German player whose language lacks the key still reads English rather than
        /// the raw <c>carryon:block-out-of-reach</c> key. This is the promise the locale
        /// fix has to keep: per-player resolution must never turn an untranslated key
        /// into literal key text on screen.
        /// </summary>
        [Trait("summary", "A German player whose locale lacks the key still reads English rather than the raw key text, after a premise that the key is untranslated.")]
        [Trait("pins", "Per-player resolution never turns an untranslated key into literal key text on screen.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task SendError_GermanPlayer_UntranslatedKeyStillFallsBackToEnglish()
        {
            string key = CarryErrorHelper.ResolveLangKey(UntranslatedCode);
            string english = Lang.Get(key);
            Assert.True(Lang.GetL(German, key) == english,
                $"Row premise: {key} must be the untranslated case - en is \"{english}\", "
                + $"{German} resolves it to \"{Lang.GetL(German, key)}\"");

            ITestPlayer player = await JoinPlayer("Tester");
            UseLanguage(player, German);

            await World.Ticks(8);
            player.Client.Clear();

            CarryErrorHelper.SendError(player.Player, UntranslatedCode);

            await World.Ticks(1);
            var wire = VanillaClientWire.Tap(player);

            var toasts = wire.ErrorsWithCode(UntranslatedCode);
            Assert.True(toasts.Count > 0, wire.Describe());
            Assert.Equal(english, toasts[0].VerbatimText);
        }
    }
}
