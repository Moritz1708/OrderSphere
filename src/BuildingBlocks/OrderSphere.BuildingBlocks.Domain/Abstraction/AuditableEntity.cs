using System.ComponentModel.DataAnnotations;

namespace OrderSphere.BuildingBlocks.Abstraction;

/// <summary>
/// Base class for all domain entities. <typeparamref name="TId"/> is a strongly-typed
/// ID struct (e.g. <c>ProductId</c>) that wraps a <see cref="Guid"/> value.
/// EF maps the typed ID to a UUID column via <c>ConfigureConventions</c> in each DbContext.
/// Implements <see cref="IHasDomainEvents"/> so DbContexts can drain raised events after saving.
/// </summary>
public abstract class AuditableEntity<TId> : IAuditableEntity, IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    [Key]
    public TId Id { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public Guid TenantId { get; set; }

    /// <summary>
    /// Adds a domain event to the pending collection.
    /// Call from within aggregate methods to record facts.
    /// </summary>
    protected void RaiseDomainEvent(IDomainEvent domainEvent)
        => _domainEvents.Add(domainEvent);

    /// <inheritdoc />
    public IReadOnlyList<IDomainEvent> PopDomainEvents()
    {
        var copy = _domainEvents.ToList();
        _domainEvents.Clear();
        return copy;
    }
}
