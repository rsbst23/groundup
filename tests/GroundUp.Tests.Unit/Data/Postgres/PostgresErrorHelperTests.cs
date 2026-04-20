using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GroundUp.Tests.Unit.Data.Postgres;

/// <summary>
/// Unit tests for <see cref="PostgresErrorHelper"/>.
/// </summary>
public sealed class PostgresErrorHelperTests
{
    private static PostgresException CreatePostgresException(string sqlState)
    {
        return new PostgresException(
            messageText: "test",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState);
    }

    [Fact]
    public void IsUniqueConstraintViolation_PostgresException23505_ReturnsTrue()
    {
        // Arrange
        var pgEx = CreatePostgresException("23505");
        var dbEx = new DbUpdateException("test", pgEx);

        // Act
        var result = PostgresErrorHelper.IsUniqueConstraintViolation(dbEx);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsUniqueConstraintViolation_PostgresExceptionDifferentCode_ReturnsFalse()
    {
        // Arrange
        var pgEx = CreatePostgresException("23503");
        var dbEx = new DbUpdateException("test", pgEx);

        // Act
        var result = PostgresErrorHelper.IsUniqueConstraintViolation(dbEx);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsUniqueConstraintViolation_NoInnerException_ReturnsFalse()
    {
        // Arrange
        var dbEx = new DbUpdateException("test");

        // Act
        var result = PostgresErrorHelper.IsUniqueConstraintViolation(dbEx);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsUniqueConstraintViolation_Null_ReturnsFalse()
    {
        // Act
        var result = PostgresErrorHelper.IsUniqueConstraintViolation(null);

        // Assert
        Assert.False(result);
    }
}
