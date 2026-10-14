using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Atlas.Api;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Common;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Raw vanilla-wire tap for one test player: drains the player's dummy client
    /// connection with the engine's own deserializer and records the vanilla
    /// server-to-client packets that Atlas's observation surface (IClientObservations,
    /// which keeps only highlights, particles, mod-channel packets and chat lines)
    /// drains and discards. Captures the block-resync channel the server's 100 ms
    /// dirty flush drives (ServerSystemBlockSimulation.UpdateEvery100ms):
    /// ExchangeBlock packets (block id per cell, the phantom-block eviction),
    /// BlockEntities packets (per-cell BE data, or the null-classname removal
    /// marker for cells with no server-side BE - the phantom-BE eviction), and
    /// IngameError packets (failure-code toasts).
    ///
    /// Ordering contract: the underlying buffer is drained destructively by BOTH
    /// this tap and every Atlas observation read (including Clear()). A scenario
    /// that wants the vanilla wire must call player.Client.Clear() before the
    /// action, make no Atlas observation calls afterwards, and tap once at the
    /// end - the tap then owns everything the server sent in the window. Test-side
    /// reflection only, mirroring HandlerInvoke: resolution fails fast with the
    /// drifted symbol named. The concrete Atlas ClientObservations holds the
    /// player's DummyTcpNetClient in the private field "_client" (Atlas 0.13.1
    /// decompile pin), and the engine's serializer is
    /// Packet_ServerSerializer.DeserializeBuffer(byte[], int, Packet_Server) -
    /// the exact calls Atlas's own Drain() makes, so the tap decodes identically.
    /// </summary>
    public sealed class VanillaClientWire
    {
        /// <summary>One ExchangeBlock packet: the server-truth block id for a cell.</summary>
        public sealed record BlockUpdate(BlockPos Pos, int BlockId);

        /// <summary>One BlockEntities entry: BE data for a cell, or the removal
        /// marker (null classname) for a cell the server holds no BE at.</summary>
        public sealed record BlockEntityUpdate(BlockPos Pos, string? Classname, bool IsRemoval);

        /// <summary>
        /// One IngameError packet: the toast the server sent to the player.
        ///
        /// The packet carries three fields, and which ones are populated decides what the
        /// player actually reads. The client handler
        /// (<c>GeneralPacketHandler.HandleIngameError</c>) resolves the toast as:
        /// <list type="bullet">
        /// <item><see cref="Message"/> non-null -> shown <em>verbatim</em>, with no further
        /// lookup. This is what <c>CarryErrorHelper.SendError</c> does, since it passes a
        /// resolved string rather than null - so the locale that decides the wording is the
        /// one the <em>server</em> resolved in, which <c>SendError</c> takes from
        /// <c>IServerPlayer.LanguageCode</c> (the reading player's language) rather than
        /// from <c>Lang.CurrentLocale</c> (the server's own).</item>
        /// <item><see cref="Message"/> null -> the client looks up
        /// <c>ingameerror-&lt;Code&gt;</c> in its <em>own</em> locale and substitutes
        /// <see cref="LangParams"/> for <c>{0}</c>-style holes. This is what vanilla does:
        /// <c>ServerSystemBlockSimulation.HandleBlockPlaceOrBreak</c> calls
        /// <c>SendIngameError(code, null, claimant)</c>.</item>
        /// </list>
        ///
        /// The distinction matters for assertions. A server-text toast must be non-empty
        /// and must not contain a raw <c>{0}</c>, because nothing will ever format it - a
        /// literal brace reaches the HUD. A client-resolved toast must carry enough
        /// <see cref="LangParams"/> to fill every hole in its lang entry, or
        /// <c>TranslationService.TryFormat</c> throws on <c>string.Format</c>, swallows the
        /// exception, and returns the <em>unsubstituted</em> string, putting a literal
        /// <c>{0}</c> on screen.
        /// </summary>
        public sealed record IngameError(string Code, string? Message, IReadOnlyList<string> LangParams)
        {
            /// <summary>The server sent display text, which the client shows verbatim
            /// without any lang lookup or placeholder substitution.</summary>
            public bool HasServerText => Message != null;

            /// <summary>The client resolves this toast from its own lang file, using
            /// <see cref="LangParams"/> to fill <c>{0}</c>-style holes.</summary>
            public bool IsClientResolved => Message == null;

            /// <summary>
            /// The text as it will appear, or null when the client has to resolve it from
            /// its own lang entry (which the tap cannot see - that lookup happens in the
            /// client's process, not on this wire).
            /// </summary>
            public string? VerbatimText => Message;

            /// <summary>
            /// True when this toast would put a raw, unsubstituted <c>{0}</c> on screen -
            /// either because the server sent one inside <see cref="Message"/>, or because
            /// the client must format a lang entry containing one with no
            /// <see cref="LangParams"/> to fill it. Callers that know the lang entry for
            /// <see cref="Code"/> uses <c>{0}</c> should treat this as a hard failure.
            /// </summary>
            public bool HasUnsubstitutedPlaceholder
                => Message != null
                    ? Message.Contains("{0}", StringComparison.Ordinal)
                    : LangParams.Count == 0;
        }

        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        private static FieldInfo? clientField;

        /// <summary>ExchangeBlock packets drained at tap time, oldest first.</summary>
        public IReadOnlyList<BlockUpdate> BlockUpdates { get; }

        /// <summary>BlockEntities entries drained at tap time, oldest first.</summary>
        public IReadOnlyList<BlockEntityUpdate> BlockEntityUpdates { get; }

        /// <summary>IngameError packets drained at tap time, oldest first.</summary>
        public IReadOnlyList<IngameError> Errors { get; }

        /// <summary>Count of drained packets none of the above categories cover
        /// (chat, sounds, entity attributes, ...), for diagnostics.</summary>
        public int OtherPackets { get; private set; }

        private VanillaClientWire(
            IReadOnlyList<BlockUpdate> blockUpdates,
            IReadOnlyList<BlockEntityUpdate> blockEntityUpdates,
            IReadOnlyList<IngameError> errors)
        {
            BlockUpdates = blockUpdates;
            BlockEntityUpdates = blockEntityUpdates;
            Errors = errors;
        }

        /// <summary>
        /// Drains everything the server has sent to the player since the last
        /// drain (this tap or an Atlas observation read) and returns it decoded.
        /// </summary>
        public static VanillaClientWire Tap(ITestPlayer player)
        {
            var client = ResolveClient(player);
            var blockUpdates = new List<BlockUpdate>();
            var blockEntityUpdates = new List<BlockEntityUpdate>();
            var errors = new List<IngameError>();
            int other = 0;

            while (client.ReadMessage() is { } message)
            {
                var packet = Packet_ServerSerializer.DeserializeBuffer(
                    message.message, message.messageLength, new Packet_Server());

                if (packet.ExchangeBlock is { } exchange)
                {
                    blockUpdates.Add(new BlockUpdate(
                        new BlockPos(exchange.X, exchange.Y, exchange.Z), exchange.BlockType));
                    continue;
                }

                if (packet.BlockEntities is { } entities)
                {
                    var entries = entities.BlockEntitites;
                    var count = Math.Min(entities.BlockEntititesCount, entries?.Length ?? 0);
                    for (int i = 0; i < count; i++)
                    {
                        var entry = entries![i];
                        if (entry == null) continue;
                        blockEntityUpdates.Add(new BlockEntityUpdate(
                            new BlockPos(entry.PosX, entry.PosY, entry.PosZ),
                            entry.Classname,
                            entry.Classname == null));
                    }
                    continue;
                }

                if (packet.IngameError is { } error)
                {
                    // LangParams is null unless at least one key-26 field was written
                    // (the deserializer only allocates on LangParamsAdd), so a null here is
                    // the engine's own signal that the client should call Lang.Get with no
                    // arguments at all. Guard the count against the array length the same
                    // way the BlockEntities branch does.
                    var rawParams = error.LangParams;
                    var paramCount = Math.Min(error.GetLangParamsCount(), rawParams?.Length ?? 0);
                    var langParams = paramCount > 0
                        ? rawParams!.Take(paramCount).ToArray()
                        : Array.Empty<string>();

                    errors.Add(new IngameError(error.Code, error.Message, langParams));
                    continue;
                }

                other++;
            }

            return new VanillaClientWire(blockUpdates, blockEntityUpdates, errors)
            {
                OtherPackets = other
            };
        }

        /// <summary>At least one ExchangeBlock carried this exact position and
        /// block id (server truth), which is what evicts a client-side phantom
        /// block. Duplicate marks are legal (reconcile + failure bookkeeping can
        /// both mark message.Position), so "at least one" is the contract.</summary>
        public bool HasBlockUpdate(BlockPos pos, int expectedBlockId)
            => BlockUpdates.Any(u => u.Pos.Equals(pos) && u.BlockId == expectedBlockId);

        /// <summary>At least one BlockEntities entry is the removal marker for
        /// the cell (null classname + null data), which is what evicts a
        /// client-side phantom block entity.</summary>
        public bool HasBlockEntityRemoval(BlockPos pos)
            => BlockEntityUpdates.Any(u => u.Pos.Equals(pos) && u.IsRemoval);

        /// <summary>At least one BlockEntities entry carries BE data (non-null
        /// classname) for the cell - the server-authoritative BE the client
        /// should hold there.</summary>
        public bool HasBlockEntityData(BlockPos pos)
            => BlockEntityUpdates.Any(u => u.Pos.Equals(pos) && !u.IsRemoval);

        /// <summary>All IngameError packets carrying the given failure code.</summary>
        public IReadOnlyList<IngameError> ErrorsWithCode(string code)
            => Errors.Where(e => e.Code == code).ToArray();

        /// <summary>
        /// Every toast would render with no raw <c>{0}</c> on screen. True when the wire
        /// holds no packet that either embeds an unsubstituted placeholder in server text
        /// or is client-resolved with no <c>LangParams</c> to format with. A lang entry
        /// with no hole in it is unaffected by the second case, so this is a
        /// necessary-not-sufficient guard: it is the cheap always-safe assertion, and a
        /// caller that knows its entry uses <c>{0}</c> should assert on that toast's
        /// <see cref="IngameError.LangParams"/> directly.
        /// </summary>
        public bool NoUnsubstitutedPlaceholders()
            => !Errors.Any(e => e.HasUnsubstitutedPlaceholder);

        /// <summary>At least one toast carries display text from the server, verbatim.</summary>
        public bool HasServerTextError(string code)
            => ErrorsWithCode(code).Any(e => e.HasServerText);

        /// <summary>At least one toast with this code is client-resolved and carries
        /// display text the server resolved for the player's locale - i.e. it is not the
        /// localize-on-the-client shape vanilla uses.</summary>
        public bool HasClientResolvedError(string code)
            => ErrorsWithCode(code).Any(e => e.IsClientResolved);

        /// <summary>
        /// The toast the player reads for <paramref name="code"/>, whichever of the two
        /// shapes it arrived in: server text when present, otherwise the lang entry
        /// <c>ingameerror-&lt;code&gt;</c> filled with its params - the same composition
        /// <c>HandleIngameError</c> performs. Lets a scenario assert on wording instead of
        /// on wire shape. Returns null for an absent toast.
        /// </summary>
        public string? RenderedText(string code, Func<string, string[], string> resolveLang)
        {
            var error = ErrorsWithCode(code).FirstOrDefault();
            if (error is null) return null;
            if (error.HasServerText) return error.Message;

            var entry = resolveLang($"ingameerror-{error.Code}", error.LangParams.ToArray());
            return error.LangParams.Count == 0
                ? entry
                : TryFormat(entry, error.LangParams);
        }

        /// <summary>
        /// <c>string.Format</c> with the engine's failure semantics: on any exception
        /// <c>TranslationService.TryFormat</c> returns the raw value, so a hole that has no
        /// argument renders literally rather than throwing at the assertion site. Mirrors
        /// that so a scenario can assert on the same text the HUD would show, including
        /// the degenerate case.
        /// </summary>
        private static string TryFormat(string format, IReadOnlyList<string> args)
        {
            try
            {
                return string.Format(format, args.Cast<object>().ToArray());
            }
            catch (FormatException)
            {
                return format;
            }
        }

        /// <summary>Human-readable dump for assertion failure messages.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"vanilla wire: {BlockUpdates.Count} block updates, ")
              .Append($"{BlockEntityUpdates.Count} BE updates, ")
              .Append($"{Errors.Count} errors, {OtherPackets} other");
            foreach (var update in BlockUpdates)
                sb.Append($"\n  ExchangeBlock {update.Pos} -> {update.BlockId}");
            foreach (var update in BlockEntityUpdates)
                sb.Append(update.IsRemoval
                    ? $"\n  BlockEntity {update.Pos} -> removal (no server BE)"
                    : $"\n  BlockEntity {update.Pos} -> {update.Classname}");
            foreach (var error in Errors)
                sb.Append($"\n  IngameError '{error.Code}'")
                  // Show what the player reads, and which of the two shapes delivered it -
                  // a failing assertion about wording is otherwise undebuggable, since the
                  // code alone never carried the text.
                  .Append(error.HasServerText
                      ? $" serverText=\"{error.Message}\""
                      : $" clientResolved langParams=[{string.Join(", ", error.LangParams.Select(p => $"\"{p}\""))}]")
                  .Append(error.HasUnsubstitutedPlaceholder ? " RAW-{0}-RISK" : string.Empty);
            return sb.ToString();
        }

        private static DummyTcpNetClient ResolveClient(ITestPlayer player)
        {
            var observations = player.Client;
            clientField ??= observations.GetType().GetField("_client", InstancePrivate)
                ?? throw new InvalidOperationException(
                    "Atlas ClientObservations._client field not found - Atlas internals "
                    + "changed; update VanillaClientWire (expected private field '_client' "
                    + "on the concrete IClientObservations implementation).");

            if (clientField.GetValue(observations) is not DummyTcpNetClient dummyClient)
            {
                throw new InvalidOperationException(
                    "Atlas ClientObservations._client is not a DummyTcpNetClient - the "
                    + "harness's player connection mechanism changed; update VanillaClientWire.");
            }

            return dummyClient;
        }
    }
}
