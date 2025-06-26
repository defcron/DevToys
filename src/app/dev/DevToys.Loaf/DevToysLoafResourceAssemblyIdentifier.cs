using DevToys.Api;
using System.ComponentModel.Composition;

namespace DevToys.Loaf;

[Export(typeof(IResourceAssemblyIdentifier))]
[Name(nameof(DevToysLoafResourceAssemblyIdentifier))]
public sealed class DevToysLoafResourceAssemblyIdentifier : IResourceAssemblyIdentifier
{
    public ValueTask<FontDefinition[]> GetFontDefinitionsAsync()
    {
        throw new NotImplementedException();
    }
}