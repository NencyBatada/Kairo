using System.Reflection;
using System.Xml.Linq;
using Kairo.SharedKernel;
using Kairo.Testing;

namespace Kairo.ArchitectureTests;

public sealed class ModuleRulesTests
{
    public static TheoryData<string> Modules => new(ModuleFolders.Names);

    [Fact]
    public void Kairo_has_modules()
    {
        Assert.NotEmpty(ModuleFolders.Names);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_exposes_only_its_module_class(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var moduleName = assemblyName["Kairo.Modules.".Length..];

        var type = Assert.Single(assembly.GetExportedTypes());

        Assert.Equal($"{assemblyName}.{moduleName}Module", type.FullName);
        Assert.True(type.IsAssignableTo(typeof(IModule)), $"{type.FullName} must implement IModule.");
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_references_only_SharedKernel_and_Contracts(string assemblyName)
    {
        var references = KairoReferencesOf(Assembly.Load(assemblyName));

        Assert.Subset(new HashSet<string> { "Kairo.SharedKernel", "Kairo.Contracts" }, references);
    }

    [Theory]
    [MemberData(nameof(Modules))]
    public void Module_project_adds_no_project_references(string assemblyName)
    {
        var references = XDocument.Load(ModuleFolders.ProjectFile(assemblyName))
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"));

        Assert.Empty(references);
    }

    [Fact]
    public void Contracts_references_only_SharedKernel()
    {
        var references = KairoReferencesOf(Assembly.Load("Kairo.Contracts"));

        Assert.Subset(new HashSet<string> { "Kairo.SharedKernel" }, references);
    }

    [Fact]
    public void SharedKernel_references_no_other_Kairo_assembly()
    {
        Assert.Empty(KairoReferencesOf(typeof(IModule).Assembly));
    }

    private static HashSet<string> KairoReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Kairo.", StringComparison.Ordinal))
            .ToHashSet();
}
