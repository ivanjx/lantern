namespace Lantern;

internal abstract record ServiceResult;

internal record SuccessServiceResult : ServiceResult;

internal record ErrorServiceResult : ServiceResult;
