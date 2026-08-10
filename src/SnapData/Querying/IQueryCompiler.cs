namespace SnapData;

public interface IQueryCompiler
{
    bool SupportsReturning => false;

    SqlQuery Compile(ISqlQueryBuilder query);
}
