using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ContextLeech.Constants;
using ContextLeech.Services.Static.DotnetSolutionDependenciesAnalyzer.Models;
using ContextLeech.Services.Static.FileIo;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using CSharpCompilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation;
using CSharpExtensions = Microsoft.CodeAnalysis.CSharp.CSharpExtensions;
using CSharpSyntaxVisitor = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxVisitor;

namespace ContextLeech.Services.Static.DotnetSolutionDependenciesAnalyzer;

public static class StaticDotnetSolutionDependenciesAnalyzer
{
    private const string GraphFile = "graph.json";

    private static readonly Type NonSourceAssemblySymbolType = typeof(CSharpSyntaxVisitor)
        .Assembly
        .GetTypes()
        .Single(x => x is { Namespace: "Microsoft.CodeAnalysis.CSharp.Symbols.PublicModel", Name: "NonSourceAssemblySymbol" });

    public static void Save(DependenciesGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var json = graph.Serialize();
        var projectFile = Path.Combine(
            graph.GetRoot().FullName,
            FileSystemConstants.ContextLeechRootDirectory,
            FileSystemConstants.MetadataSubDirectory,
            GraphFile);
        StaticFileIo.Write(projectFile, json, Encoding.UTF8);
    }

    public static bool TryReadExisting(DirectoryInfo projectRoot, [NotNullWhen(true)] out DependenciesGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        var projectFilePath = Path.Combine(
            projectRoot.FullName,
            FileSystemConstants.ContextLeechRootDirectory,
            FileSystemConstants.MetadataSubDirectory,
            GraphFile);
        if (!StaticFileIo.TryReadExisting(projectFilePath, Encoding.UTF8, out var json))
        {
            graph = null;
            return false;
        }

        if (DependenciesGraph.TryDeserialize(json, projectRoot, out var deserializedProject))
        {
            graph = deserializedProject;
            return true;
        }

        graph = null;
        return false;
    }

    public static async Task<DependenciesGraph> AnalyzeSolutionAsync(
        DirectoryInfo projectRoot,
        FileInfo solutionFile)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(solutionFile);
        if (!projectRoot.Exists)
        {
            throw new ArgumentException("Project root not exists", nameof(projectRoot));
        }

        if (!solutionFile.Exists)
        {
            throw new ArgumentException("Solution file not exists", nameof(solutionFile));
        }

        if (!solutionFile.FullName.StartsWith(projectRoot.FullName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Solution file is outside project root", nameof(solutionFile));
        }

        var actualProjectRoot = new DirectoryInfo(projectRoot.FullName);
        if (!actualProjectRoot.Exists)
        {
            throw new InvalidOperationException("Can't get project root absolute path");
        }

        var results = new ConcurrentBag<AnalyzeResult>();
        using var workspace = MSBuildWorkspace.Create();
        var solution = await workspace.OpenSolutionAsync(solutionFile.FullName);

        var projects = solution.Projects.ToArray();
        var compilationsWithProjects = new List<(CSharpCompilation Compilation, Project Project)>();
        foreach (var project in projects)
        {
            var abstractCompilation = await project.GetCompilationAsync();
            if (abstractCompilation is not CSharpCompilation cSharpCompilation)
            {
                continue;
            }

            compilationsWithProjects.Add(new(cSharpCompilation, project));
        }

        await Parallel.ForEachAsync(compilationsWithProjects, async (compilationWithProject, _) =>
        {
            var (cSharpCompilation, project) = compilationWithProject;
            var innerResults = await AnalyzeCompilationAsync(cSharpCompilation, project);
            foreach (var innerResult in innerResults)
            {
                results.Add(innerResult);
            }
        });

        var upstreamDependenciesGraph = CreateUpstreamGraph(results);
        var downstreamDependenciesGraph = CreateDownstreamGraphFromUpstreamGraph(upstreamDependenciesGraph);

        var resultUpstream = TransformToResultGraph(upstreamDependenciesGraph);
        var resultDownstream = TransformToResultGraph(downstreamDependenciesGraph);

        var filteredUpstream = FilterFilesOutsideProjectDirectory(resultUpstream, actualProjectRoot);
        var filteredDownstream = FilterFilesOutsideProjectDirectory(resultDownstream, actualProjectRoot);

        return new(actualProjectRoot, filteredUpstream, filteredDownstream);
    }

