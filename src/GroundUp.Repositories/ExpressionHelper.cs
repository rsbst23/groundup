using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;

namespace GroundUp.Repositories;

/// <summary>
/// Builds LINQ expressions dynamically from string property names and values.
/// Used by <see cref="BaseRepository{TEntity, TDto}"/> to translate
/// <see cref="GroundUp.Core.Models.FilterParams"/> dictionaries into queryable predicates.
/// All expressions are database-agnostic — they translate to SQL via EF Core's query provider.
/// <para>
/// Supports nested property access via dot notation (e.g., "Customer.Name", "Order.Customer.Email").
/// EF Core translates nested member access to SQL JOINs automatically when navigation properties
/// are configured.
/// </para>
/// <para>
/// Design notes:
/// <list type="bullet">
/// <item>Case-insensitive string comparison uses <c>ToLower()</c> which EF Core translates to SQL <c>LOWER()</c>.
/// This is database-agnostic but prevents index usage on the column. For index-friendly case-insensitive
/// queries, use a provider-specific approach (e.g., Postgres citext columns or collation).</item>
/// <item>All predicate methods return an always-true predicate for invalid inputs (unknown property names,
/// unparseable values) rather than throwing. This provides safe degradation — a bad filter parameter
/// results in no filtering for that criterion, not a crash.</item>
/// <item>All date/time parsing uses <see cref="CultureInfo.InvariantCulture"/> for predictable behavior
/// regardless of server locale.</item>
/// </list>
/// </para>
/// </summary>
public static class ExpressionHelper
{
    /// <summary>
    /// Comparable types that support range operations (>=, &lt;=).
    /// </summary>
    private static readonly HashSet<Type> ComparableTypes = new()
    {
        typeof(int), typeof(long), typeof(short), typeof(byte),
        typeof(uint), typeof(ulong), typeof(ushort),
        typeof(float), typeof(double), typeof(decimal),
        typeof(DateTime), typeof(DateTimeOffset),
        typeof(DateOnly), typeof(TimeOnly), typeof(TimeSpan)
    };

    #region Exact Match

    /// <summary>
    /// Builds an exact-match predicate for the specified property.
    /// String properties use case-insensitive comparison.
    /// Non-string properties (Guid, int, DateTime, DateTimeOffset, DateOnly, TimeOnly, bool, enum)
    /// parse the value and compare for equality. Handles nullable property types.
    /// Supports nested properties via dot notation (e.g., "Customer.Name").
    /// Returns an always-true predicate if the property path does not exist on <typeparamref name="T"/>
    /// or if the value cannot be parsed to the property's type.
    /// </summary>
    public static Expression<Func<T, bool>> BuildPredicate<T>(string propertyName, string value)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var memberInfo = FindPropertyPath(parameter, propertyName);
        if (memberInfo is null)
            return AlwaysTrue<T>();

        var (member, property) = memberInfo.Value;
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        Expression body;
        if (underlyingType == typeof(string))
        {
            body = BuildNullSafeStringOperation(member, value, StringOperation.Equals);
        }
        else
        {
            var typedValue = ConvertTo(value, underlyingType);
            if (typedValue is null)
                return AlwaysTrue<T>();

            body = BuildNullableEqualityCheck(member, property.PropertyType, underlyingType, typedValue);
        }

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    #endregion

    #region String Matching (Contains, StartsWith, EndsWith)

    /// <summary>
    /// Builds a substring-match (contains) predicate for the specified string property.
    /// Uses case-insensitive comparison. Null property values are treated as non-matching.
    /// Supports nested properties via dot notation.
    /// Returns an always-true predicate if the property does not exist or is not a string.
    /// </summary>
    public static Expression<Func<T, bool>> BuildContainsPredicate<T>(string propertyName, string value)
    {
        return BuildStringOperationPredicate<T>(propertyName, value, StringOperation.Contains);
    }

