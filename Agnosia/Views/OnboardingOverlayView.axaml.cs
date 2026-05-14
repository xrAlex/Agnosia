using Avalonia.Controls;
using Avalonia.Threading;

namespace Agnosia.Views;

public partial class OnboardingOverlayView : UserControl
{
    private const int CardRevealDelayMs = 500;
    private CancellationTokenSource? _cardRevealCancellation;

    public OnboardingOverlayView()
    {
        InitializeComponent();

        AttachedToVisualTree += (_, _) => StartCardReveal();
        DetachedFromVisualTree += (_, _) => CancelCardReveal();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty) StartCardReveal();
        };
    }

    private void StartCardReveal()
    {
        CancelCardReveal();
        HideInfoCards();

        if (!IsVisible) return;

        _cardRevealCancellation = new CancellationTokenSource();
        _ = RevealInfoCardsAsync(_cardRevealCancellation.Token);
    }

    private void CancelCardReveal()
    {
        _cardRevealCancellation?.Cancel();
        _cardRevealCancellation?.Dispose();
        _cardRevealCancellation = null;
    }

    private async Task RevealInfoCardsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cards = GetInfoCards();
            foreach (var card in cards)
            {
                await Task.Delay(CardRevealDelayMs, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                await Dispatcher.UIThread.InvokeAsync(() => card.Opacity = 1, DispatcherPriority.Background);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void HideInfoCards()
    {
        foreach (var card in GetInfoCards()) card.Opacity = 0;
    }

    private Border[] GetInfoCards()
    {
        return
        [
            InfoCardAgnosia,
            InfoCardIsolation,
            InfoCardWorkProfile,
            InfoCardRussianApps,
            InfoCardLessTraces,
            InfoCardTemporaryLaunch
        ];
    }
}