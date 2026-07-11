namespace Lantern.MikroTik;

internal sealed record MikroTikLeasesResult(
    IReadOnlyList<MikroTikLease> Leases) : SuccessServiceResult;

internal sealed record MikroTikUnauthorizedErrorResult : ErrorServiceResult;

internal sealed record MikroTikInvalidResponseErrorResult : ErrorServiceResult;
