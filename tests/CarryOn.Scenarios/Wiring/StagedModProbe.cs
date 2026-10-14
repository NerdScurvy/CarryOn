using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Xunit;

namespace CarryOn.Scenarios.Wiring
{
    /// <summary>
    /// Shared primitives for the plain metadata/IL wiring pins in this folder.
    ///
    /// <para>Every routine here reads the staged <c>CarryOn.dll</c> file with
    /// <c>PEReader</c>/<c>MetadataReader</c> and <b>never loads an assembly</b> - loading
    /// <c>CarryOn.dll</c> (or anything it references) poisons the Atlas host's per-install
    /// preflight, so a later version sweep fails on every engine install but the build
    /// target. See <c>CarryabilitySyncWiringTests</c> for the incident that established the
    /// rule.</para>
    ///
    /// <para>These are honest <i>wiring</i> pins: they prove the shape of a fix - that a
    /// method calls something, or that a branch exists - not its runtime behavior. A row
    /// built on this helper must say so in its <c>summary</c>/<c>caveat</c>.</para>
    /// </summary>
    internal static class StagedModProbe
    {
        /// <summary>One decoded IL instruction. <see cref="Member"/> is set for
        /// <c>call</c>/<c>callvirt</c>/<c>newobj</c>/<c>ldftn</c> operands;
        /// <see cref="TypeName"/> is set for <c>isinst</c>/<c>castclass</c>/<c>ldtoken</c>
        /// type operands. <see cref="Operand"/> carries the raw token for those, or the
        /// absolute target offset for a branch.</summary>
        internal readonly struct Instruction
        {
            internal Instruction(int offset, OpCode opCode, string? member, string? typeName, bool memberDefinedHere, int operand)
            {
                Offset = offset;
                OpCode = opCode;
                Member = member;
                TypeName = typeName;
                MemberDefinedHere = memberDefinedHere;
                Operand = operand;
            }

            public int Offset { get; }
            public OpCode OpCode { get; }
            public string? Member { get; }
            public string? TypeName { get; }
            public bool MemberDefinedHere { get; }
            public int Operand { get; }
        }

        private static readonly OpCode[] OneByteOpcodes = BuildOpcodeTable(oneByte: true);
        private static readonly OpCode[] TwoByteOpcodes = BuildOpcodeTable(oneByte: false);

        private static OpCode[] BuildOpcodeTable(bool oneByte)
        {
            var table = new OpCode[0x100];
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode opcode) continue;
                if ((opcode.Size == 1) == oneByte) table[opcode.Value & 0xFF] = opcode;
            }