    /// <summary>
    /// Builds a starts-with predicate for the specified string property.
    /// Uses case-insensitive comparison. Null property values are treated as non-matching.
    /// Supports nested properties via dot notation.
    /// Returns an always-true predicate if the property does not exist or is not a string.
    /// </summary>
    public static Expression<Func<T, bool>> BuildStartsWithPredicate<T>(string propertyName, string value)
    {
        return BuildStringOperationPredicate<T>(propertyName, value, StringOperation.StartsWith);
    }

    /// <summary>
    /// Builds an ends-with predicate for the specified string property.
    /// Uses case-insensitive comparison. Null property values are treated as non-matching.
    /// Supports nested properties via dot notation.
    /// Returns an always-true predicate if the property does not exist or is not a string.
    /// </summary>
    public static Expression<Func<T, bool>> BuildEndsWithPredicate<T>(string propertyName, string value)
    {
        return BuildStringOperationPredicate<T>(propertyName, value, StringOperation.EndsWith);
    }

    #endregion

    #region Multi-Value (IN Clause)

    /// <summary>
    /// Builds a multi-value (IN clause) predicate for the specified property.
    /// Generates an expression equivalent to SQL: WHERE Property IN (value1, value2, ...).
    /// Parses each string value to the property's type. Handles nullable properties.
    /// String comparisons are case-insensitive. Supports nested properties via dot notation.
    /// Returns an always-true predicate if the property does not exist or no values can be parsed.
    /// </summary>
    public static Expression<Func<T, bool>> BuildMultiValuePredicate<T>(string propertyName, List<string> values)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var memberInfo = FindPropertyPath(parameter, propertyName);
        if (memberInfo is null || values.Count == 0)
            return AlwaysTrue<T>();

        var (member, property) = memberInfo.Value;
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (underlyingType == typeof(string))
        {
            return BuildStringMultiValuePredicate<T>(parameter, member, values);
        }

