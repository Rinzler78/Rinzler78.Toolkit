using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text.RegularExpressions;
using System.Reflection.PortableExecutable;
using Microsoft.Build.Framework;

namespace Rinzler78.Build.Tasks;

/// <summary>
/// Reads a compiled assembly's metadata and fails the build on what its policy forbids: a
/// reference to an assembly its layer may not depend on, and a bare <c>double</c> in the
/// public API of a layer that owns its surface. The assembly is never loaded, so it can
/// target a platform the build machine does not run.
/// </summary>
public sealed class InspectAssembly : Microsoft.Build.Utilities.Task
{
    private static readonly Regex BareDouble = new(@"(?<![.\w])double(?![\w.])", RegexOptions.CultureInvariant);

    /// <summary>Gets or sets the compiled assembly to inspect.</summary>
    [Required]
    public string Assembly { get; set; } = string.Empty;

#pragma warning disable CA1819 // MSBuild passes an item list to a task as an array: the contract fixes the type.
    /// <summary>Gets or sets the assembly names the inspected assembly may not reference.</summary>
    public ITaskItem[] ForbiddenReferences { get; set; } = [];

    /// <summary>Gets or sets the types allowed to take a bare double, by full name; their nested types are too.</summary>
    public ITaskItem[] AllowBareDouble { get; set; } = [];

    /// <summary>Gets or sets the namespaces whose types are allowed to take a bare double.</summary>
    public ITaskItem[] AllowBareDoubleNamespace { get; set; } = [];
#pragma warning restore CA1819

    /// <summary>Gets or sets a value indicating whether a bare double in the public API fails the build.</summary>
    public bool ForbidBareDouble { get; set; }

    /// <inheritdoc />
    public override bool Execute()
    {
        using var stream = File.OpenRead(Assembly);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            return true;
        }

        var reader = pe.GetMetadataReader();
        var name = reader.GetString(reader.GetAssemblyDefinition().Name);

        CheckReferences(reader, name);
        if (ForbidBareDouble)
        {
            CheckBareDouble(reader, name);
        }

