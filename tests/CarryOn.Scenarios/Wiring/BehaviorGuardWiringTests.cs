using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace CarryOn.Scenarios.Wiring
{
    /// <summary>
    /// Plain xUnit wiring pins for a client validator that dereferences the result of an
    /// <c>as</c> cast without a null check. The validator is client-side and runs on the
    /// client tick, so the Atlas host (a headless dedicated server) can never execute it -
    /// the behavioral half is a manual real-client gate. These rows pin the missing guard's
    /// IL shape only: after the <c>isinst</c> that produces the reference, a conditional
    /// branch must occur before the reference is first consumed. That shape is shared by both
    /// idiomatic fixes - an explicit <c>if (x == null) return</c> and an <c>is</c> pattern -
    /// so the pin does not over-fit one of them.
    ///
    /// <para>Both rows read the staged <c>CarryOn.dll</c> with
    /// <c>PEReader</c>/<c>MetadataReader</c> and load no assembly - loading it poisons the
    /// Atlas host's per-install preflight and breaks the version sweep. See
    /// <c>CarryabilitySyncWiringTests</c>.</para>
    /// </summary>
    [Trait("area", "Client/Interaction")]
    [Trait("summary", "Plain xUnit wiring pins: each row reads the staged CarryOn.dll IL and asserts a null guard branches between an as-cast and its first use in a ground-storage validator, with no server boot and no client tick executed.")]
    public class BehaviorGuardWiringTests
    {
        private const string ValidatorTypeName = "CarryOn.Client.Logic.Interaction.CarryInteractionValidator";
        private const string GroundStorageEntitySuffix = "BlockEntityGroundStorage";

        /// <summary>
        /// Authored red-first. The shared validator is reached on the client tick while a
        /// block entity can be removed between the guard and the cast; dereferencing the
        /// unguarded result throws.
        /// </summary>
        [Trait("summary", "Each ground-storage bag validator must null-check its as-cast BlockEntityGroundStorage before dereferencing it.")]
        [Trait("pins", "A null block entity cannot reach the dereference in the ground-storage bag validators.")]
        [Trait("role", "contract")]
        [Trait("redfirst", "true")]
        [Trait("evidence", "Failed on the unfixed head with no conditional branch between the isinst and the first call that consumes the cast reference in all three validator methods.")]
        [Trait("caveat", "Wiring only - proves the guard's IL shape, not its runtime behavior; the validator is client-only and never executes in this headless suite.")]
        [Theory]
        [InlineData("IsValidGroundStorageBagTarget")]
        [InlineData("IsValidGroundStorageBagPickupTarget")]
        [InlineData("BeginGroundStorageBagPickupInteraction")]
        public void GroundStorageBagValidator_NullChecksTheCastBeforeUse(string methodName)
        {
            using var peReader = new PEReader(File.OpenRead(StagedModProbe.ModAssemblyPath()));
            var reader = peReader.GetMetadataReader();

            var il = StagedModProbe.IlOf(reader, peReader, ValidatorTypeName, methodName);
            var instructions = StagedModProbe.Decode(reader, il);

            var castIndex = LastIndexOfGroundStorageCast(instructions);
            Assert.True(
                castIndex >= 0,
                $"{ValidatorTypeName}.{methodName}() has no 'as {GroundStorageEntitySuffix}' cast "
                + "to guard - if the method was restructured, re-derive this pin rather than "
                + "delete it.");

            var firstUseIndex = FirstCallAfter(instructions, castIndex);
            Assert.True(
                firstUseIndex > castIndex,
                $"{ValidatorTypeName}.{methodName}() casts to {GroundStorageEntitySuffix} but "
                + "never consumes the result, so this pin cannot express the defect. Re-derive "
                + "it against the method that dereferences the cast.");

            var guarded = instructions
                .Skip(castIndex + 1)
                .Take(firstUseIndex - (castIndex + 1))
                .Any(instruction => instruction.OpCode.FlowControl == FlowControl.Cond_Branch);

            Assert.True(
                guarded,
                $"{ValidatorTypeName}.{methodName}() must null-check the {GroundStorageEntitySuffix} "
                + "it casts to before dereferencing it. The cast is an 'as', which yields null for "
                + "a missing or non-matching block entity (a block removed between the guard and "
                + "the cast, or a modded block-entity swap on the client tick); the immediate "
                + "dereference then throws. Add a guard branch between the cast and the first use.");
        }

        private static int LastIndexOfGroundStorageCast(IReadOnlyList<StagedModProbe.Instruction> instructions)
        {
            var index = -1;
            for (var i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                if (instruction.OpCode != OpCodes.Isinst) continue;
                if (instruction.TypeName?.EndsWith(GroundStorageEntitySuffix, StringComparison.Ordinal) != true) continue;
                index = i;
            }

            return index;
        }

        private static int FirstCallAfter(IReadOnlyList<StagedModProbe.Instruction> instructions, int castIndex)
        {
            for (var i = castIndex + 1; i < instructions.Count; i++)
            {
                if (instructions[i].OpCode.FlowControl == FlowControl.Call) return i;
            }

            return -1;
        }
    }
}