        return BuildTypedMultiValuePredicate<T>(parameter, member, property.PropertyType, underlyingType, values);
    }

    #endregion

    #region Range

    /// <summary>
    /// Builds a range predicate (>= min AND/OR &lt;= max) for the specified property.
    /// Supports numeric types (int, long, short, byte, float, double, decimal),
    /// DateTime, DateTimeOffset, DateOnly, TimeOnly, and TimeSpan. Handles nullable properties.
    /// Supports nested properties via dot notation.
    /// Returns an always-true predicate if the property does not exist, is not a comparable type,
    /// or if values are unparseable.
    /// </summary>
    public static Expression<Func<T, bool>> BuildRangePredicate<T>(
        string propertyName, string? minValue, string? maxValue)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var memberInfo = FindPropertyPath(parameter, propertyName);
        if (memberInfo is null)
            return AlwaysTrue<T>();

        var (member, property) = memberInfo.Value;
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (!ComparableTypes.Contains(underlyingType))
            return AlwaysTrue<T>();

        var isNullable = property.PropertyType != underlyingType;

        Expression compareTarget = isNullable
            ? Expression.Property(member, "Value")
            : member;

        Expression? rangeBody = null;

        if (!string.IsNullOrWhiteSpace(minValue))
        {
            var min = ConvertTo(minValue, underlyingType);
            if (min is not null)
            {
                var minConstant = Expression.Constant(min, underlyingType);
                rangeBody = Expression.GreaterThanOrEqual(compareTarget, minConstant);
            }
        }

        if (!string.IsNullOrWhiteSpace(maxValue))
        {
            var max = ConvertTo(maxValue, underlyingType);
            if (max is not null)
            {
                var maxConstant = Expression.Constant(max, underlyingType);
                var maxExpr = Expression.LessThanOrEqual(compareTarget, maxConstant);
                rangeBody = rangeBody is null ? maxExpr : Expression.AndAlso(rangeBody, maxExpr);
            }
        }

        if (rangeBody is null)
            return AlwaysTrue<T>();

        if (isNullable)
        {
            var hasValue = Expression.Property(member, "HasValue");
            rangeBody = Expression.AndAlso(hasValue, rangeBody);
        }

        return Expression.Lambda<Func<T, bool>>(rangeBody, parameter);
    }

    /// <summary>
    /// Builds a date range predicate for the specified DateTime, DateTimeOffset, or DateOnly property.
    /// Parses date strings using invariant culture and applies >= min AND/OR &lt;= max comparisons.
    /// Supports nested properties via dot notation.
    /// Returns an always-true predicate if the property does not exist,
    /// is not a date type, or if date strings are unparseable.
    /// </summary>
    public static Expression<Func<T, bool>> BuildDateRangePredicate<T>(
        string propertyName, string? minDate, string? maxDate)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var memberInfo = FindPropertyPath(parameter, propertyName);
        if (memberInfo is null)
            return AlwaysTrue<T>();

        var (_, property) = memberInfo.Value;
        var underlyingType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (underlyingType != typeof(DateTime) && underlyingType != typeof(DateTimeOffset) && underlyingType != typeof(DateOnly))
            return AlwaysTrue<T>();

        string? validMin = null;
        string? validMax = null;

        if (!string.IsNullOrWhiteSpace(minDate) && TryParseDate(minDate, underlyingType))
            validMin = minDate;
        if (!string.IsNullOrWhiteSpace(maxDate) && TryParseDate(maxDate, underlyingType))
            validMax = maxDate;

        if (validMin is null && validMax is null)
            return AlwaysTrue<T>();

        return BuildRangePredicate<T>(propertyName, validMin, validMax);
    }

    #endregion

    #region Search (Free-Text Across String Properties)

    /// <summary>
    /// Builds a free-text search predicate that matches the search term against all string
    /// properties on <typeparamref name="T"/> using case-insensitive substring matching.
    /// Generates: x.Prop1.ToLower().Contains(term) || x.Prop2.ToLower().Contains(term) || ...
    /// Only searches top-level string properties (not nested navigation properties).
    /// Returns an always-true predicate if the search term is null/empty or the type has no string properties.
    /// </summary>
    public static Expression<Func<T, bool>> BuildSearchPredicate<T>(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return AlwaysTrue<T>();

        var stringProperties = typeof(T).GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.CanRead)
            .ToList();

        if (stringProperties.Count == 0)
            return AlwaysTrue<T>();

        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? combinedBody = null;

        foreach (var prop in stringProperties)
        {
            var member = Expression.Property(parameter, prop);
            var containsExpr = BuildNullSafeStringOperation(member, searchTerm, StringOperation.Contains);

            combinedBody = combinedBody is null
                ? containsExpr
                : Expression.OrElse(combinedBody, containsExpr);
        }

        return Expression.Lambda<Func<T, bool>>(combinedBody!, parameter);
    }

    #endregion

    #region Sorting

    /// <summary>
    /// Applies dynamic sorting to the queryable based on a sort expression.
    /// Supports single and multi-column sorting, and nested properties via dot notation.
    /// <para>
    /// Formats:
    /// <list type="bullet">
    /// <item>Single column: "PropertyName" (ascending) or "PropertyName desc" (descending)</item>
    /// <item>Multi-column: "PropertyName, OtherProperty desc" (comma-separated)</item>
    /// <item>Nested: "Customer.Name" or "Customer.Name desc"</item>
    /// </list>
    /// </para>
    /// Returns the queryable unchanged if no valid sort columns are found.
    /// </summary>
    public static IQueryable<T> ApplySorting<T>(IQueryable<T> query, string? sortExpression)
    {
        if (string.IsNullOrWhiteSpace(sortExpression))
            return query;

        var columns = sortExpression.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var isFirst = true;

        foreach (var column in columns)
        {
            var parts = column.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            var propertyPath = parts[0];
            var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);

            var parameter = Expression.Parameter(typeof(T), "x");
            var memberInfo = FindPropertyPath(parameter, propertyPath);
            if (memberInfo is null)
                continue;

            var (member, property) = memberInfo.Value;
            var lambda = Expression.Lambda(member, parameter);

            string methodName;
            if (isFirst)
            {
                methodName = descending ? "OrderByDescending" : "OrderBy";
                isFirst = false;
            }
            else
            {
                methodName = descending ? "ThenByDescending" : "ThenBy";
            }

            var method = typeof(Queryable).GetMethods()
                .First(m => m.Name == methodName && m.GetParameters().Length == 2);

            var generic = method.MakeGenericMethod(typeof(T), property.PropertyType);
            query = (IQueryable<T>)generic.Invoke(null, new object[] { query, lambda })!;
        }

        return query;
    }

    #endregion

    #region Private Helpers

    private enum StringOperation
    {
        Equals,
        Contains,
        StartsWith,
        EndsWith
    }

    /// <summary>
    /// Resolves a property path (supports dot notation for nested properties).
    /// Returns the final MemberExpression and PropertyInfo, or null if any segment is invalid.
    /// <para>
    /// Examples:
    /// <list type="bullet">
    /// <item>"Name" → x.Name</item>
    /// <item>"Customer.Name" → x.Customer.Name</item>
    /// <item>"Order.Customer.Email" → x.Order.Customer.Email</item>
    /// </list>
    /// </para>
    /// </summary>
    private static (Expression Member, PropertyInfo Property)? FindPropertyPath(
        Expression root, string propertyPath)
    {
        var segments = propertyPath.Split('.');
        Expression current = root;
        PropertyInfo? property = null;
        var currentType = root.Type;

        foreach (var segment in segments)
        {
            property = currentType.GetProperties()
                .FirstOrDefault(p => string.Equals(p.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (property is null)
                return null;

            current = Expression.Property(current, property);
            currentType = property.PropertyType;

            // For nullable types, we need the underlying type for further navigation
            var underlying = Nullable.GetUnderlyingType(currentType);
            if (underlying is not null && segments.Length > 1)
                currentType = underlying;
        }

        return property is null ? null : (current, property);
    }

    private static Expression<Func<T, bool>> AlwaysTrue<T>()
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, bool>>(Expression.Constant(true), parameter);
    }

    /// <summary>
    /// Builds a null-safe string operation expression:
    /// x.Property != null &amp;&amp; x.Property.ToLower().{Operation}(value.ToLower())
    /// </summary>
    private static Expression BuildNullSafeStringOperation(
        Expression member, string value, StringOperation operation)
    {
        var toLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        var nullCheck = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        var memberLower = Expression.Call(member, toLowerMethod);
        var valueLower = Expression.Constant(value.ToLower(CultureInfo.InvariantCulture), typeof(string));

        Expression operationExpr = operation switch
        {
            StringOperation.Equals => Expression.Equal(memberLower, valueLower),
            StringOperation.Contains => Expression.Call(memberLower,
                typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!, valueLower),
            StringOperation.StartsWith => Expression.Call(memberLower,
                typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!, valueLower),
            StringOperation.EndsWith => Expression.Call(memberLower,
                typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!, valueLower),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        return Expression.AndAlso(nullCheck, operationExpr);
    }

    /// <summary>
    /// Builds a string operation predicate with property validation.
    /// </summary>
    private static Expression<Func<T, bool>> BuildStringOperationPredicate<T>(
        string propertyName, string value, StringOperation operation)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var memberInfo = FindPropertyPath(parameter, propertyName);
        if (memberInfo is null)
            return AlwaysTrue<T>();

        var (member, property) = memberInfo.Value;
        if (property.PropertyType != typeof(string))
            return AlwaysTrue<T>();

        var body = BuildNullSafeStringOperation(member, value, operation);
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>
    /// Builds an equality check that handles nullable types correctly.
    /// For nullable: x.Property.HasValue &amp;&amp; x.Property.Value == typedValue
    /// For non-nullable: x.Property == typedValue
    /// </summary>
    private static Expression BuildNullableEqualityCheck(
        Expression member, Type propertyType, Type underlyingType, object typedValue)
    {
        var isNullable = propertyType != underlyingType;
        var constant = Expression.Constant(typedValue, underlyingType);

        if (isNullable)
        {
            var hasValue = Expression.Property(member, "HasValue");
            var memberValue = Expression.Property(member, "Value");
            var equals = Expression.Equal(memberValue, constant);
            return Expression.AndAlso(hasValue, equals);
        }

        return Expression.Equal(member, constant);
    }

    /// <summary>
    /// Builds a case-insensitive string multi-value predicate:
    /// x.Property != null &amp;&amp; lowerValues.Contains(x.Property.ToLower())
    /// </summary>
    private static Expression<Func<T, bool>> BuildStringMultiValuePredicate<T>(
        ParameterExpression parameter, Expression member, List<string> values)
    {
        var lowerValues = values.Select(v => v.ToLower(CultureInfo.InvariantCulture)).ToList();
        var toLowerMethod = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
        var containsMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(string));

        var nullCheck = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        var memberLower = Expression.Call(member, toLowerMethod);
        var valuesConstant = Expression.Constant(lowerValues, typeof(List<string>));
        var contains = Expression.Call(containsMethod, valuesConstant, memberLower);
        var body = Expression.AndAlso(nullCheck, contains);

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>
    /// Builds a typed multi-value predicate for non-string properties.
    /// Parses values, builds a typed list, and uses Enumerable.Contains.
    /// </summary>
    private static Expression<Func<T, bool>> BuildTypedMultiValuePredicate<T>(
        ParameterExpression parameter, Expression member,
        Type propertyType, Type underlyingType, List<string> values)
    {
        var parsedValues = values
            .Select(v => ConvertTo(v, underlyingType))
            .Where(v => v is not null)
            .ToList();

        if (parsedValues.Count == 0)
            return AlwaysTrue<T>();

        var listType = typeof(List<>).MakeGenericType(underlyingType);
        var typedList = Activator.CreateInstance(listType)!;
        var addMethod = listType.GetMethod("Add")!;
        foreach (var val in parsedValues)
            addMethod.Invoke(typedList, new[] { val });

        var containsMethod = typeof(Enumerable).GetMethods()
            .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
            .MakeGenericMethod(underlyingType);

        var valuesConstant = Expression.Constant(typedList);
        var isNullable = propertyType != underlyingType;

        if (isNullable)
        {
            var hasValue = Expression.Property(member, "HasValue");
            var memberValue = Expression.Property(member, "Value");
            var contains = Expression.Call(containsMethod, valuesConstant, memberValue);
            var body = Expression.AndAlso(hasValue, contains);
            return Expression.Lambda<Func<T, bool>>(body, parameter);
        }

        var containsExpr = Expression.Call(containsMethod, valuesConstant, member);
        return Expression.Lambda<Func<T, bool>>(containsExpr, parameter);
    }

    /// <summary>
    /// Validates that a date string can be parsed for the given date type.
    /// </summary>
    private static bool TryParseDate(string value, Type dateType)
    {
        if (dateType == typeof(DateTime))
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        if (dateType == typeof(DateTimeOffset))
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        if (dateType == typeof(DateOnly))
            return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        return false;
    }

    /// <summary>
    /// Converts a string value to the specified target type using invariant culture.
    /// Returns null if conversion fails (safe degradation).
    /// </summary>
    private static object? ConvertTo(string value, Type targetType)
    {
        try
        {
            if (targetType == typeof(string)) return value;
            if (targetType == typeof(Guid)) return Guid.Parse(value);
            if (targetType == typeof(DateTime)) return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (targetType == typeof(DateTimeOffset)) return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(DateOnly)) return DateOnly.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(TimeOnly)) return TimeOnly.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(TimeSpan)) return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
            if (targetType == typeof(bool)) return ParseBoolean(value);
            if (targetType.IsEnum) return Enum.Parse(targetType, value, ignoreCase: true);
            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses boolean values robustly, accepting common representations.
    /// </summary>
    private static bool? ParseBoolean(string value)
    {
        return value.ToLower(CultureInfo.InvariantCulture) switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => null
        };
    }

    #endregion
}
