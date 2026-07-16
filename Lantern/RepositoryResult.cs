namespace Lantern;

internal abstract record RepositoryResult;

internal record SuccessRepositoryResult : RepositoryResult;

internal record ErrorRepositoryResult : RepositoryResult;

internal sealed record CanceledRepositoryResult : RepositoryResult;

internal sealed record RepositoryResult<T>(T Value) : SuccessRepositoryResult;
