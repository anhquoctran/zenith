using System;

namespace Zenith.Data;

public class Record
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public long FileSize { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public string Codec { get; set; } = string.Empty;
    public string ThumbnailPath { get; set; } = string.Empty;
}
