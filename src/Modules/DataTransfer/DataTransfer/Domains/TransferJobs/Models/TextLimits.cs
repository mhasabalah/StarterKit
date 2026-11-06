namespace DataTransfer.Domains.TransferJobs.Models;

/// <summary>Column-length guard shared by the job entities (values longer than the column are cut, never rejected).</summary>
internal static class TextLimits
{
    public static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
