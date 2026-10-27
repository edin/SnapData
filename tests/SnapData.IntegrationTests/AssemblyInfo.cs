using Xunit;

// Provider tests share live database catalogs. Schema inspection must not race
// table creation/drop in another test class; explicit concurrency tests still
// exercise parallel operations within their own test method.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
