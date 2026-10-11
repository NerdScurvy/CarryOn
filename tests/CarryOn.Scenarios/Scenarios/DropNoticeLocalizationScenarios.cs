using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn.API.Common.Models;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Wire side of the drop-notice localization fix. <c>MessageOnBlockDropped</c> authors
    /// the notice on the server, so the only place the dropping player's language can be
    /// honoured is the server send itself.
    /// <para>
    /// Mechanism differs from <see cref="PlayerLanguageToastScenarios"/>: those rows read
    /// <c>VanillaClientWire.Tap</c>, but chat lines are an <em>Atlas observation</em>, not a
    /// decoded wire type (<c>VanillaClientWire</c> currently folds them into
    /// <c>OtherPackets</c>). The terminal read here is <c>player.Client.ChatLines()</c>
    /// (<c>IClientObservations.ChatLines</c>, Atlas 0.13.1), with
    /// <c>player.Client.Clear()</c> before the action and <b>no</b> <c>Tap</c> in the same
    /// window - both drain the same TCP buffer destructively. Decoded observation lists
    /// accumulate until <c>Clear()</c> (decompile-verified: <c>ChatLines()</c> returns a
    /// copy of the accumulated list).
    /// </para>
    /// <para>
    /// Trigger: <c>Actions.PickUpIntoHands</c> then <c>Actions.Drop(player.Entity,
    /// CarrySlot.Hands)</c>. Open-field placement produces the bare
    /// <c>carryon:drop-notice</c> key (<c>destroyed</c>/<c>hadContents</c> both false).
    /// <c>MessageOnBlockDropped.Init</c> registers on the server side, so it is live here.
    /// </para>
    /// </summary>
    [Trait("area", "Drop")]
    [Trait("summary", "Drop-notice localization rows: a joined player picks up and drops a chest, and the row asserts the chat notice the dropping player receives resolves in their language, stays single-recipient, and never emits a raw slot key.")]
    public class DropNoticeLocalizationScenarios : CarryOnScenarioBase
    {
        private const string German = "de";
        private const string ChestCode = "game:chest-east";

        private static string DropNoticeKey => $"{ModId}:drop-notice";
        private static string SlotHandsKey => $"{ModId}:slot-hands";

        /// <summary>
        /// A German player reads the German drop notice. This is the red row: before the
        /// fix the sentence resolved with server-locale <c>Lang.Get</c>, which on an
        /// English server is the English sentence even for a German player. The premise
        /// asserts the two locales actually differ, so a later translation edit that
        /// emptied <c>de</c> fails here with a named cause instead of letting the row pass
        /// vacuously. Prefix/suffix are derived from the <c>de</c> template split on the
        /// placeholders, so a translator editing <c>de.json</c> does not break the row.
        /// </summary>
        [Trait("summary", "A German player, on a server whose own locale is English, reads the German drop notice after dropping a chest.")]
        [Trait("pins", "The drop notice is resolved in the dropping player's language at the server send.")]
        [Trait("role", "regression")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with the German player receiving the English sentence from the server-locale lookup.")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropNotice_GermanPlayer_TextIsGerman()
        {
            string english = Lang.Get(DropNoticeKey);
            string german = Lang.GetL(German, DropNoticeKey);
            Assert.True(german != english,
                $"Row premise: {DropNoticeKey} must differ between en and de for this row to prove the fix - "
                + $"en is \"{english}\", {German} is \"{german}\"");

            (string prefix, string suffix) = SplitOnPlaceholders(german);
            string germanSlot = Lang.GetL(German, SlotHandsKey);

            ITestPlayer player = await JoinPlayer("Tester");
            UseLanguage(player, German);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, pos);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var pickupCode);
            Assert.True(carried != null, $"Chest pickup failed: {pickupCode}");

            await World.Ticks(8);
            player.Client.Clear();

            Actions.Drop(player.Entity, CarrySlot.Hands);

            await World.Ticks(1);
            IReadOnlyList<string> lines = player.Client.ChatLines();
            RequireDropNoticeLine(lines, prefix, suffix, germanSlot);
        }

        /// <summary>
        /// A player on the server's own language still reads exactly what it read before
        /// the fix: the line is split-asserted against <c>Lang.Get</c>'s template, which is
        /// the pre-fix resolution path. This pins the non-regression half rather than only
        /// the new behaviour.
        /// </summary>
        [Trait("summary", "A player on the server's own language still reads exactly the pre-fix sentence, split-asserted against the server-locale template.")]
        [Trait("pins", "The pre-fix resolution path for a same-locale player is unchanged.")]
        [Trait("role", "control")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropNotice_EnglishPlayer_TextUnchanged()
        {
            string english = Lang.Get(DropNoticeKey);
            (string prefix, string suffix) = SplitOnPlaceholders(english);
            string englishSlot = Lang.Get(SlotHandsKey);

            ITestPlayer player = await JoinPlayer("Tester");
            Assert.Equal(Lang.CurrentLocale, player.Player.LanguageCode);

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, pos);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var pickupCode);
            Assert.True(carried != null, $"Chest pickup failed: {pickupCode}");

            await World.Ticks(8);
            player.Client.Clear();

            Actions.Drop(player.Entity, CarrySlot.Hands);

            await World.Ticks(1);
            IReadOnlyList<string> lines = player.Client.ChatLines();
            RequireDropNoticeLine(lines, prefix, suffix, englishSlot);
        }

        /// <summary>
        /// The dropping player's notice is a single-recipient send. A second player in the
        /// same session receives no drop-notice line after the first player drops. This is
        /// the guard that locks the single-recipient correction (verified by decompile on
        /// 2026-10-06: <c>ServerPlayer.SendMessage</c> -&gt; one packet, one player); it is
        /// a regression guard, not a red row.
        /// </summary>
        [Trait("summary", "After a player drops a chest, the dropper receives the notice and a second player in the same session receives nothing.")]
        [Trait("pins", "The drop notice is a single-recipient send, not a broadcast.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropNotice_SingleRecipient_BystanderGetsNothing()
        {
            string english = Lang.Get(DropNoticeKey);
            (string prefix, string _) = SplitOnPlaceholders(english);

            ITestPlayer dropper = await JoinPlayer("Dropper");
            ITestPlayer bystander = await JoinPlayer("Bystander");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, pos);
            await dropper.TeleportTo(World.Spawn.Offset(2, 0, 2));
            await bystander.TeleportTo(World.Spawn.Offset(6, 0, 2));

            var carried = Actions.PickUpIntoHands(dropper.Entity, pos, out var pickupCode);
            Assert.True(carried != null, $"Chest pickup failed: {pickupCode}");

            await World.Ticks(8);
            dropper.Client.Clear();
            bystander.Client.Clear();

            Actions.Drop(dropper.Entity, CarrySlot.Hands);

            await World.Ticks(1);
            IReadOnlyList<string> dropperLines = dropper.Client.ChatLines();
            Assert.True(dropperLines.Any(l => l.StartsWith(prefix, StringComparison.Ordinal)),
                $"Dropper must receive the drop notice - {Describe(dropperLines)}");

            IReadOnlyList<string> bystanderLines = bystander.Client.ChatLines();
            Assert.False(bystanderLines.Any(l => l.StartsWith(prefix, StringComparison.Ordinal)),
                $"Bystander must not receive the dropper's drop notice - {Describe(bystanderLines)}");
        }

        /// <summary>
        /// After a Hands drop, no chat line contains the literal <c>slot-</c> key token.
        /// Before Phase 2 the key was built from <c>enum.ToString()</c>, which for the one
        /// unreachable-but-unmapped member (<c>Attached</c> -&gt; <c>slot-attached</c>, missing
        /// from every locale) would put the raw key on screen. A Hands drop resolves
        /// cleanly today, so this is a cheap latent-bug guard, not a red row.
        /// </summary>
        [Trait("summary", "After a hands drop, no chat line contains the raw slot lang key token.")]
        [Trait("pins", "The slot word resolves through the locale table instead of falling back to a raw key.")]
        [Trait("role", "regression")]
        [AtlasScenario(RollbackWorld = true)]
        public async Task DropNotice_NeverEmitsRawSlotKey()
        {
            ITestPlayer player = await JoinPlayer("Tester");

            BlockPos pos = World.Spawn.Offset(2, 1, 0);
            World.SetBlock(ChestCode, pos);
            await player.TeleportTo(World.Spawn.Offset(2, 0, 2));

            var carried = Actions.PickUpIntoHands(player.Entity, pos, out var pickupCode);
            Assert.True(carried != null, $"Chest pickup failed: {pickupCode}");

            await World.Ticks(8);
            player.Client.Clear();

            Actions.Drop(player.Entity, CarrySlot.Hands);

            await World.Ticks(1);
            IReadOnlyList<string> lines = player.Client.ChatLines();
            Assert.False(lines.Any(l => l.Contains("slot-", StringComparison.Ordinal)),
                $"No drop-notice line may carry a raw slot lang key - {Describe(lines)}");
        }

        private static (string Prefix, string Suffix) SplitOnPlaceholders(string template)
        {
            int nameIdx = template.IndexOf("{0}", StringComparison.Ordinal);
            int slotIdx = template.IndexOf("{1}", StringComparison.Ordinal);
            Assert.True(nameIdx >= 0 && slotIdx > nameIdx,
                $"Row premise: {DropNoticeKey} template must contain {{0}} then {{1}} - got \"{template}\"");
            return (template[..nameIdx], template[(slotIdx + "{1}".Length)..]);
        }

        private static string RequireDropNoticeLine(
            IReadOnlyList<string> lines,
            string prefix,
            string suffix,
            string expectedSlotWord)
        {
            var matches = lines
                .Where(l => l.StartsWith(prefix, StringComparison.Ordinal)
                    && l.EndsWith(suffix, StringComparison.Ordinal))
                .ToList();
            Assert.True(matches.Count > 0,
                $"Expected a drop-notice line starting \"{prefix}\" and ending \"{suffix}\" - {Describe(lines)}");
            Assert.True(matches[0].Contains(expectedSlotWord, StringComparison.Ordinal),
                $"Drop notice must name the slot word \"{expectedSlotWord}\" - got \"{matches[0]}\"");
            return matches[0];
        }

        private static string Describe(IReadOnlyList<string> lines)
            => lines.Count == 0
                ? "(no chat lines)"
                : string.Join(" | ", lines.Select(l => $"\"{l}\""));
    }
}
