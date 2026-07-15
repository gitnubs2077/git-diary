namespace GitDiary.Client.Models;

/// <summary>
/// One committed image blob under a day's assets folder, as shown in the gallery.
/// <see cref="DataUrl"/> and <see cref="LoadFailed"/> are filled in lazily by the
/// gallery UI as each thumbnail resolves; everything else comes from the git tree.
/// </summary>
public sealed class GalleryImage
{
    public string Path { get; set; } = "";
    public string Sha { get; set; } = "";
    public int Size { get; set; }

    /// <summary>Resolved data: URL for display, or null until (and unless) it loads.</summary>
    public string? DataUrl { get; set; }
    public bool LoadFailed { get; set; }

    /// <summary>When the image was committed (uploaded), or null until it loads / if unknown.</summary>
    public DateTimeOffset? CommittedAt { get; set; }

    public string FileName => Path.Length == 0 ? "" : Path[(Path.LastIndexOf('/') + 1)..];
}
