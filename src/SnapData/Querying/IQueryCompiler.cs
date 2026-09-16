namespace SnapData;

public interface IQueryCompiler
{
    QueryCapabilities Capabilities => QueryCapabilities.None;

    /// <summary>Plan generated-value hydration with mapping metadata. The column-only overload
    /// remains available for custom compilers and explicit low-level calls.</summary>
    GeneratedInsertPlan CompileGeneratedEntityInsert(
        InsertQueryBuilder insert, IReadOnlyList<PropertyMapping> generatedProperties)
    {
        ArgumentNullException.ThrowIfNull(generatedProperties);
        return CompileGeneratedInsert(insert, generatedProperties.Select(property => property.Column).ToArray());
    }

    GeneratedInsertPlan CompileGeneratedInsert(
        InsertQueryBuilder insert,
        IReadOnlyList<ColumnReference> generatedColumns)
    {
        ArgumentNullException.ThrowIfNull(insert);
        ArgumentNullException.ThrowIfNull(generatedColumns);
        if (generatedColumns.Count == 0)
        {
            throw new ArgumentException(
                "At least one generated column is required.",
                nameof(generatedColumns));
        }

        insert.Returning(generatedColumns[0], generatedColumns.Skip(1).ToArray());
        return new GeneratedInsertPlan(Compile(insert));
    }

    SqlQuery Compile(ISqlQueryBuilder query);
}

public sealed record GeneratedInsertPlan(
    SqlQuery Command,
    SqlQuery? FollowUpQuery = null);