    private static Dictionary<FileInfo, HashSet<FileInfo>> FilterFilesOutsideProjectDirectory(
        Dictionary<FileInfo, HashSet<FileInfo>> graph,
        DirectoryInfo projectDirectory)
    {
        var resultsAccumulator = new Dictionary<FileInfo, HashSet<FileInfo>>();
        foreach (var (key, values) in graph)
        {
            if (key.FullName.StartsWith(projectDirectory.FullName, StringComparison.Ordinal))
            {
                var valuesAccumulator = new HashSet<FileInfo>();
                foreach (var value in values)
                {
                    if (value.FullName.StartsWith(projectDirectory.FullName, StringComparison.Ordinal))
                    {
                        valuesAccumulator.Add(value);
                    }
                }

                resultsAccumulator[key] = valuesAccumulator;
            }
        }

        return resultsAccumulator;
    }

    private static Dictionary<FileInfo, HashSet<FileInfo>> TransformToResultGraph(Dictionary<string, HashSet<string>> src)
    {
        var result = new Dictionary<FileInfo, HashSet<FileInfo>>();
        foreach (var (key, values) in src)
        {
            var keyFileInfo = new FileInfo(key);
            if (!keyFileInfo.Exists)
            {
                continue;
            }

            var valuesAccumulator = new HashSet<FileInfo>();
            foreach (var value in values)
            {
                var valueFileInfo = new FileInfo(value);
                if (!valueFileInfo.Exists)
                {
                    continue;
                }

                valuesAccumulator.Add(valueFileInfo);
            }

            result[keyFileInfo] = valuesAccumulator;
        }

        return result;
    }

    private static Dictionary<string, HashSet<string>> CreateDownstreamGraphFromUpstreamGraph(
        Dictionary<string, HashSet<string>> upstreamDependenciesGraph)
    {
        var downstreamGraph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (downstreamDependency, upstreamDependencies) in upstreamDependenciesGraph)
        {
            if (upstreamDependencies.Count > 0)
            {
                foreach (var upstreamDependency in upstreamDependencies)
                {
                    if (!downstreamGraph.TryGetValue(upstreamDependency, out var downstreamDependencies))
                    {
                        downstreamDependencies = new(StringComparer.OrdinalIgnoreCase);
                        downstreamGraph[upstreamDependency] = downstreamDependencies;
                    }

                    downstreamDependencies.Add(downstreamDependency);
                    if (!downstreamGraph.ContainsKey(upstreamDependency))
                    {
                        downstreamGraph[upstreamDependency] = new(StringComparer.OrdinalIgnoreCase);
                    }
                }
            }

            if (!downstreamGraph.ContainsKey(downstreamDependency))
            {
                downstreamGraph[downstreamDependency] = new(StringComparer.OrdinalIgnoreCase);
            }
        }

