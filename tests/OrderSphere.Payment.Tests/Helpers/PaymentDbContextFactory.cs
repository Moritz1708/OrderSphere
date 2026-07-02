using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using OrderSphere.BuildingBlocks.Security;
using OrderSphere.Payment.Infrastructure.Persistence;

namespace OrderSphere.Payment.Tests.Helpers;

/// <summary>
/// Creates an isolated <see cref="PaymentDbContext"/> backed by a SQLite in-memory database.
/// SQLite is used rather than the EF in-memory provider because the latter does not reliably
/// materialise entities with complex properties (e.g. <c>PaymentRecord.Amount</c>, a <c>Money</c>
/// value object mapped via <c>ComplexProperty</c>).
/// </summary>
internal static class PaymentDbContextFactory
{
    internal static PaymentDbContext Create(ITenantContext? tenantContext = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<PaymentDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new PaymentDbContext(
            options, Substitute.For<IPublisher>(), NullCurrentUser.Instance, tenantContext ?? NullTenantContext.Instance);
        context.Database.EnsureCreated();
        return context;
    }
}
