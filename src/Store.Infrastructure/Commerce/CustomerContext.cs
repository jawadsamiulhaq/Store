using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Store.Application.Common;
using Store.Domain.Customers;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

/// <summary>
/// Resolves the signed-in user's storefront profile.
/// </summary>
/// <remarks>
/// Five services — wishlist, addresses, cart, checkout, orders — plus reviews each carried their
/// own private copy of "find the Customer row for this user, return null if there isn't one". That
/// duplication was not the problem in itself; the problem was that a null meant "not signed in"
/// everywhere, and a signed-in staff account also produces a null.
/// <para>
/// So a staff member browsing their own shop was told to sign in, which they had already done.
/// </para>
/// </remarks>
public interface ICustomerContext
{
    /// <summary>
    /// The profile id, or null when there is none. Creates nothing.
    /// </summary>
    /// <remarks>
    /// Used by reads. A GET must not write: filling the admin customer list with everyone who has
    /// ever loaded a product page would make "customers" mean "visitors".
    /// </remarks>
    Task<Guid?> GetIdAsync(CancellationToken ct = default);

    /// <summary>
    /// The profile id, creating one if the user is signed in and does not have it yet.
    /// </summary>
    /// <remarks>
    /// Used by writes. <see cref="Customer"/> is kept separate from the identity user so that
    /// staff accounts carry no commerce columns — and that stays true until the moment a staff
    /// member actually saves something, places an order or stores an address. At that point they
    /// really are a customer of the shop, and belonging in the customer list is correct rather
    /// than surprising. Browsing alone still creates nothing.
    /// </remarks>
    Task<Guid?> GetOrCreateIdAsync(CancellationToken ct = default);
}

public sealed class CustomerContext(
    StoreDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    ILogger<CustomerContext> logger) : ICustomerContext
{
    public async Task<Guid?> GetIdAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        return await db.Customers
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> GetOrCreateIdAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        if (await GetIdAsync(ct) is { } existing)
        {
            return existing;
        }

        var customer = new Customer
        {
            UserId = userId,
            AcceptsMarketing = false,
            CreatedAt = clock.UtcNow
        };

        db.Customers.Add(customer);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two concurrent writes — a save and an add-to-cart from two tabs — can both find no
            // profile and both try to insert. The unique index on UserId makes the loser fail, and
            // the correct response is to use the row the winner created, not to surface an error
            // for something that succeeded.
            db.Entry(customer).State = EntityState.Detached;

            return await GetIdAsync(ct);
        }

        logger.LogInformation(
            "Created a storefront profile for user {UserId} on first commerce action", userId);

        return customer.Id;
    }
}