        return downstreamGraph;
    }

    private static Dictionary<string, HashSet<string>> CreateUpstreamGraph(ConcurrentBag<AnalyzeResult> results)
    {
        var upstreamGraph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var foundDependency in results)
        {
            if (!upstreamGraph.TryGetValue(foundDependency.FullPath, out var upstreamDependencies))
            {
                upstreamDependencies = new(StringComparer.OrdinalIgnoreCase);
                upstreamGraph[foundDependency.FullPath] = upstreamDependencies;
            }

            foreach (var dependency in foundDependency.DependsFullPaths)
            {
                upstreamDependencies.Add(dependency);
                if (!upstreamGraph.ContainsKey(dependency))
                {
                    upstreamGraph[dependency] = new(StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        return upstreamGraph;
    }

    private static async Task<List<AnalyzeResult>> AnalyzeCompilationAsync(
        CSharpCompilation compilation,
        Project project)
    {
        var results = new List<AnalyzeResult>();
        var razorFiles = project.AdditionalDocuments
            .Where(x => !string.IsNullOrEmpty(x.FilePath))
            .Select(x => x.FilePath)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => new FileInfo(x))
            .Where(x => x is { Exists: true, Extension: ".cshtml" or ".razor" })
            .Select(x => x.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var innerResults = await AnalyzeSyntaxTreeAsync(
                syntaxTree,
                compilation,
                razorFiles);
            results.AddRange(innerResults);
        }

        return results;
    }

    private static async Task<List<AnalyzeResult>> AnalyzeSyntaxTreeAsync(
        SyntaxTree syntaxTree,
        CSharpCompilation compilation,
        HashSet<string> razorFiles)
    {
        var filePath = syntaxTree.FilePath;
        if (string.IsNullOrEmpty(filePath))
        {
            return new();
        }

        var results = new List<AnalyzeResult>();
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var typeDeclarations = await GetTypeDeclarationsAsync(syntaxTree);
        foreach (var typeDeclaration in typeDeclarations)
        {
            var innerResults = await AnalyzeTypeDeclarationAsync(
                typeDeclaration,
                semanticModel,
                syntaxTree,
                compilation,
                filePath,
                razorFiles);
            results.AddRange(innerResults);
        }

        return results;
    }

    private static async Task<List<TypeDeclarationSyntax>> GetTypeDeclarationsAsync(SyntaxTree syntaxTree)
    {
        var root = await syntaxTree.GetRootAsync();
        var typeDeclarations = root.DescendantNodes()
            .Where(static x => x.GetType().IsAssignableTo(typeof(TypeDeclarationSyntax)))
            .Select(static x => x as TypeDeclarationSyntax)
            .Where(static x => x is not null)
            .Select(static x => x!)
            .ToList();
        return typeDeclarations;
    }

    private static async Task<List<AnalyzeResult>> AnalyzeTypeDeclarationAsync(
        TypeDeclarationSyntax typeDeclaration,
        SemanticModel semanticModel,
        SyntaxTree syntaxTree,
        Compilation compilation,
        string filePath,
        HashSet<string> razorFiles)
    {
        var sourceFile = new FileInfo(filePath);
        if (!sourceFile.Exists)
        {
            if (IsRazorFile(syntaxTree, razorFiles, out var newFilePath))
            {
                filePath = newFilePath;
            }
            else
            {
                return new();
            }
        }

        var declaredSymbol = CSharpExtensions.GetDeclaredSymbol(semanticModel, typeDeclaration);
        if (declaredSymbol is not ITypeSymbol typeSymbol)
        {
            return new();
        }

        var typeName = GetFullTypeName(typeSymbol);
        // todo: Check if this is a Designer.cs file for resx resources
        var visitedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var dependencies = await AnalyzeRootTypeSymbolAsync(
            typeSymbol,
            visitedSymbols,
            compilation);

        // Cleanup non-exists deps and self-linked deps
        var cleanDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies)
        {
            var dependencyFile = new FileInfo(dependency);
            if (dependencyFile.Exists)
            {
                if (!string.Equals(dependencyFile.FullName, sourceFile.FullName, StringComparison.OrdinalIgnoreCase))
                {
                    cleanDependencies.Add(dependencyFile.FullName);
                }
            }
        }

        return [new(typeName, filePath, cleanDependencies)];
    }

    private static bool IsRazorFile(
        SyntaxTree syntaxTree,
        HashSet<string> razorFiles,
        [NotNullWhen(true)] out string? razorFilePath)
    {
        var root = syntaxTree.GetRoot();
        foreach (var trivia in root.GetLeadingTrivia())
        {
            if (trivia.GetStructure() is PragmaChecksumDirectiveTriviaSyntax { File.ValueText: { } file }
                && (file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)))
            {
                var fileInfo = new FileInfo(file);
                if (!fileInfo.Exists)
                {
                    continue;
                }

                if (razorFiles.Contains(fileInfo.FullName))
                {
                    razorFilePath = fileInfo.FullName;
                    return true;
                }
            }
        }

        razorFilePath = null;
        return false;
    }

    private static string GetFullTypeName(ITypeSymbol typeSymbol)
    {
        var parts = new List<string>();
        ISymbol current = typeSymbol;
        while (current != null)
        {
            if (current is ITypeSymbol or INamespaceSymbol { IsGlobalNamespace: false })
            {
                parts.Insert(0, current.Name);
            }

            current = current.ContainingSymbol;
        }

        return string.Join(".", parts);
    }

    private static async Task<HashSet<string>> AnalyzeRootTypeSymbolAsync(
        ITypeSymbol typeSymbol,
        HashSet<ISymbol> visitedSymbols,
        Compilation compilation)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!visitedSymbols.Add(typeSymbol))
        {
            return dependencies;
        }

        // Analyze base type
        if (typeSymbol.BaseType is not null && !IsExternalType(typeSymbol.BaseType))
        {
            foreach (var innerDependency in AddDependencies(typeSymbol.BaseType))
            {
                dependencies.Add(innerDependency);
            }
        }

        // Analyze interfaces
        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            if (!IsExternalType(interfaceType))
            {
                foreach (var innerDependency in AddDependencies(typeSymbol.BaseType))
                {
                    dependencies.Add(innerDependency);
                }
            }
        }

        // Analyze type parameters and constraints
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } genericType)
        {
            foreach (var typeParam in genericType.TypeParameters)
            {
                foreach (var constraintType in typeParam.ConstraintTypes)
                {
                    if (!IsExternalType(constraintType))
                    {
                        foreach (var innerDependency in AddDependencies(typeSymbol.BaseType))
                        {
                            dependencies.Add(innerDependency);
                        }
                    }

                    foreach (var innerDependency in AnalyzeTypeReference(constraintType))
                    {
                        dependencies.Add(innerDependency);
                    }
                }
            }

            foreach (var typeArg in genericType.TypeArguments)
            {
                foreach (var innerDependency in AnalyzeTypeReference(typeArg))
                {
                    dependencies.Add(innerDependency);
                }
            }
        }

        // Get all members including private ones
        var members = typeSymbol.GetMembers();
        foreach (var member in members)
        {
            var innerDependencies = await AnalyzeMemberAsync(member, visitedSymbols, compilation);
            foreach (var innerDependency in innerDependencies)
            {
                dependencies.Add(innerDependency);
            }
        }

        return dependencies;
    }

    private static async Task<HashSet<string>> AnalyzeMemberAsync(
        ISymbol? member,
        HashSet<ISymbol> visitedSymbols,
        Compilation compilation)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (member is null || !visitedSymbols.Add(member))
        {
            return dependencies;
        }

        switch (member)
        {
            case IFieldSymbol field:
                {
                    foreach (var innerDependency in AnalyzeTypeReference(field.Type))
                    {
                        dependencies.Add(innerDependency);
                    }

                    break;
                }
            case IPropertySymbol property:
                {
                    foreach (var innerDependency in AnalyzeTypeReference(property.Type))
                    {
                        dependencies.Add(innerDependency);
                    }

                    foreach (var param in property.Parameters)
                    {
                        foreach (var innerDependency in AnalyzeTypeReference(param.Type))
                        {
                            dependencies.Add(innerDependency);
                        }
                    }

                    break;
                }
            case IMethodSymbol method:
                {
                    var innerDependencies = await AnalyzeMethodAsync(method, compilation);
                    foreach (var innerDependency in innerDependencies)
                    {
                        dependencies.Add(innerDependency);
                    }

                    break;
                }
            case IEventSymbol eventSymbol:
                {
                    foreach (var innerDependency in AnalyzeTypeReference(eventSymbol.Type))
                    {
                        dependencies.Add(innerDependency);
                    }

                    break;
                }
            case INamedTypeSymbol nestedType:
                {
                    var innerDependencies = await AnalyzeRootTypeSymbolAsync(nestedType, visitedSymbols, compilation);
                    foreach (var innerDependency in innerDependencies)
                    {
                        dependencies.Add(innerDependency);
                    }

                    break;
                }
        }

        // Analyze member attributes
        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass is not null && !IsExternalType(attribute.AttributeClass))
            {
                foreach (var innerDependency in AddDependencies(attribute.AttributeClass))
                {
                    dependencies.Add(innerDependency);
                }
            }
        }

        return dependencies;
    }

    private static async Task<HashSet<string>> AnalyzeMethodAsync(IMethodSymbol method, Compilation compilation)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Analyze return type
        foreach (var innerDependency in AnalyzeTypeReference(method.ReturnType))
        {
            dependencies.Add(innerDependency);
        }

        // Analyze parameters
        foreach (var parameter in method.Parameters)
        {
            foreach (var innerDependency in AnalyzeTypeReference(parameter.Type))
            {
                dependencies.Add(innerDependency);
            }

            // Analyze default values
            if (parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: ITypeSymbol typeDefault })
            {
                foreach (var innerDependency in AnalyzeTypeReference(typeDefault))
                {
                    dependencies.Add(innerDependency);
                }
            }

            // Analyze type parameters and constraints
            foreach (var typeParam in method.TypeParameters)
            {
                foreach (var constraint in typeParam.ConstraintTypes)
                {
                    foreach (var innerDependency in AnalyzeTypeReference(constraint))
                    {
                        dependencies.Add(innerDependency);
                    }
                }
            }

            // Analyze method body for local functions, lambdas, and type references
            if (method.DeclaringSyntaxReferences.Any())
            {
                foreach (var syntaxRef in method.DeclaringSyntaxReferences)
                {
                    var syntax = await syntaxRef.GetSyntaxAsync();
                    var innerDependencies = await AnalyzeMethodBodyAsync(syntax, compilation);
                    foreach (var innerDependency in innerDependencies)
                    {
                        dependencies.Add(innerDependency);
                    }
                }
            }
        }

        return dependencies;
    }

    [SuppressMessage("Maintainability", "CA1508:Avoid dead conditional code")]
    private static async Task<HashSet<string>> AnalyzeMethodBodyAsync(
        SyntaxNode? methodSyntax,
        Compilation compilation)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (methodSyntax is null)
        {
            return dependencies;
        }

        var semanticModel = compilation.GetSemanticModel(methodSyntax.SyntaxTree);

        // Find all type references in the method body
        var typeReferences = methodSyntax.DescendantNodes()
            .Where(node => node
                is TypeSyntax
                or ObjectCreationExpressionSyntax
                or CastExpressionSyntax
                or BinaryPatternSyntax
                or DeclarationPatternSyntax
                or RecursivePatternSyntax
                or TypePatternSyntax
                or ConstantPatternSyntax
                or IsPatternExpressionSyntax
                or SwitchExpressionSyntax
                or LocalFunctionStatementSyntax);
        foreach (var typeRef in typeReferences)
        {
            ITypeSymbol? typeSymbol = null;

            switch (typeRef)
            {
                case TypeSyntax typeSyntax:
                    {
                        var symbolInfo = CSharpExtensions.GetSymbolInfo(semanticModel, typeSyntax);
                        typeSymbol = symbolInfo.Symbol as ITypeSymbol ?? CSharpExtensions.GetTypeInfo(semanticModel, typeSyntax).Type;
                        if (typeSymbol is not null && !IsExternalType(typeSymbol))
                        {
                            foreach (var innerDependency in AnalyzeTypeReference(typeSymbol))
                            {
                                dependencies.Add(innerDependency);
                            }
                        }

                        break;
                    }

                case ObjectCreationExpressionSyntax creation:
                    {
                        typeSymbol = CSharpExtensions.GetTypeInfo(semanticModel, creation).Type;
                        break;
                    }

                case CastExpressionSyntax cast:
                    {
                        typeSymbol = CSharpExtensions.GetTypeInfo(semanticModel, cast.Type).Type;
                        if (typeSymbol is not null && !IsExternalType(typeSymbol))
                        {
                            foreach (var innerDependency in AnalyzeTypeReference(typeSymbol))
                            {
                                dependencies.Add(innerDependency);
                            }
                        }

                        break;
                    }

                case LocalFunctionStatementSyntax localFunction:
                    {
                        var localFunctionSymbol = CSharpExtensions.GetDeclaredSymbol(semanticModel, localFunction);
                        if (localFunctionSymbol != null)
                        {
                            var innerDependencies = await AnalyzeMethodAsync(localFunctionSymbol, compilation);
                            foreach (var innerDependency in innerDependencies)
                            {
                                dependencies.Add(innerDependency);
                            }
                        }

                        if (typeSymbol is not null && !IsExternalType(typeSymbol))
                        {
                            foreach (var innerDependency in AnalyzeTypeReference(typeSymbol))
                            {
                                dependencies.Add(innerDependency);
                            }
                        }

                        break;
                    }
            }

            if (typeSymbol is not null && !IsExternalType(typeSymbol))
            {
                foreach (var innerDependency in AnalyzeTypeReference(typeSymbol))
                {
                    dependencies.Add(innerDependency);
                }
            }
        }

        // Analyze lambda expressions and anonymous methods
        var lambdas = methodSyntax.DescendantNodes()
            .Where(node => node is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax);
        foreach (var lambda in lambdas)
        {
            if (semanticModel.GetSymbolInfo(lambda).Symbol is IMethodSymbol lambdaSymbol)
            {
                var innerDependencies = await AnalyzeMethodAsync(lambdaSymbol, compilation);
                foreach (var innerDependency in innerDependencies)
                {
                    dependencies.Add(innerDependency);
                }
            }
        }

        return dependencies;
    }

    private static HashSet<string> AnalyzeTypeReference(ITypeSymbol typeSymbol)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (IsExternalType(typeSymbol))
        {
            return dependencies;
        }

        foreach (var innerDependency in AddDependencies(typeSymbol))
        {
            dependencies.Add(innerDependency);
        }

        // Handle generic types
        if (typeSymbol is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            foreach (var typeArg in namedType.TypeArguments)
            {
                foreach (var innerDependency in AnalyzeTypeReference(typeArg))
                {
                    dependencies.Add(innerDependency);
                }
            }
        }

        // Handle array types
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            foreach (var innerDependency in AnalyzeTypeReference(arrayType.ElementType))
            {
                dependencies.Add(innerDependency);
            }
        }

        // Handle array types
        if (typeSymbol is IPointerTypeSymbol pointerType)
        {
            foreach (var innerDependency in AnalyzeTypeReference(pointerType.PointedAtType))
            {
                dependencies.Add(innerDependency);
            }
        }

        // Handle tuple types
        if (typeSymbol.IsTupleType && typeSymbol is INamedTypeSymbol tupleType)
        {
            foreach (var element in tupleType.TupleElements)
            {
                foreach (var innerDependency in AnalyzeTypeReference(element.Type))
                {
                    dependencies.Add(innerDependency);
                }
            }
        }

        // Handle function pointer types
        if (typeSymbol is IFunctionPointerTypeSymbol funcPointer)
        {
            var signature = funcPointer.Signature;
            foreach (var innerDependency in AnalyzeTypeReference(signature.ReturnType))
            {
                dependencies.Add(innerDependency);
            }

            foreach (var param in signature.Parameters)
            {
                foreach (var innerDependency in AnalyzeTypeReference(param.Type))
                {
                    dependencies.Add(innerDependency);
                }
            }
        }

        return dependencies;
    }

    private static HashSet<string> AddDependencies(ITypeSymbol? typeSymbol)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (typeSymbol is null || IsExternalType(typeSymbol))
        {
            return dependencies;
        }

        // Get the original definition to handle generic types correctly
        var originalDefinition = typeSymbol.OriginalDefinition;
        var result = GetFilePathForSymbol(originalDefinition);
        if (!string.IsNullOrWhiteSpace(result))
        {
            dependencies.Add(result);
        }

        return dependencies;
    }

    private static string? GetFilePathForSymbol(ISymbol symbol)
    {
        // Check if the symbol is from the current compilation
        if (symbol.Locations.Any())
        {
            foreach (var location in symbol.Locations)
            {
                if (location is { IsInSource: true, SourceTree: not null })
                {
                    var syntaxTree = location.SourceTree;
                    if (!string.IsNullOrEmpty(syntaxTree.FilePath))
                    {
                        return syntaxTree.FilePath;
                    }
                }
            }
        }

        return null;
    }

    private static bool IsExternalType(ITypeSymbol namedTypeSymbol)
    {
        return namedTypeSymbol.ContainingAssembly?.GetType() == NonSourceAssemblySymbolType;
    }

    private sealed record AnalyzeResult(string TypeName, string FullPath, HashSet<string> DependsFullPaths);
}
