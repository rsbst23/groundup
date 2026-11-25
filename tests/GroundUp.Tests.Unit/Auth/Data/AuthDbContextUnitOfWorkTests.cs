using FluentAssertions;
using GroundUp.Auth.Data.Postgres;
using GroundUp.Data.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GroundUp.Tests.Unit.Auth.Data;

/// <summary>
/// Unit tests for <see cref="AuthDbContextUnitOfWork"/>: unique violations are recognized only through
/// <see cref="IUniqueConstraintDetector"/>, never by matching exception messages.
/// </summary>
public sealed class AuthDbContextUnitOfWorkTests : IDisposable
{
    private readonly AuthDbContext _context;
    private readonly IUniqueConstraintDetector _detector = Substitute.For<IUniqueConstraintDetector>();
    private readonly AuthDbContextUnitOfWork _sut;

    public AuthDbContextUnitOfWorkTests()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _context = new AuthDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
        _sut = new AuthDbContextUnitOfWork(_context, _detector, NullLogger<AuthDbContextUnitOfWork>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task ExecuteInTransactionAsync_OperationSucceeds_ReturnsOk()
    {
        // Act
        var result = await _sut.ExecuteInTransactionAsync(_ => Task.CompletedTask);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_DetectorSaysUniqueViolation_Returns409()
    {
        // Arrange
        var failure = new DbUpdateException("save failed");
        _detector.IsUniqueConstraintViolation(failure).Returns(true);

        // Act
        var result = await _sut.ExecuteInTransactionAsync(_ => throw failure);

        // Assert
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_MessageMentionsUniqueButDetectorSaysNo_Rethrows()
    {
        // Arrange: the old check matched "duplicate key" in the message; the detector is now the only judge
        var failure = new DbUpdateException("wrapper", new InvalidOperationException("duplicate key value violates unique constraint"));
        _detector.IsUniqueConstraintViolation(failure).Returns(false);

        // Act
        var act = () => _sut.ExecuteInTransactionAsync(_ => throw failure);

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
