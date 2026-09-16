using System.Diagnostics.CodeAnalysis;

namespace EmailBlastCommunicationServices.Domain.Rules;

public static class DeliveryStatus
{
    public const string Queued = "Queued";
    public const string Sent = "Sent";
    public const string Failed = "Failed";

    public static bool IsReportStatus([NotNullWhen(true)] string? status) => status is
        "OutForDelivery" or "Delivered" or "Bounced" or Failed or "Dropped" or
        "Suppressed" or "Quarantined" or "FilteredSpam" or "Expanded";

    public static bool CanApplyReport(string current, string next) =>
        IsReportStatus(next) && current is (Queued or Sent or "OutForDelivery" or "Expanded") &&
        !(current == "Expanded" && next == "OutForDelivery");
}
