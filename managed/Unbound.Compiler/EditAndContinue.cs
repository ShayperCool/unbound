using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace Unbound.Compiler;

/// <summary>The metadata, IL and PDB deltas of one assembly's edit, for MetadataUpdater.ApplyUpdate.</summary>
public sealed class AssemblyDelta
{
    public string Name { get; set; } = "";
    public byte[] Metadata { get; set; } = Array.Empty<byte>();
    public byte[] IL { get; set; } = Array.Empty<byte>();
    public byte[] Pdb { get; set; } = Array.Empty<byte>();
    public int Edits { get; set; }
    public List<string> Changed { get; set; } = new();
}

/// <summary>
/// Edit and Continue for a running game: the difference between the compilation of the assembly
/// the game loaded (or the last applied edit) and the current one, as semantic edits per member,
/// emitted as deltas the runtime applies in place. Edits the runtime cannot apply (a changed base
/// type or field type, ...) are refused with their reason so that the caller restarts instead.
/// </summary>
public sealed class EditAndContinue
{
    sealed class Session
    {
        public required CSharpCompilation Compilation;
        public required EmitBaseline Baseline;
        public int Generation;
    }

    readonly Dictionary<string, Session> _sessions = new();

    /// <summary>Forgets every session (after the game's code was reloaded from new outputs).</summary>
    public void Reset() => _sessions.Clear();

    public bool HasSession(string assembly) => _sessions.ContainsKey(assembly);

