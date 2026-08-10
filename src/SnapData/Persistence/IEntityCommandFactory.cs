namespace SnapData;

public interface IEntityCommandFactory
{
    InsertQueryBuilder Insert<T>(T entity)
        where T : class;

    UpdateQueryBuilder Update<T>(T entity)
        where T : class;

    DeleteQueryBuilder Delete<T>(T entity)
        where T : class;
}
