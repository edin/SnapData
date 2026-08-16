namespace SnapData.Migrations;

public enum MigrationColumnType
{
    Int16,
    Int32,
    Int64,
    String,
    Text,
    Boolean,
    Decimal,
    Float,
    Double,
    Guid,
    Binary,
    Date,
    Time,
    DateTime,
    DateTimeOffset,
    Json
}

public enum MigrationSortOrder
{
    Ascending,
    Descending
}
