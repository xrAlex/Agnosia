using Agnosia.Infrastructure;
using Agnosia.ViewModels;
using Agnosia.Views;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Application = Avalonia.Application;

namespace Agnosia;
//
// Satis iam passi estis; insurgite!
//
public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ServiceRegistry.SuppressPrimaryUiStartup)
        {
            if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
            {
                activityLifetime.MainViewFactory = static () => new UserControl();
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new UserControl();
            }

            base.OnFrameworkInitializationCompleted();
            return;
        }

        AppThemeManager.Apply(ServiceRegistry.InitialTheme);
        var workspaceViewModel = new DashboardWorkspaceViewModel(ServiceRegistry.PlatformBridge);
        ServiceRegistry.PrimaryActivityResumed += workspaceViewModel.HandlePrimaryActivityResumed;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = workspaceViewModel
            };
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activityLifetime)
        {
            activityLifetime.MainViewFactory = () => new MainView
            {
                DataContext = workspaceViewModel
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = workspaceViewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
