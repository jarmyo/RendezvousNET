using RendezvousNET.Mobile.ViewModels;

namespace RendezvousNET.Mobile.Views;

public partial class MeetingDashboardPage : ContentPage
{
    public MeetingDashboardPage(MeetingDashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MeetingDashboardViewModel vm)
            await vm.InitializeAsync();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is MeetingDashboardViewModel vm)
            await vm.CleanupAsync();
    }
}