            return table;
        }

        /// <summary>
        /// The staged mod assembly, found by walking up from this test assembly's own
        /// location - <b>not</b> from <c>AppContext.BaseDirectory</c>, which the Atlas host
        /// points at the VintageStory install when it boots servers, so a base-directory walk
        /// finds no repo ancestor in a full-suite run. The assembly location is stable in
        /// both a filtered and a full-suite run.
        /// </summary>
        internal static string ModAssemblyPath()
        {
            foreach (var directory in ProbeDirectories())
            {
                var candidate = Path.Combine(directory, "CarryOn.dll");
                if (File.Exists(candidate)) return candidate;
            }

            throw new FileNotFoundException(
                "CarryOn.dll not found. Build the mod in Release (run-tests.ps1 does this "
                + $"first) and re-run. Probed: {string.Join("; ", ProbeDirectories())}.");
        }

        private static IEnumerable<string> ProbeDirectories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var anchor in new[]
            {
                typeof(StagedModProbe).Assembly.Location,
                AppContext.BaseDirectory,
            })
            {
                if (string.IsNullOrEmpty(anchor)) continue;

                for (var dir = new DirectoryInfo(Path.GetDirectoryName(anchor)!); dir != null; dir = dir.Parent)
                {
                    foreach (var relative in new[]
                    {
                        Path.Combine("bin", "Release", "net10.0"),
                        Path.Combine("CarryOn", "bin", "Release", "net10.0"),
                        Path.Combine("CarryOnLib", "bin", "Release", "net10.0"),
                    })
                    {
                        var candidate = Path.GetFullPath(Path.Combine(dir.FullName, relative));
                        if (seen.Add(candidate) && Directory.Exists(candidate)) yield return candidate;
                    }
                }
            }
        }

        /// <summary>Finds a method by declaring type full name and method name. Namespace
        /// and type name are matched separately so a nested or renamed type fails loudly
        /// instead of silently matching nothing.</summary>
        internal static MethodDefinition? FindMethod(MetadataReader reader, string typeFullName, string methodName)
        {
            var type = FindType(reader, typeFullName);
            if (type == null) return null;

            foreach (var methodHandle in type.Value.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (string.Equals(reader.GetString(method.Name), methodName, StringComparison.Ordinal))
                {
                    return method;
                }
            }

            return null;
        }

        internal static TypeDefinition? FindType(MetadataReader reader, string typeFullName)
        {
            var dot = typeFullName.LastIndexOf('.');
            var typeNamespace = dot < 0 ? "" : typeFullName[..dot];
            var typeName = typeFullName[(dot + 1)..];

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(typeHandle);
                if (string.Equals(reader.GetString(type.Namespace), typeNamespace, StringComparison.Ordinal)
                    && string.Equals(reader.GetString(type.Name), typeName, StringComparison.Ordinal))
                {
                    return type;
                }
            }

            return null;
        }

        internal static MethodDefinition RequireMethod(MetadataReader reader, string typeFullName, string methodName)
        {
            var method = FindMethod(reader, typeFullName, methodName);
            Assert.True(
                method.HasValue,
                $"{typeFullName}.{methodName}() not found in the staged mod assembly - if it "
                + "was renamed or inlined away, this pin must be re-derived against the new "
                + "call site rather than deleted");
            return method!.Value;
        }

        internal static byte[] RequireIl(PEReader peReader, MethodDefinition method)
        {
            Assert.True(
                method.RelativeVirtualAddress != 0,
                "the target method has no IL body - an abstract or extern method cannot carry "
                + "the call or branch this pin is about");

            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            Assert.True(
                body != null,
                "the target method has no method body - it cannot carry the call or branch "
                + "this pin is about");

            var il = body!.GetILBytes();
            Assert.True(
                il != null && il.Length > 0,
                "the target method has no IL bytes - an empty body cannot carry the call or "
                + "branch this pin is about");
            return il!;
        }

        private static bool TryGetIl(PEReader peReader, MethodDefinition method, out byte[] il)
        {
            il = Array.Empty<byte>();
            if (method.RelativeVirtualAddress == 0) return false;

            var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
            var bytes = body?.GetILBytes();
            if (bytes == null || bytes.Length == 0) return false;

            il = bytes;
            return true;
        }

        /// <summary>Decodes every IL instruction in a method body. Never throws on a
        /// malformed opcode: an unknown byte is reported with a zero opcode and skipped.</summary>
        internal static List<Instruction> Decode(MetadataReader reader, byte[] il)
        {
            var instructions = new List<Instruction>();
            var position = 0;

            while (position < il.Length)
            {
                var offset = position;
                var first = il[position++];
                var opcode = first == 0xFE
                    ? (position < il.Length ? TwoByteOpcodes[il[position++]] : default)
                    : OneByteOpcodes[first];

                if (opcode.Size == 0)
                {
                    instructions.Add(new Instruction(offset, opcode, null, null, false, 0));
                    continue;
                }

                string? member = null;
                string? typeName = null;
                var memberDefinedHere = false;
                var operand = 0;

                switch (opcode.OperandType)
                {
                    case OperandType.InlineMethod:
                        if (position + sizeof(int) > il.Length) { instructions.Add(new Instruction(offset, opcode, null, null, false, 0)); return instructions; }
                        operand = BitConverter.ToInt32(il, position);
                        position += sizeof(int);
                        member = ResolveMethodToken(reader, operand, out memberDefinedHere);
                        break;
                    case OperandType.InlineType:
                        if (position + sizeof(int) > il.Length) { instructions.Add(new Instruction(offset, opcode, null, null, false, 0)); return instructions; }
                        operand = BitConverter.ToInt32(il, position);
                        position += sizeof(int);
                        typeName = ResolveTypeToken(reader, operand);
                        break;
                    case OperandType.ShortInlineBrTarget:
                        if (position + 1 > il.Length) { instructions.Add(new Instruction(offset, opcode, null, null, false, 0)); return instructions; }
                        operand = position + 1 + (sbyte)il[position];
                        position += 1;
                        break;
                    case OperandType.InlineBrTarget:
                        if (position + sizeof(int) > il.Length) { instructions.Add(new Instruction(offset, opcode, null, null, false, 0)); return instructions; }
                        operand = position + sizeof(int) + BitConverter.ToInt32(il, position);
                        position += sizeof(int);
                        break;
                    default:
                        if (opcode.OperandType == OperandType.InlineSwitch)
                        {
                            if (position + sizeof(int) > il.Length) { instructions.Add(new Instruction(offset, opcode, null, null, false, 0)); return instructions; }
                            var count = BitConverter.ToInt32(il, position);
                            position += sizeof(int) + (count * sizeof(int));
                        }
                        else
                        {
                            position += OperandSize(opcode.OperandType);
                        }
                        break;
                }

                instructions.Add(new Instruction(offset, opcode, member, typeName, memberDefinedHere, operand));
            }

            return instructions;
        }

        internal static byte[] IlOf(MetadataReader reader, PEReader peReader, string typeFullName, string methodName)
            => RequireIl(peReader, RequireMethod(reader, typeFullName, methodName));

        /// <summary>Every member a method calls, deduplicated, resolved from metadata.</summary>
        internal static HashSet<string> CallsFrom(
            MetadataReader reader, PEReader peReader, string typeFullName, string methodName)
        {
            var il = IlOf(reader, peReader, typeFullName, methodName);
            var calls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instruction in Decode(reader, il))
            {
                if (instruction.Member != null) calls.Add(instruction.Member);
            }

            return calls;
        }

        /// <summary>Every member a method calls, in order, with duplicates - so a caller can
        /// count how many times a specific call appears.</summary>
        internal static List<string> CallList(MetadataReader reader, byte[] il)
        {
            var calls = new List<string>();
            foreach (var instruction in Decode(reader, il))
            {
                if (instruction.Member != null) calls.Add(instruction.Member);
            }

            return calls;
        }

        /// <summary>The members called from the IL window [offset, offset+length), resolved
        /// from metadata. Exception-region offsets are method-relative and the IL byte array
        /// is method-relative, so the two line up for a method body - exactly the granularity
        /// a handler is expressed in.</summary>
        internal static HashSet<string> CallsWithinRegion(MetadataReader reader, byte[] il, int offset, int length)
        {
            if (offset <= 0 || length <= 0 || offset >= il.Length) return new HashSet<string>(StringComparer.Ordinal);

            var end = Math.Min(il.Length, offset + length);
            var calls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instruction in Decode(reader, il))
            {
                if (instruction.Offset < offset || instruction.Offset >= end) continue;
                if (instruction.Member != null) calls.Add(instruction.Member);
            }

            return calls;
        }

        /// <summary>
        /// Whether <paramref name="targetMember"/> is reachable from the named method by
        /// following calls - including calls that land on a same-assembly method, which are
        /// followed in turn. Calls into other assemblies are recorded but not expanded, so a
        /// fix may place the disposal/invalidation directly, behind one local helper, or
        /// behind a small chain; a fix that only moves the call into an external assembly is
        /// out of reach and would have to be re-derived.
        /// </summary>
        internal static bool ReachesMember(
            MetadataReader reader, PEReader peReader, string typeFullName, string methodName,
            string targetMember, int maxDepth = 6)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<(string Type, string Method, int Depth)>();
            queue.Enqueue((typeFullName, methodName, 0));

            while (queue.Count > 0)
            {
                var (type, method, depth) = queue.Dequeue();
                var key = type + "." + method;
                if (!visited.Add(key)) continue;

                var definition = FindMethod(reader, type, method);
                if (definition == null || !TryGetIl(peReader, definition.Value, out var il)) continue;

                foreach (var instruction in Decode(reader, il))
                {
                    if (instruction.Member == null) continue;
                    if (string.Equals(instruction.Member, targetMember, StringComparison.Ordinal)) return true;

                    if (depth < maxDepth && instruction.MemberDefinedHere)
                    {
                        var split = instruction.Member.LastIndexOf('.');
                        if (split > 0)
                        {
                            queue.Enqueue((
                                instruction.Member[..split],
                                instruction.Member[(split + 1)..],
                                depth + 1));
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a property (or its compiler-generated backing field) carries a
        /// <c>ProtoBuf.ProtoMemberAttribute</c>, and whether that attribute sets
        /// <c>IsRequired = true</c>. The named-argument blob is decoded directly: the
        /// attribute's <c>IsRequired</c> is a property, so it lives in the custom-attribute
        /// blob's named-argument list, not in a constructor signature. A caller should
        /// validate the reader against a known-good property (a premise row) before trusting
        /// a negative.
        /// </summary>
        internal static bool TryGetProtoMember(
            MetadataReader reader, string typeFullName, string propertyName,
            out bool hasProtoMember, out bool isRequired)
        {
            hasProtoMember = false;
            isRequired = false;

            var type = FindType(reader, typeFullName);
            if (type == null) return false;

            var foundMember = false;
            var foundRequired = false;

            foreach (var propertyHandle in type.Value.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                if (!string.Equals(reader.GetString(property.Name), propertyName, StringComparison.Ordinal)) continue;
                Scan(property.GetCustomAttributes());
            }

            var backingFieldName = "<" + propertyName + ">k__BackingField";
            foreach (var fieldHandle in type.Value.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if (!string.Equals(reader.GetString(field.Name), backingFieldName, StringComparison.Ordinal)) continue;
                Scan(field.GetCustomAttributes());
            }

            hasProtoMember = foundMember;
            isRequired = foundRequired;
            return true;

            void Scan(CustomAttributeHandleCollection attributes)
            {
                foreach (var attributeHandle in attributes)
                {
                    var attribute = reader.GetCustomAttribute(attributeHandle);
                    if (!string.Equals(AttributeTypeName(reader, attribute.Constructor), "ProtoBuf.ProtoMemberAttribute", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foundMember = true;
                    if (TryParseIsRequired(reader.GetBlobBytes(attribute.Value), out var namedRequired) && namedRequired)
                    {
                        foundRequired = true;
                    }
                }
            }
        }

        private static string? AttributeTypeName(MetadataReader reader, EntityHandle constructor)
        {
            switch (constructor.Kind)
            {
                case HandleKind.MethodDefinition:
                    return TypeName(reader, reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
                case HandleKind.MemberReference:
                    return TypeName(reader, reader.GetMemberReference((MemberReferenceHandle)constructor).Parent);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Parses a custom-attribute value blob to find the named-argument
        /// <c>IsRequired = true</c>. The fixed-argument prefix (a single <c>int tag</c>) makes
        /// the named-argument table start at a known offset; the two candidates cover an
        /// attribute constructor with one int fixed arg and one with int+bool.
        /// </summary>
        private static bool TryParseIsRequired(byte[] blob, out bool isRequired)
        {
            isRequired = false;
            if (blob.Length < 3 || blob[0] != 0x01 || blob[1] != 0x00) return false;

            foreach (var start in new[] { 2 + sizeof(int), 2 + sizeof(int) + 1 })
            {
                if (start + 2 > blob.Length) continue;

                var namedCount = blob[start] | (blob[start + 1] << 8);
                if (namedCount < 1 || namedCount > 64) continue;

                var position = start + 2;
                var ok = true;
                for (var i = 0; i < namedCount && ok; i++)
                {
                    if (position + 2 > blob.Length) { ok = false; break; }

                    var marker = blob[position++];
                    if (marker != 0x53 && marker != 0x54) { ok = false; break; } // field / property

                    var elementType = blob[position++];
                    if (!TryReadSerString(blob, ref position, out var name)) { ok = false; break; }

                    var size = ElementSize(elementType);
                    if (elementType == 0x0E) // string: variable length
                    {
                        if (!TryReadSerString(blob, ref position, out _)) { ok = false; break; }
                    }
                    else if (size < 0 || position + size > blob.Length)
                    {
                        ok = false;
                        break;
                    }
                    else
                    {
                        if (string.Equals(name, "IsRequired", StringComparison.Ordinal) && elementType == 0x02)
                        {
                            isRequired = blob[position] != 0;
                        }

                        position += size;
                    }
                }

                if (ok && position == blob.Length) return true;
            }

            return false;
        }

        private static int ElementSize(byte elementType) => elementType switch
        {
            0x02 => 1, // bool
            0x03 => 2, // char
            0x04 => 1, // int8/uint8
            0x05 => 1,
            0x06 => 2, // int16
            0x07 => 2,
            0x08 => 4, // int32/uint32 (and int-based enums)
            0x09 => 4,
            0x0A => 8, // int64
            0x0B => 8,
            0x0C => 4, // float
            0x0D => 8, // double
            0x0E => -1, // string (handled by the caller)
            _ => -1,
        };

        private static bool TryReadSerString(byte[] blob, ref int position, out string value)
        {
            value = string.Empty;
            if (position >= blob.Length) return false;

            int length;
            var first = blob[position];
            if ((first & 0x80) == 0) { length = first; position += 1; }
            else if ((first & 0xC0) == 0x80)
            {
                if (position + 1 >= blob.Length) return false;
                length = ((first & 0x3F) << 8) | blob[position + 1];
                position += 2;
            }
            else if ((first & 0xE0) == 0xC0)
            {
                if (position + 3 >= blob.Length) return false;
                length = ((first & 0x1F) << 24) | (blob[position + 1] << 16) | (blob[position + 2] << 8) | blob[position + 3];
                position += 4;
            }
            else
            {
                return false;
            }

            if (length < 0 || position + length > blob.Length) return false;
            value = System.Text.Encoding.UTF8.GetString(blob, position, length);
            position += length;
            return true;
        }

        private static string? ResolveMethodToken(MetadataReader reader, int token, out bool definedHere)
        {
            definedHere = false;
            var row = token & 0x00FFFFFF;
            switch (token >> 24)
            {
                case 0x06: // MethodDef (this assembly)
                {
                    definedHere = true;
                    var method = reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row));
                    var declaring = TypeName(reader, method.GetDeclaringType());
                    return declaring == null ? null : $"{declaring}.{reader.GetString(method.Name)}";
                }
                case 0x0A: // MemberRef (another assembly)
                {
                    var member = reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(row));
                    var declaring = TypeName(reader, member.Parent);
                    return declaring == null ? null : $"{declaring}.{reader.GetString(member.Name)}";
                }
                default:
                    return null;
            }
        }

        private static string? ResolveTypeToken(MetadataReader reader, int token)
        {
            var row = token & 0x00FFFFFF;
            switch (token >> 24)
            {
                case 0x01: // TypeRef
                    return TypeName(reader, MetadataTokens.TypeReferenceHandle(row));
                case 0x02: // TypeDef
                    return TypeName(reader, MetadataTokens.TypeDefinitionHandle(row));
                default: // TypeSpec (generic) - not a plain named type
                    return null;
            }
        }

        /// <summary>The full name of a named type handle, or null when it is not a named
        /// type (a generic <c>TypeSpec</c>) - the caller reports those as unresolvable
        /// rather than guessing at a signature.</summary>
        internal static string? TypeName(MetadataReader reader, EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                {
                    var type = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    return Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
                }
                case HandleKind.TypeReference:
                {
                    var type = reader.GetTypeReference((TypeReferenceHandle)handle);
                    return Join(reader.GetString(type.Namespace), reader.GetString(type.Name));
                }
                default:
                    return null;
            }
        }

        private static string Join(string ns, string name) => string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

        private static int OperandSize(OperandType operandType) => operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget
                or OperandType.ShortInlineI
                or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8
                or OperandType.InlineR => 8,
            _ => sizeof(int), // InlineBrTarget, InlineField, InlineI, InlineSig,
                              // InlineString, InlineTok, InlineType, ShortInlineR
        };
    }
}
