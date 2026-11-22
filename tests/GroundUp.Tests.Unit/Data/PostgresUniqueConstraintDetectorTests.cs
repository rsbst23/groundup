using GroundUp.Data.Postgres;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GroundUp.Tests.Unit.Data;

public sealed class PostgresUniqueConstraintDetectorTests
{
    private readonly PostgresUniqueConstraintDetector _sut = new();

    private static DbUpdateException WithSqlState(string sqlState)
        => new("save failed", new PostgresException("msg", "ERROR", "ERROR", sqlState));

    [Fact]
    public void IsUniqueConstraintViolation_SqlState23505_True()
        => Assert.True(_sut.IsUniqueConstraintViolation(WithSqlState("23505")));

    [Fact]
    public void IsUniqueConstraintViolation_OtherSqlState_False()
        => Assert.False(_sut.IsUniqueConstraintViolation(WithSqlState("23503")));

    [Fact]
    public void IsUniqueConstraintViolation_MessageMentionsDuplicateButNoPostgresException_False()
        => Assert.False(_sut.IsUniqueConstraintViolation(
            new DbUpdateException("duplicate key value violates unique constraint (23505)")));

    [Fact]
    public void IsUniqueConstraintViolation_NotADbUpdateException_False()
        => Assert.False(_sut.IsUniqueConstraintViolation(new InvalidOperationException("23505")));
}
