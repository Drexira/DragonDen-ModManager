using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DragonDen.ModManager.Views;

public partial class ConfirmUninstallDialog : Window
{
    public ConfirmUninstallDialog()
    {
        InitializeComponent();
        TitleText.Text = "Uninstall Mod";
        BodyText.Text = $"Are you sure you want to uninstall 'X'?";
    }
    
    public ConfirmUninstallDialog(string modName)
    {
        InitializeComponent();
        TitleText.Text = "Uninstall Mod";
        BodyText.Text = $"Are you sure you want to uninstall '{modName}'?";
        CancelBtn.Click += (_, __) => Close(false);
        UninstallBtn.Click += (_, __) => Close(true);
        
        AddHandler(KeyDownEvent, (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close(false);
                    break;
            }
        }, RoutingStrategies.Tunnel);
    }
}