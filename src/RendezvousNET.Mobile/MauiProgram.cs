using Microsoft.Extensions.Logging;
using RendezvousNET.Mobile.ViewModels;
using RendezvousNET.Mobile.Views;

namespace RendezvousNET.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMaui()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Register pages and view models
        builder.Services.AddTransient<MeetingDashboardPage>();
        builder.Services.AddTransient<MeetingDashboardViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
