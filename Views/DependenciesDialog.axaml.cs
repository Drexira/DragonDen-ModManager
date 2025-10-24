using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using DragonDen.ModManager.Services;
using DragonDen.ModManager.ViewModels;

namespace DragonDen.ModManager.Views;

public partial class DependenciesDialog : Window
{
    public enum InstallChoice { InstallWithDeps, InstallWithoutDeps, Cancel }
    public DependenciesDialog()
    {
        InitializeComponent();

        InstallWithDepsBtn.Click += (_, __) => Close(InstallChoice.InstallWithDeps);
        InstallWithoutDepsBtn.Click += (_, __) => Close(InstallChoice.InstallWithoutDeps);
        CancelBtn.Click += (_, __) => Close(InstallChoice.Cancel);

        Closed += (_, __) =>
        {
            if (!IsVisible) return;
        };
    }

    public static Task<InstallChoice> ShowAsync(
        Window owner,
        string modName,
        IEnumerable<ForgeClient.MissingDep> deps)
    {
        var vm = DependenciesDialogViewModel.Create(modName, deps);
        var dlg = new DependenciesDialog { DataContext = vm, Icon = owner?.Icon };
        return dlg.ShowDialog<InstallChoice>(owner);
    }
}