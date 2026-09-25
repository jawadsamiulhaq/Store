using Microsoft.AspNetCore.Mvc;
using Store.Api.Authorization;
using Store.Api.Extensions;
using Store.Application.Commerce;
using Store.Domain.Enums;
using Store.Domain.Identity;
using Store.Infrastructure.Commerce;

namespace Store.Api.Endpoints;

/// <summary>
/// Cart, wishlist, checkout, orders and shipping.
/// </summary>
/// <remarks>
/// Cart endpoints are anonymous by design — guests shop before signing in, identified by the
/// <c>wps_aid</c> cookie, and their cart is merged into their account at login. Order endpoints
/// are scoped by ownership inside the service layer, not merely by route.
/// </remarks>
public static class CommerceEndpoints
{
    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder app)
    {
        MapCart(app);
        MapWishlist(app);
        MapCheckout(app);
        MapCustomerOrders(app);
        MapAdminOrders(app);
        MapNotifications(app);
        MapShipping(app);

        return app;
    }

    // ==========================================================================================
    // Cart — anonymous, cookie-identified
    // ==========================================================================================

    private static void MapCart(IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/api/cart").WithTags("Cart").AllowAnonymous();

        // Every cart route issues the guest cookie if it is missing, so the very first
        // add-to-cart works without a prior "create cart" round trip.
        cart.MapGet("/", async (ICartService service, HttpContext http, CancellationToken ct) =>
        {
            EnsureAnonymousId(http);
            return Results.Ok(await service.GetAsync(ct));
        });

        cart.MapPost("/items", async ([FromBody] AddToCartRequest request, ICartService service, HttpContext http, CancellationToken ct) =>
        {
            EnsureAnonymousId(http);
            return (await service.AddAsync(request, ct)).ToHttpResult();
        }).WithSummary("Add a variant to the cart.");

        cart.MapPut("/items/{itemId:guid}", async (Guid itemId, [FromBody] UpdateCartItemRequest request, ICartService service, CancellationToken ct) =>
            (await service.UpdateQuantityAsync(itemId, request.Quantity, ct)).ToHttpResult());

        cart.MapDelete("/items/{itemId:guid}", async (Guid itemId, ICartService service, CancellationToken ct) =>
            (await service.RemoveAsync(itemId, ct)).ToHttpResult());

        cart.MapDelete("/", async (ICartService service, CancellationToken ct) =>
            (await service.ClearAsync(ct)).ToHttpResult());

        cart.MapPost("/coupon", async ([FromBody] ApplyCouponRequest request, ICartService service, CancellationToken ct) =>
            (await service.ApplyCouponAsync(request.Code, ct)).ToHttpResult());

        cart.MapDelete("/coupon", async (ICartService service, CancellationToken ct) =>
            (await service.RemoveCouponAsync(ct)).ToHttpResult());
    }

    /// <summary>
    /// Issues the guest cart cookie when there isn't one.
    /// </summary>
    /// <remarks>
    /// Not HttpOnly: the storefront reads it to keep its local cart state aligned. That is
    /// acceptable because the value is an opaque random id, not a credential — it grants access to
    /// nothing except an anonymous basket, and is replaced by the account's cart at sign-in.
    /// </remarks>
    private static void EnsureAnonymousId(HttpContext http)
    {
        if (!string.IsNullOrWhiteSpace(http.Request.Cookies[CurrentUser.AnonymousIdCookie]))
        {
            return;
        }

        http.Response.Cookies.Append(
            CurrentUser.AnonymousIdCookie,
            Guid.CreateVersion7().ToString("N"),
            new CookieOptions
            {
                HttpOnly = false,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                IsEssential = true
            });
    }

    // ==========================================================================================
    // Wishlist — requires an account
    // ==========================================================================================

    private static void MapWishlist(IEndpointRouteBuilder app)
    {
        var wishlist = app.MapGroup("/api/wishlist").WithTags("Wishlist").RequireAuthorization();

        wishlist.MapGet("/", async (IWishlistService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(ct)));

        wishlist.MapGet("/ids", async (IWishlistService service, CancellationToken ct) =>
                Results.Ok(await service.GetProductIdsAsync(ct)))
            .WithSummary("Saved product ids, so a grid can render every heart in one request.");

        wishlist.MapPost("/", async ([FromBody] AddToWishlistRequest request, IWishlistService service, CancellationToken ct) =>
            (await service.AddAsync(request, ct)).ToHttpResult());

        wishlist.MapPost("/{productId:guid}/toggle", async (Guid productId, IWishlistService service, CancellationToken ct) =>
            (await service.ToggleAsync(productId, ct)).ToHttpResult());

        wishlist.MapDelete("/{productId:guid}", async (Guid productId, IWishlistService service, CancellationToken ct) =>
            (await service.RemoveAsync(productId, ct)).ToHttpResult());
    }

    // ==========================================================================================
    // Checkout — anonymous allowed (guest checkout is a store setting)
    // ==========================================================================================

    private static void MapCheckout(IEndpointRouteBuilder app)
    {
        var checkout = app.MapGroup("/api/checkout").WithTags("Checkout").AllowAnonymous();

        checkout.MapGet("/summary", async (
                    ICheckoutService service,
                    CancellationToken ct,
                    string? region = null,
                    Guid? shippingMethodId = null) =>
                (await service.GetSummaryAsync(region, shippingMethodId, ct)).ToHttpResult())
            .WithSummary("Priced preview including shipping options and any blockers.");

        checkout.MapPost("/", async ([FromBody] CheckoutRequest request, ICheckoutService service, CancellationToken ct) =>
                (await service.PlaceOrderAsync(request, ct)).ToHttpResult())
            // Tight limit: order placement is expensive and a natural target for abuse.
            .RequireRateLimiting("auth")
            .WithSummary("Place the order. Transactional; re-validates stock, price and coupon.");
    }

    // ==========================================================================================
    // Customer orders
    // ==========================================================================================

    private static void MapCustomerOrders(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/orders").WithTags("Orders");

        orders.MapGet("/", async (IOrderService service, CancellationToken ct, int page = 1, int pageSize = 10) =>
                Results.Ok(await service.GetMyOrdersAsync(page, pageSize, ct)))
            .RequireAuthorization();

        orders.MapGet("/{id:guid}", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.GetMyOrderAsync(id, ct)).ToHttpResult())
            .RequireAuthorization();

        orders.MapPost("/{id:guid}/cancel", async (Guid id, [FromBody] CancelOrderRequest request, IOrderService service, CancellationToken ct) =>
                (await service.CancelMyOrderAsync(id, request.Reason, ct)).ToHttpResult())
            .RequireAuthorization();

        // Guest tracking: needs the order number *and* the email it was placed with, so an order
        // number on its own reveals nothing.
        orders.MapGet("/track", async (IOrderService service, CancellationToken ct, string orderNumber = "", string email = "") =>
                (await service.TrackAsync(orderNumber, email, ct)).ToHttpResult())
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithSummary("Track an order by number and email.");
    }

    // ==========================================================================================
    // Admin orders
    // ==========================================================================================

    private static void MapAdminOrders(IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/api/admin/orders").WithTags("Admin · Orders");

        orders.MapGet("/", async (
                    IOrderService service,
                    CancellationToken ct,
                    string? search = null,
                    OrderStatus? status = null,
                    PaymentStatus? paymentStatus = null,
                    Guid? customerId = null,
                    DateTimeOffset? from = null,
                    DateTimeOffset? to = null,
                    string? sortBy = null,
                    bool descending = true,
                    int page = 1,
                    int pageSize = 24) =>
                Results.Ok(await service.ListAsync(new OrderQuery
                {
                    Search = search,
                    Status = status,
                    PaymentStatus = paymentStatus,
                    CustomerId = customerId,
                    From = from,
                    To = to,
                    SortBy = sortBy,
                    Descending = descending,
                    Page = page,
                    PageSize = pageSize
                }, ct)))
            .RequirePermission(Permissions.Orders.View);

        orders.MapGet("/{id:guid}", async (Guid id, IOrderService service, CancellationToken ct) =>
                (await service.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Orders.View);

        orders.MapPut("/{id:guid}/status", async (Guid id, [FromBody] UpdateOrderStatusRequest request, IOrderService service, CancellationToken ct) =>
                (await service.UpdateStatusAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Orders.UpdateStatus);

        orders.MapPost("/{id:guid}/shipments", async (Guid id, [FromBody] CreateShipmentRequest request, IOrderService service, CancellationToken ct) =>
                (await service.AddShipmentAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Orders.ManageShipments);
    }

    // ==========================================================================================
    // Notifications
    // ==========================================================================================

    private static void MapNotifications(IEndpointRouteBuilder app)
    {
        var notifications = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        notifications.MapGet("/", async (INotificationService service, CancellationToken ct, int page = 1, int pageSize = 20, bool unreadOnly = false) =>
            Results.Ok(await service.GetMineAsync(page, pageSize, unreadOnly, ct)));

        notifications.MapGet("/unread-count", async (INotificationService service, CancellationToken ct) =>
                Results.Ok(new { count = await service.GetUnreadCountAsync(ct) }))
            .WithSummary("Unread count for the bell badge.");

        notifications.MapPost("/{id:guid}/read", async (Guid id, INotificationService service, CancellationToken ct) =>
        {
            await service.MarkReadAsync(id, ct);
            return Results.NoContent();
        });

        notifications.MapPost("/read-all", async (INotificationService service, CancellationToken ct) =>
        {
            await service.MarkAllReadAsync(ct);
            return Results.NoContent();
        });
    }

    // ==========================================================================================
    // Shipping
    // ==========================================================================================

    private static void MapShipping(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/shipping/quotes", async (
                    IShippingService service,
                    CancellationToken ct,
                    string? region = null,
                    decimal subtotal = 0,
                    decimal weightKg = 0) =>
                Results.Ok(await service.GetQuotesAsync(region, subtotal, weightKg, ct)))
            .WithTags("Shipping")
            .AllowAnonymous();

        var admin = app.MapGroup("/api/admin/shipping").WithTags("Admin · Shipping");

        admin.MapGet("/zones", async (IShippingService service, CancellationToken ct) =>
                Results.Ok(await service.GetZonesAsync(ct)))
            .RequirePermission(Permissions.Shipping.View);

        admin.MapPost("/zones", async ([FromBody] ShippingZoneDto request, IShippingService service, CancellationToken ct) =>
                (await service.SaveZoneAsync(null, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Shipping.Manage);

        admin.MapPut("/zones/{id:guid}", async (Guid id, [FromBody] ShippingZoneDto request, IShippingService service, CancellationToken ct) =>
                (await service.SaveZoneAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Shipping.Manage);

        admin.MapDelete("/zones/{id:guid}", async (Guid id, IShippingService service, CancellationToken ct) =>
                (await service.DeleteZoneAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Shipping.Manage);

        admin.MapPost("/methods", async ([FromBody] ShippingMethodDto request, IShippingService service, CancellationToken ct) =>
                (await service.SaveMethodAsync(null, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Shipping.Manage);

        admin.MapPut("/methods/{id:guid}", async (Guid id, [FromBody] ShippingMethodDto request, IShippingService service, CancellationToken ct) =>
                (await service.SaveMethodAsync(id, request, ct)).ToHttpResult())
            .RequirePermission(Permissions.Shipping.Manage);

        admin.MapDelete("/methods/{id:guid}", async (Guid id, IShippingService service, CancellationToken ct) =>
                (await service.DeleteMethodAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.Shipping.Manage);
    }
}
