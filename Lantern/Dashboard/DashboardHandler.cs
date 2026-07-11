using Lantern.Devices;
using Lantern.Monitoring;
using Lantern.Slices;

namespace Lantern.Dashboard;

internal sealed class DashboardHandler(DeviceRepository _repository, PollStatus _pollStatus)
{
    public async Task<IResult> GetAsync(string? feedback, CancellationToken cancellationToken)
    {
        var registryResult = await _repository.GetRegistryAsync(cancellationToken);
        var baselineResult = await _repository.IsInitialScanCompletedAsync(cancellationToken);

        if (registryResult is not RepositoryResult<DeviceRegistry> registry ||
            baselineResult is not RepositoryResult<bool> baseline)
        {
            return Results.Problem("Lantern could not load the device registry.", statusCode: 500);
        }

        var poll = _pollStatus.GetSnapshot();
        var model = new DashboardViewModel(
            registry.Value.Devices,
            registry.Value.UnknownCount,
            registry.Value.TrustedCount,
            baseline.Value,
            poll.LastSuccessfulPollUtc,
            poll.LastError,
            GetFeedback(feedback));
        return Results.RazorSlice<Home, DashboardViewModel>(model);
    }

    public Task<IResult> TrustAsync(string mac, CancellationToken cancellationToken) =>
        ChangeStatusAsync(mac, DeviceStatus.Trusted, "trusted", cancellationToken);

    public Task<IResult> IgnoreAsync(string mac, CancellationToken cancellationToken) =>
        ChangeStatusAsync(mac, DeviceStatus.Ignored, "ignored", cancellationToken);

    public Task<IResult> UntrustAsync(string mac, CancellationToken cancellationToken) =>
        ChangeStatusAsync(mac, DeviceStatus.Unknown, "untrusted", cancellationToken);

    public Task<IResult> UnignoreAsync(string mac, CancellationToken cancellationToken) =>
        ChangeStatusAsync(mac, DeviceStatus.Unknown, "unignored", cancellationToken);

    public async Task<IResult> DeleteAsync(string mac, CancellationToken cancellationToken)
    {
        if (!MacAddress.TryNormalize(mac, out _))
        {
            return Redirect("invalid-mac");
        }

        var lastSuccessfulPollUtc = _pollStatus.GetSnapshot().LastSuccessfulPollUtc;
        if (lastSuccessfulPollUtc is null)
        {
            return Redirect("device-not-deletable");
        }

        return RedirectForResult(
            await _repository.DeleteUnknownOfflineAsync(mac, lastSuccessfulPollUtc.Value, cancellationToken),
            "deleted");
    }

    public async Task<IResult> RenameAsync(string mac, HttpRequest request, CancellationToken cancellationToken)
    {
        if (!MacAddress.TryNormalize(mac, out _))
        {
            return Redirect("invalid-mac");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var friendlyName = form["friendlyName"].ToString().Trim();
        if (friendlyName.Length > 100)
        {
            return Redirect("name-too-long");
        }

        return RedirectForResult(await _repository.RenameAsync(
            mac,
            friendlyName.Length == 0 ? null : friendlyName,
            cancellationToken), "renamed");
    }

    private async Task<IResult> ChangeStatusAsync(
        string mac,
        DeviceStatus status,
        string successFeedback,
        CancellationToken cancellationToken)
    {
        if (!MacAddress.TryNormalize(mac, out _))
        {
            return Redirect("invalid-mac");
        }

        return RedirectForResult(
            await _repository.SetStatusAsync(mac, status, cancellationToken),
            successFeedback);
    }

    private static IResult RedirectForResult(RepositoryResult result, string successFeedback) => result switch
    {
        DeviceNotFoundRepositoryErrorResult => Redirect("device-missing"),
        DeviceNotDeletableRepositoryErrorResult => Redirect("device-not-deletable"),
        SuccessRepositoryResult => Redirect(successFeedback),
        _ => Redirect("update-failed")
    };

    private static IResult Redirect(string feedback) =>
        Results.Redirect($"/?feedback={Uri.EscapeDataString(feedback)}");

    private static DashboardFeedback? GetFeedback(string? feedback) => feedback switch
    {
        "trusted" => new("Device marked as trusted.", false),
        "ignored" => new("Device ignored.", false),
        "untrusted" => new("Device returned to unknown.", false),
        "unignored" => new("Device returned to unknown.", false),
        "renamed" => new("Device name updated.", false),
        "deleted" => new("Unknown offline device removed. It will be reported again if it reconnects.", false),
        "invalid-mac" => new("The device address is invalid.", true),
        "name-too-long" => new("Friendly names must be 100 characters or fewer.", true),
        "device-missing" => new("That device no longer exists.", true),
        "device-not-deletable" => new("Only unknown offline devices can be removed.", true),
        "update-failed" => new("The device could not be updated. Try again.", true),
        _ => null
    };
}
