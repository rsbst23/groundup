using GroundUp.Sample.Data;
using GroundUp.Sample.Entities;

namespace GroundUp.Tests.Integration.Fixtures;

/// <summary>
/// Seeds deterministic test data for order filtering/sorting integration tests.
/// Creates 2 customers and 10 orders with varied values across all supported data types.
/// </summary>
public static class OrderTestData
{
    public static readonly Guid AliceCustomerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid BobCustomerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static void SeedTestOrders(SampleDbContext context)
    {
        var alice = new Customer
        {
            Id = AliceCustomerId,
            Name = "Alice Corp",
            Email = "alice@example.com",
            Phone = "555-0001",
            CreatedAt = DateTime.UtcNow
        };

        var bob = new Customer
        {
            Id = BobCustomerId,
            Name = "Bob Inc",
            Email = "bob@example.com",
            Phone = "555-0002",
            CreatedAt = DateTime.UtcNow
        };

        context.Customers.AddRange(alice, bob);
        context.SaveChanges();

        var orders = new List<Order>
        {
            new()
            {
                OrderNumber = "ORD-001",
                CustomerId = AliceCustomerId,
                OrderDate = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                Total = 150.00m,
                Status = "Pending",
                ItemCount = 1,
                IsUrgent = false,
                Priority = OrderPriority.Low,
                ShippingWeight = 0.5,
                TrackingNumber = 1000000001,
                ShipDate = null,
                DiscountPercent = null,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-002",
                CustomerId = AliceCustomerId,
                OrderDate = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc),
                Total = 75.00m,
                Status = "Shipped",
                ItemCount = 2,
                IsUrgent = true,
                Priority = OrderPriority.Normal,
                ShippingWeight = 2.0,
                TrackingNumber = 1000000002,
                ShipDate = new DateOnly(2026, 2, 12),
                DiscountPercent = 5,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-003",
                CustomerId = BobCustomerId,
                OrderDate = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                Total = 500.00m,
                Status = "Delivered",
                ItemCount = 3,
                IsUrgent = false,
                Priority = OrderPriority.Normal,
                ShippingWeight = 5.0,
                TrackingNumber = 1000000003,
                ShipDate = new DateOnly(2026, 3, 10),
                DiscountPercent = 10,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-004",
                CustomerId = BobCustomerId,
                OrderDate = new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
                Total = 1200.00m,
                Status = "Pending",
                ItemCount = 4,
                IsUrgent = true,
                Priority = OrderPriority.High,
                ShippingWeight = 8.0,
                TrackingNumber = 1000000004,
                ShipDate = null,
                DiscountPercent = null,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-005",
                CustomerId = AliceCustomerId,
                OrderDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                Total = 50.00m,
                Status = "Cancelled",
                ItemCount = 5,
                IsUrgent = false,
                Priority = OrderPriority.Low,
                ShippingWeight = 1.0,
                TrackingNumber = 1000000005,
                ShipDate = null,
                DiscountPercent = 15,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-006",
                CustomerId = AliceCustomerId,
                OrderDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
                Total = 3000.00m,
                Status = "Shipped",
                ItemCount = 6,
                IsUrgent = true,
                Priority = OrderPriority.Critical,
                ShippingWeight = 12.0,
                TrackingNumber = 1000000006,
                ShipDate = new DateOnly(2026, 6, 18),
                DiscountPercent = 20,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-007",
                CustomerId = BobCustomerId,
                OrderDate = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
                Total = 800.00m,
                Status = "Delivered",
                ItemCount = 7,
                IsUrgent = false,
                Priority = OrderPriority.Normal,
                ShippingWeight = 10.0,
                TrackingNumber = 1000000007,
                ShipDate = new DateOnly(2026, 7, 15),
                DiscountPercent = null,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-008",
                CustomerId = BobCustomerId,
                OrderDate = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc),
                Total = 250.00m,
                Status = "Pending",
                ItemCount = 8,
                IsUrgent = true,
                Priority = OrderPriority.High,
                ShippingWeight = 15.0,
                TrackingNumber = 1000000008,
                ShipDate = null,
                DiscountPercent = 25,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-009",
                CustomerId = AliceCustomerId,
                OrderDate = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc),
                Total = 5000.00m,
                Status = "Shipped",
                ItemCount = 9,
                IsUrgent = false,
                Priority = OrderPriority.Critical,
                ShippingWeight = 20.0,
                TrackingNumber = 1000000009,
                ShipDate = new DateOnly(2026, 9, 14),
                DiscountPercent = 10,
                CreatedAt = DateTime.UtcNow
            },
            new()
            {
                OrderNumber = "ORD-010",
                CustomerId = BobCustomerId,
                OrderDate = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc),
                Total = 400.00m,
                Status = "Delivered",
                ItemCount = 10,
                IsUrgent = true,
                Priority = OrderPriority.Normal,
                ShippingWeight = 25.0,
                TrackingNumber = 1000000010,
                ShipDate = new DateOnly(2026, 11, 2),
                DiscountPercent = null,
                CreatedAt = DateTime.UtcNow
            }
        };

        context.Orders.AddRange(orders);
        context.SaveChanges();
    }
}
