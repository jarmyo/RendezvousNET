using Android.App;
using Android.BillingClient.Api;
using Android.Widget;

namespace VrView;

/// <summary>
/// "Buy me a beer": one optional Google Play product. It unlocks nothing, so it is consumed right
/// after the purchase, which also lets people buy another round. Create <see cref="ProductId"/>
/// as a one-time product in Play Console; see store/README.md.
/// </summary>
internal sealed class TipJar : Java.Lang.Object, IPurchasesUpdatedListener
{
    public const string ProductId = "tip_beer";
    private const string InApp = "inapp"; // BillingClient.ProductType.INAPP

    private readonly Activity _activity;
    private readonly BillingClient _client;

    public TipJar(Activity activity)
    {
        _activity = activity;
        _client = BillingClient.NewBuilder(activity)
            .SetListener(this)
            .EnablePendingPurchases(PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
            .EnableAutoServiceReconnection()
            .Build();
    }

    /// <summary>Consumes purchases that finished while the app was closed (Play refunds them after 3 days otherwise).</summary>
    public async Task FinishPendingAsync()
    {
        try
        {
            if (!await ConnectAsync()) return;
            var owned = await _client.QueryPurchasesAsync(QueryPurchasesParams.NewBuilder().SetProductType(InApp).Build());
            await ConsumeAsync(owned.Purchases, thank: false);
        }
        catch (Java.Lang.Exception) { } // Tried again on the next start.
    }

    /// <summary>Opens the Google Play purchase sheet, which shows the price in the buyer's currency.</summary>
    public async Task BuyAsync()
    {
        try
        {
            if (!await ConnectAsync()) { Say(T("The beer isn't available right now. Try the Google Play version, or try again later.")); return; }
            var product = QueryProductDetailsParams.Product.NewBuilder().SetProductId(ProductId).SetProductType(InApp).Build();
            var query = await _client.QueryProductDetailsAsync(QueryProductDetailsParams.NewBuilder().SetProductList([product]).Build());
            if ((query.ProductDetails ?? query.ProductDetailsList)?.FirstOrDefault() is not { } details)
            {
                Say(T("The beer isn't available right now. Try the Google Play version, or try again later."));
                return;
            }
            var offer = BillingFlowParams.ProductDetailsParams.NewBuilder().SetProductDetails(details);
            if (details.GetOneTimePurchaseOfferDetails()?.OfferToken is { Length: > 0 } token) offer.SetOfferToken(token);
            var flow = BillingFlowParams.NewBuilder().SetProductDetailsParamsList([offer.Build()]).Build();
            _client.LaunchBillingFlow(_activity, flow);
        }
        catch (Java.Lang.Exception)
        {
            Say(T("The beer isn't available right now. Try the Google Play version, or try again later."));
        }
    }

    public void OnPurchasesUpdated(BillingResult result, IList<Purchase>? purchases)
    {
        if (result.ResponseCode == BillingResponseCode.Ok && purchases is not null) _ = ConsumeSafelyAsync(purchases);
    }

    public void Close() => _client.EndConnection();

    private async Task<bool> ConnectAsync()
    {
        if (_client.IsReady) return true;
        var result = await _client.StartConnectionAsync(() => { });
        return result.ResponseCode == BillingResponseCode.Ok;
    }

    private async Task ConsumeSafelyAsync(IList<Purchase> purchases)
    {
        try { await ConsumeAsync(purchases, thank: true); }
        catch (Java.Lang.Exception) { } // FinishPendingAsync picks it up on the next start.
    }

    private async Task ConsumeAsync(IList<Purchase>? purchases, bool thank)
    {
        foreach (var purchase in purchases ?? [])
        {
            if (!purchase.Products.Contains(ProductId)) continue;
            if (purchase.PurchaseState == PurchaseState.Pending)
            {
                if (thank) Say(T("Payment pending. Thanks for the beer!"));
                continue;
            }
            if (purchase.PurchaseState != PurchaseState.Purchased) continue;
            var done = await _client.ConsumeAsync(ConsumeParams.NewBuilder().SetPurchaseToken(purchase.PurchaseToken).Build());
            if (thank && done.BillingResult.ResponseCode == BillingResponseCode.Ok) Say(T("Thank you! Cheers! 🍻"));
        }
    }

    private void Say(string text) =>
        _activity.RunOnUiThread(() => Toast.MakeText(_activity, text, ToastLength.Long)!.Show());
}