        return !Log.HasLoggedErrors;
    }

    // A decoded type is a name tree written as text — `List<double>`, `double[]`, `ValueTuple<double,double>`,
    // `double?` — so a double anywhere in it is a bare double in the signature.
    private static bool IsDouble(string type) => BareDouble.IsMatch(type);

    private static bool IsVisible(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;

    private static bool IsVisible(FieldAttributes attributes) =>
        (attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;

    // A type is part of the public API when it, and every type enclosing it, can be seen from outside.
    private static bool IsExposed(MetadataReader reader, TypeDefinition type)
    {
        while (true)
        {
            var visibility = type.Attributes & TypeAttributes.VisibilityMask;
            if (visibility == TypeAttributes.Public)
            {
                return true;
            }

            if (visibility is not (TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem))
            {
                return false;
            }

            type = reader.GetTypeDefinition(type.GetDeclaringType());
        }
    }

    private static string FullName(MetadataReader reader, TypeDefinition type)
    {
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return $"{FullName(reader, reader.GetTypeDefinition(declaring))}.{name}";
        }

        var space = reader.GetString(type.Namespace);
        return space.Length == 0 ? name : $"{space}.{name}";
    }

    // An allowance names the outermost type, and its nested types inherit it; a namespace
    // allowance covers every type under it. A wildcard cannot be the syntax: MSBuild would
    // read an item with an asterisk as a file pattern.
    private static bool IsAllowed(string typeName, IEnumerable<string> types, IEnumerable<string> namespaces) =>
        types.Any(entry => typeName == entry || typeName.StartsWith(entry + ".", StringComparison.Ordinal))
        || namespaces.Any(entry => typeName.StartsWith(entry + ".", StringComparison.Ordinal));

    private static IEnumerable<string> Decode(MetadataReader reader, BlobHandle signature, NameProvider provider)
    {
        var blob = reader.GetBlobReader(signature);
        var decoded = new SignatureDecoder<string, object?>(provider, reader, null).DecodeMethodSignature(ref blob);
        return decoded.ParameterTypes.Prepend(decoded.ReturnType);
    }

    private void CheckReferences(MetadataReader reader, string name)
    {
        var forbidden = new HashSet<string>(
            ForbiddenReferences.Select(item => item.ItemSpec.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var handle in reader.AssemblyReferences)
        {
            var referenced = reader.GetString(reader.GetAssemblyReference(handle).Name);
            if (forbidden.Contains(referenced))
            {
                Log.LogError(
                    "{0} references {1}, which its layer may not depend on. Source dependencies point inward only.",
                    name,
                    referenced);
            }
        }
    }

    private void CheckBareDouble(MetadataReader reader, string name)
    {
        var allowedTypes = AllowBareDouble.Select(item => item.ItemSpec.Trim()).ToList();
        var allowedNamespaces = AllowBareDoubleNamespace.Select(item => item.ItemSpec.Trim()).ToList();
        var provider = new NameProvider();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (!IsExposed(reader, type))
            {
                continue;
            }

            var typeName = FullName(reader, type);
            if (IsAllowed(typeName, allowedTypes, allowedNamespaces))
            {
                continue;
            }

            var accessors = new HashSet<MethodDefinitionHandle>();
            foreach (var handle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(handle);
                var pair = property.GetAccessors();
                accessors.UnionWith(new[] { pair.Getter, pair.Setter }.Where(accessor => !accessor.IsNil));
                var visible = new[] { pair.Getter, pair.Setter }.Any(accessor =>
                    !accessor.IsNil && IsVisible(reader.GetMethodDefinition(accessor).Attributes));
                if (visible && Decode(reader, property.Signature, provider).Any(IsDouble))
                {
                    Report(name, "property", typeName, reader.GetString(property.Name));
                }
            }

            foreach (var handle in type.GetEvents())
            {
                var definition = reader.GetEventDefinition(handle);
                var pair = definition.GetAccessors();
                accessors.UnionWith(new[] { pair.Adder, pair.Remover, pair.Raiser }.Where(accessor => !accessor.IsNil));
                var visible = new[] { pair.Adder, pair.Remover }.Any(accessor =>
                    !accessor.IsNil && IsVisible(reader.GetMethodDefinition(accessor).Attributes));
                if (visible && IsDouble(provider.GetTypeFromHandle(reader, definition.Type)))
                {
                    Report(name, "event", typeName, reader.GetString(definition.Name));
                }
            }

            foreach (var handle in type.GetMethods().Where(handle => !accessors.Contains(handle)))
            {
                var method = reader.GetMethodDefinition(handle);
                if (IsVisible(method.Attributes) && Decode(reader, method.Signature, provider).Any(IsDouble))
                {
                    Report(name, "method", typeName, reader.GetString(method.Name));
                }
            }

            foreach (var handle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(handle);
                if (IsVisible(field.Attributes) && IsDouble(field.DecodeSignature(provider, null)))
                {
                    Report(name, "field", typeName, reader.GetString(field.Name));
                }
            }
        }
    }

    private void Report(string assemblyName, string kind, string typeName, string member) =>
        Log.LogError(
            "{0}: the public API exposes a bare double in the {1} {2}.{3}. A layer that owns its surface takes a value type, never a bare double.",
            assemblyName,
            kind,
            typeName,
            member);

    // Names are all the check needs: a primitive's keyword and a type's full name, composed
    // with their generic arguments, array elements and by-reference marks.
    private sealed class NameProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetTypeFromHandle(MetadataReader reader, EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(reader, null, (TypeSpecificationHandle)handle, 0),
            _ => string.Empty,
        };

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            typeCode == PrimitiveTypeCode.Double ? "double" : typeCode.ToString();

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            FullName(reader, reader.GetTypeDefinition(handle));

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            var space = reader.GetString(reference.Namespace);
            var name = reader.GetString(reference.Name);
            return space.Length == 0 ? name : $"{space}.{name}";
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType == "System.Nullable`1" && typeArguments.Length == 1
                ? $"{typeArguments[0]}?"
                : $"{genericType}<{string.Join(",", typeArguments)}>";

        public string GetByReferenceType(string elementType) => $"{elementType}&";

        public string GetSZArrayType(string elementType) => $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[]";

        public string GetPointerType(string elementType) => $"{elementType}*";

        public string GetFunctionPointerType(MethodSignature<string> signature) =>
            $"fnptr({string.Join(",", signature.ParameterTypes.Prepend(signature.ReturnType))})";

        public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

        public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

        public string GetPinnedType(string elementType) => elementType;
    }
}
