using Microsoft.EntityFrameworkCore;
using Store.Application.Commerce;
using Store.Application.Common;
using Store.Domain.Customers;
using Store.Infrastructure.Persistence;

namespace Store.Infrastructure.Commerce;

/// <summary>Compact address row, reused by the customer's address book and the admin customer view.</summary>
public sealed record AddressSummaryDto(
    Guid Id,
    string? Label,
    string FullName,
    string Phone,
    string Line1,
    string? Line2,
    string? District,
    string City,
    string? Region,
    string? PostalCode,
    string CountryCode,
    bool IsDefaultShipping);

public interface IAddressService
{
    Task<IReadOnlyList<AddressDto>> GetMineAsync(CancellationToken ct = default);
    Task<Result<AddressDto>> SaveAsync(Guid? id, SaveAddressRequest request, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);
    Task<Result> SetDefaultAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// The customer's address book.
/// </summary>
/// <remarks>
/// Every operation is scoped to the signed-in customer inside the query, so an address id
/// belonging to someone else simply does not resolve. Orders snapshot their address rather than
/// referencing these rows, so editing an address here never rewrites delivery history.
/// </remarks>
public sealed class AddressService(
    StoreDbContext db,
    ICurrentUser currentUser) : IAddressService
{
    public async Task<IReadOnlyList<AddressDto>> GetMineAsync(CancellationToken ct = default)
    {
        if (await ResolveCustomerIdAsync(ct) is not { } customerId)
        {
            return [];
        }

        return await db.Addresses
            .AsNoTracking()
            .Where(a => a.CustomerId == customerId)
            .OrderByDescending(a => a.IsDefaultShipping)
            .ThenByDescending(a => a.CreatedAt)
            .Select(a => new AddressDto(
                a.Id, a.Label, a.FullName, a.Phone, a.Line1, a.Line2, a.District,
                a.City, a.Region, a.PostalCode, a.CountryCode,
                a.IsDefaultShipping, a.IsDefaultBilling))
            .ToListAsync(ct);
    }

    public async Task<Result<AddressDto>> SaveAsync(
        Guid? id, SaveAddressRequest request, CancellationToken ct = default)
    {
        if (await ResolveCustomerIdAsync(ct) is not { } customerId)
        {
            return Result<AddressDto>.Forbidden("Sign in to manage your addresses.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Line1))
        {
            return Result<AddressDto>.Failure("Name, phone and street address are required.");
        }

        Address address;

        if (id is { } addressId)
        {
            // Ownership is part of the lookup, not a check afterwards.
            var existing = await db.Addresses
                .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId, ct);

            if (existing is null)
            {
                return Result<AddressDto>.NotFound("Address not found.");
            }

            address = existing;
        }
        else
        {
            address = new Address { CustomerId = customerId };
            db.Addresses.Add(address);
        }

        address.Label = request.Label;
        address.FullName = request.FullName.Trim();
        address.Phone = request.Phone.Trim();
        address.Line1 = request.Line1.Trim();
        address.Line2 = request.Line2;
        address.District = request.District;
        address.City = string.IsNullOrWhiteSpace(request.City) ? "Hong Kong" : request.City.Trim();
        address.Region = request.Region;
        // Left optional deliberately — Hong Kong has no postal codes.
        address.PostalCode = request.PostalCode;
        address.CountryCode = string.IsNullOrWhiteSpace(request.CountryCode) ? "HK" : request.CountryCode;

        // The first address a customer saves becomes their default automatically, so checkout
        // always has something pre-selected.
        var isFirst = !await db.Addresses.AnyAsync(a => a.CustomerId == customerId && a.Id != address.Id, ct);

        address.IsDefaultShipping = request.IsDefaultShipping || isFirst;
        address.IsDefaultBilling = request.IsDefaultBilling || isFirst;

        if (address.IsDefaultShipping)
        {
            await ClearOtherDefaultsAsync(customerId, address.Id, ct);
        }

        await db.SaveChangesAsync(ct);

        return Result<AddressDto>.Success(new AddressDto(
            address.Id, address.Label, address.FullName, address.Phone, address.Line1,
            address.Line2, address.District, address.City, address.Region,
            address.PostalCode, address.CountryCode,
            address.IsDefaultShipping, address.IsDefaultBilling));
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await ResolveCustomerIdAsync(ct) is not { } customerId)
        {
            return Result.Forbidden("Sign in to manage your addresses.");
        }

        var address = await db.Addresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct);

        if (address is null)
        {
            return Result.NotFound("Address not found.");
        }

        var wasDefault = address.IsDefaultShipping;

        db.Addresses.Remove(address);
        await db.SaveChangesAsync(ct);

        // Promote another address so the customer is never left without a default.
        if (wasDefault)
        {
            var replacement = await db.Addresses
                .Where(a => a.CustomerId == customerId)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (replacement is not null)
            {
                replacement.IsDefaultShipping = true;
                replacement.IsDefaultBilling = true;
                await db.SaveChangesAsync(ct);
            }
        }

        return Result.Success();
    }

    public async Task<Result> SetDefaultAsync(Guid id, CancellationToken ct = default)
    {
        if (await ResolveCustomerIdAsync(ct) is not { } customerId)
        {
            return Result.Forbidden("Sign in to manage your addresses.");
        }

        var address = await db.Addresses
            .FirstOrDefaultAsync(a => a.Id == id && a.CustomerId == customerId, ct);

        if (address is null)
        {
            return Result.NotFound("Address not found.");
        }

        await ClearOtherDefaultsAsync(customerId, id, ct);

        address.IsDefaultShipping = true;
        address.IsDefaultBilling = true;

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Set-based, so making one address default cannot leave two marked as default.</summary>
    private async Task ClearOtherDefaultsAsync(Guid customerId, Guid keepId, CancellationToken ct) =>
        await db.Addresses
            .Where(a => a.CustomerId == customerId && a.Id != keepId && (a.IsDefaultShipping || a.IsDefaultBilling))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsDefaultShipping, false)
                .SetProperty(a => a.IsDefaultBilling, false), ct);

    private async Task<Guid?> ResolveCustomerIdAsync(CancellationToken ct)
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
}
