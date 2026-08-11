namespace SnapData;

public interface IQueryCompiler
{
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
