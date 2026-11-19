using System.IO;

namespace DriveSentinel;

public sealed class DriveRow
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public string TypeLabel { get; init; } = "";
    public string CapacityLabel { get; init; } = "";
    public string FreeLabel { get; init; } = "";
    public string FileSystem { get; init; } = "";
    public string RootPath { get; init; } = "";
    public DriveType DriveType { get; init; }
    public bool IsReady { get; init; }
    public bool IsWatched { get; set; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
}