    /// <summary>Starts a session from the assembly the game loaded: its image and PDB, and the
    /// compilation that produced them.</summary>
    public void Begin(string assembly, CSharpCompilation loadedCompilation, byte[] dll, byte[] pdb)
    {
        var module = ModuleMetadata.CreateFromImage(dll);
        var pdbProvider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(pdb));
        var pdbReader = pdbProvider.GetMetadataReader();
        var peReader = new PEReader(ImmutableArray.Create(dll));
        var metadata = peReader.GetMetadataReader();
        var baseline = EmitBaseline.CreateInitialBaseline(
            loadedCompilation,
            module,
            handle => DebugInformation(pdbReader, handle),
            handle =>
            {
                var body = metadata.GetMethodDefinition(handle).RelativeVirtualAddress;
                if (body == 0) return default;
                return peReader.GetMethodBody(body).LocalSignature;
            },
            hasPortableDebugInformation: true);
        _sessions[assembly] = new Session { Compilation = loadedCompilation, Baseline = baseline };
    }

    static readonly Guid EncLocalSlotMap = new("755F52A8-91C5-45BE-B4B8-209571E552BD");
    static readonly Guid EncLambdaAndClosureMap = new("A643004C-0240-496F-A783-30D64F4979DE");
    static readonly Guid EncStateMachineStateMap = new("8B78CD68-2EDE-420B-980B-E15884B8AAA3");

    static EditAndContinueMethodDebugInformation DebugInformation(MetadataReader pdb, MethodDefinitionHandle handle)
    {
        ImmutableArray<byte> slots = default, lambdas = default, states = default;
        foreach (var cdiHandle in pdb.GetCustomDebugInformation(handle))
        {
            var cdi = pdb.GetCustomDebugInformation(cdiHandle);
            var kind = pdb.GetGuid(cdi.Kind);
            if (kind == EncLocalSlotMap) slots = pdb.GetBlobContent(cdi.Value);
            else if (kind == EncLambdaAndClosureMap) lambdas = pdb.GetBlobContent(cdi.Value);
            else if (kind == EncStateMachineStateMap) states = pdb.GetBlobContent(cdi.Value);
        }
        return EditAndContinueMethodDebugInformation.Create(
            slots.IsDefault ? ImmutableArray<byte>.Empty : slots,
            lambdas.IsDefault ? ImmutableArray<byte>.Empty : lambdas,
            states.IsDefault ? ImmutableArray<byte>.Empty : states);
    }

    /// <summary>The delta from the session's compilation to <paramref name="current"/>; null when
    /// nothing the runtime runs changed. Throws <see cref="RudeEditException"/> when the edit
    /// cannot be applied in place.</summary>
    public AssemblyDelta? Emit(string assembly, CSharpCompilation current)
    {
        var session = _sessions[assembly];
        var edits = Diff(session.Compilation, current, out var changed);
        if (edits.Count == 0)
        {
            session.Compilation = current;
            return null;
        }
        using var metadata = new MemoryStream();
        using var il = new MemoryStream();
        using var pdb = new MemoryStream();
        var result = current.EmitDifference(session.Baseline, edits, _ => false, metadata, il, pdb, default);
        if (!result.Success)
        {
            var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(5).Select(d => d.ToString());
            throw new RudeEditException("the edit could not be emitted: " + string.Join("; ", errors));
        }
        session.Baseline = result.Baseline!;
        session.Compilation = current;
        session.Generation++;
        return new AssemblyDelta
        {
            Name = assembly,
            Metadata = metadata.ToArray(),
            IL = il.ToArray(),
            Pdb = pdb.ToArray(),
            Edits = edits.Count,
            Changed = changed,
        };
    }

    // ------------------------------------------------------------------ semantic edits

    /// <summary>Member-level edits between two compilations of the same assembly, from the syntax
    /// trees whose text changed (matched by file path).</summary>
    static List<SemanticEdit> Diff(CSharpCompilation previous, CSharpCompilation current, out List<string> changed)
    {
        var edits = new List<SemanticEdit>();
        changed = new List<string>();
        var oldTrees = previous.SyntaxTrees.ToDictionary(t => t.FilePath);
        var newTrees = current.SyntaxTrees.ToDictionary(t => t.FilePath);
        var oldMembers = new Dictionary<string, (ISymbol Symbol, SyntaxNode Syntax)>();
        var newMembers = new Dictionary<string, (ISymbol Symbol, SyntaxNode Syntax)>();
        var touchedFiles = new List<string>();
        foreach (var (path, tree) in newTrees)
        {
            if (oldTrees.TryGetValue(path, out var oldTree) && oldTree.GetText().ContentEquals(tree.GetText())) continue;
            touchedFiles.Add(path);
            Collect(current, tree, newMembers);
            if (oldTree != null) Collect(previous, oldTree, oldMembers);
        }
        foreach (var (path, oldTree) in oldTrees)
        {
            if (newTrees.ContainsKey(path)) continue;
            touchedFiles.Add(path);
            Collect(previous, oldTree, oldMembers);
        }
        // A member can move between files; look it up in the whole compilation when missing.
        foreach (var (id, (symbol, syntax)) in newMembers)
        {
            if (oldMembers.TryGetValue(id, out var old))
            {
                // A field's syntax is its declarator (`every = 30`): its type and modifiers are on
                // the declaration around it, so they are compared through the symbols.
                if (Same(old.Syntax, syntax) && !FieldShapeChanged(old.Symbol, symbol)) continue;
                if (symbol is IFieldSymbol changedField && !changedField.IsConst && SymbolEqualityComparer.Default.Equals(((IFieldSymbol)old.Symbol).Type, changedField.Type) && ((IFieldSymbol)old.Symbol).IsStatic == changedField.IsStatic)
                {
                    // Only the initializer changed: it runs in the constructors.
                    AddConstructorUpdates(edits, changed, previous, current, changedField);
                    continue;
                }
                AddUpdate(edits, changed, old.Symbol, symbol, id);
                continue;
            }
            var moved = Find(previous, id);
            if (moved != null)
            {
                var movedSyntax = moved.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (movedSyntax != null && Same(movedSyntax, syntax) && !FieldShapeChanged(moved, symbol)) continue;
                AddUpdate(edits, changed, moved, symbol, id);
                continue;
            }
            // New member or type.
            if (symbol is INamedTypeSymbol || symbol.ContainingType == null || Find(previous, Id(symbol.ContainingType)) != null)
            {
                if (symbol is IFieldSymbol field && field.ContainingType.TypeKind == TypeKind.Struct && !field.IsStatic)
                    throw new RudeEditException($"adding an instance field to the struct {field.ContainingType.Name} needs a restart");
                if (symbol.ContainingType != null && newMembers.ContainsKey(Id(symbol.ContainingType)) && Find(previous, Id(symbol.ContainingType)) == null)
                    continue; // inserted with its new containing type
                edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, symbol));
                changed.Add("+" + id);
                if (symbol is IFieldSymbol f && HasInitializer(f))
                    AddConstructorUpdates(edits, changed, previous, current, f);
            }
        }
        foreach (var (id, (symbol, _)) in oldMembers)
        {
            if (newMembers.ContainsKey(id) || Find(current, id) != null) continue;
            if (symbol is INamedTypeSymbol) throw new RudeEditException($"removing the type {symbol.Name} needs a restart");
            if (symbol is IFieldSymbol || symbol is IPropertySymbol || symbol is IEventSymbol)
                throw new RudeEditException($"removing {symbol.Name} from {symbol.ContainingType?.Name} needs a restart");
            var containing = Find(current, Id(symbol.ContainingType));
            if (containing == null) continue;
            edits.Add(new SemanticEdit(SemanticEditKind.Delete, symbol, containing));
            changed.Add("-" + id);
        }
        return Deduplicate(edits);
    }

    static List<SemanticEdit> Deduplicate(List<SemanticEdit> edits)
    {
        var seen = new HashSet<(SemanticEditKind, ISymbol?, ISymbol?)>();
        var result = new List<SemanticEdit>();
        foreach (var e in edits)
            if (seen.Add((e.Kind, e.OldSymbol, e.NewSymbol))) result.Add(e);
        return result;
    }

    static void AddUpdate(List<SemanticEdit> edits, List<string> changed, ISymbol old, ISymbol symbol, string id)
    {
        switch (symbol)
        {
            case IMethodSymbol:
                edits.Add(new SemanticEdit(SemanticEditKind.Update, old, symbol));
                changed.Add("~" + id);
                break;
            case IPropertySymbol property:
                var oldProperty = (IPropertySymbol)old;
                if (!SymbolEqualityComparer.Default.Equals(oldProperty.Type, property.Type))
                    throw new RudeEditException($"changing the type of {property.Name} needs a restart");
                if (property.GetMethod != null && oldProperty.GetMethod != null)
                    edits.Add(new SemanticEdit(SemanticEditKind.Update, oldProperty.GetMethod, property.GetMethod));
                if (property.SetMethod != null && oldProperty.SetMethod != null)
                    edits.Add(new SemanticEdit(SemanticEditKind.Update, oldProperty.SetMethod, property.SetMethod));
                if (property.GetMethod != null && oldProperty.GetMethod == null)
                    edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, property.GetMethod));
                if (property.SetMethod != null && oldProperty.SetMethod == null)
                    edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, property.SetMethod));
                changed.Add("~" + id);
                break;
            case IFieldSymbol field:
                var oldField = (IFieldSymbol)old;
                if (!SymbolEqualityComparer.Default.Equals(oldField.Type, field.Type) || oldField.IsStatic != field.IsStatic)
                    throw new RudeEditException($"changing the field {field.Name} of {field.ContainingType.Name} needs a restart");
                if (field.IsConst)
                {
                    // Constants are inlined at their uses: every user would need recompiling.
                    throw new RudeEditException($"changing the constant {field.ContainingType.Name}.{field.Name} needs a restart");
                }
                break;
            case IEventSymbol:
                changed.Add("~" + id);
                break;
            case INamedTypeSymbol type:
                var oldType = (INamedTypeSymbol)old;
                if (!SymbolEqualityComparer.Default.Equals(oldType.BaseType, type.BaseType) ||
                    oldType.TypeKind != type.TypeKind ||
                    !oldType.Interfaces.Select(Id).OrderBy(x => x).SequenceEqual(type.Interfaces.Select(Id).OrderBy(x => x)))
                    throw new RudeEditException($"changing the base type or interfaces of {type.Name} needs a restart");
                break;
        }
    }

    static bool FieldShapeChanged(ISymbol old, ISymbol current) =>
        old is IFieldSymbol oldField && current is IFieldSymbol field &&
        (!SymbolEqualityComparer.Default.Equals(oldField.Type, field.Type) || oldField.IsStatic != field.IsStatic ||
         oldField.IsConst != field.IsConst || oldField.IsReadOnly != field.IsReadOnly);

    static bool HasInitializer(IFieldSymbol field) =>
        field.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is VariableDeclaratorSyntax v && v.Initializer != null);

    /// <summary>A new or changed field initializer runs in the type's constructors.</summary>
    static void AddConstructorUpdates(List<SemanticEdit> edits, List<string> changed, CSharpCompilation previous, CSharpCompilation current, IFieldSymbol field)
    {
        var type = field.ContainingType;
        var oldType = Find(previous, Id(type)) as INamedTypeSymbol;
        if (oldType == null) return;
        var ctors = field.IsStatic ? type.StaticConstructors : type.InstanceConstructors;
        foreach (var ctor in ctors)
        {
            var oldCtor = (field.IsStatic ? oldType.StaticConstructors : oldType.InstanceConstructors)
                .FirstOrDefault(c => Id(c) == Id(ctor));
            if (oldCtor != null) edits.Add(new SemanticEdit(SemanticEditKind.Update, oldCtor, ctor));
            else edits.Add(new SemanticEdit(SemanticEditKind.Insert, null, ctor));
            changed.Add("~" + Id(ctor));
        }
    }

    static bool Same(SyntaxNode a, SyntaxNode b) => a.IsEquivalentTo(b, topLevel: false);

    static string Id(ISymbol symbol) => symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString();

    static ISymbol? Find(CSharpCompilation compilation, string id)
    {
        var found = DocumentationCommentId.GetSymbolsForDeclarationId(id, compilation);
        return found.IsDefaultOrEmpty ? null : found[0];
    }

    /// <summary>Declared types and members of a tree, with their syntax (fields: the declarator,
    /// whose initializer is part of it; methods: the whole declaration).</summary>
    static void Collect(CSharpCompilation compilation, SyntaxTree tree, Dictionary<string, (ISymbol, SyntaxNode)> into)
    {
        var model = compilation.GetSemanticModel(tree);
        foreach (var node in tree.GetRoot().DescendantNodes(n => !(n is BlockSyntax || n is ArrowExpressionClauseSyntax)))
        {
            ISymbol? symbol = node switch
            {
                BaseTypeDeclarationSyntax or DelegateDeclarationSyntax => model.GetDeclaredSymbol(node),
                BaseMethodDeclarationSyntax or AccessorDeclarationSyntax => model.GetDeclaredSymbol(node),
                PropertyDeclarationSyntax or IndexerDeclarationSyntax or EventDeclarationSyntax => model.GetDeclaredSymbol(node),
                VariableDeclaratorSyntax v when v.Parent?.Parent is BaseFieldDeclarationSyntax => model.GetDeclaredSymbol(v),
                _ => null,
            };
            if (symbol == null || symbol.IsImplicitlyDeclared) continue;
            if (symbol is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove } accessor && node is AccessorDeclarationSyntax)
            {
                // Accessors compare through their property, whose syntax includes them.
                continue;
            }
            var id = Id(symbol);
            // Partial declarations: keep the first; a changed part changes the whole tree anyway.
            into.TryAdd(id, (symbol, node));
        }
    }
}

public sealed class RudeEditException : Exception
{
    public RudeEditException(string message) : base(message) { }
}
