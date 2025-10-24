using System.Collections.Generic;
using System.Linq;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.ViewModels;

public sealed class DependenciesDialogViewModel
{
    public string Header { get; init; } = "Dependencies required";
    public string Subheader { get; init; } = "";
    public List<ItemVm> Items { get; init; } = new();

    public sealed class ItemVm
    {
        public string Name { get; init; } = "";
        public string? VersionConstraint { get; init; }
        public bool IsOptional { get; init; }
    }

    public static DependenciesDialogViewModel Create(string modName, IEnumerable<ForgeClient.MissingDep> deps)
    {
        var list = deps.ToList();
        return new DependenciesDialogViewModel
        {
            Header = "Dependencies required",
            Subheader = $"“{modName}” needs {list.Count} dependenc{(list.Count == 1 ? "y" : "ies")}:",
            Items = list.Select(d => new ItemVm
            {
                Name = d.Name ?? "(unknown)",
                VersionConstraint = string.IsNullOrWhiteSpace(d.VersionConstraint) ? null : d.VersionConstraint,
                IsOptional = d.IsOptional
            }).ToList()
        };
    }
}