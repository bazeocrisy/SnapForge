using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SnapForge.Core.Annotations;
using SnapForge.Core.Imaging;
using SnapForge.Core.Storage;

namespace SnapForge.Core.Library;

/// <summary>Facts about a finished recording, written to its manifest.</summary>
public sealed record VideoInfo(int Width, int Height, int FramesPerSecond, long DurationMs, bool HasAudio, bool Recovered = false);

/// <summary>A recording that was interrupted (crash, power loss) before it was completed.</summary>
public sealed record InterruptedRecording(Guid Id, string VideoPath, DateTime StartedUtc, long Bytes);

/// <summary>
/// Screen recordings in the library (format: docs/LIBRARY-VIDEO-FORMAT.md). Videos live in <c>videos/&lt;id&gt;/</c>,
/// a folder that SnapForge versions without video never read, so an older version cannot list, open or rewrite a
/// video (it neither sees nor touches them). Each folder holds <c>video.mp4</c>, <c>thumb.sfpx</c>, <c>meta.json</c>
/// (same metadata as images: title, tags, folder, favorite, trash) and <c>video.json</c>, the manifest, written last.
/// The manifest records the MP4's size and (since FA-008) its streamed SHA-256 (<c>videoSha256</c>, an additive field:
/// the format version stays 1, so older versions still read it). Indexing checks only the size; the hash is checked
/// by <see cref="VerifyIntegrity"/>, backup, restore, relocation and <see cref="ExportVideo"/>.
/// While recording, the folder holds a <c>recording.inprogress</c> marker and no manifest: the item is not listed,
/// and if the app stops before completing, the folder is reported by <see cref="InterruptedRecordings"/>.
/// </summary>
public sealed partial class CaptureLibrary
{
    public const string VideosFolderName = "videos";
    public const string VideoFormatId = "snapforge.video";
    public const int VideoFormatVersion = 1;
    public const string VideoManifestFile = "video.json";
    public const string VideoFile = "video.mp4";

    /// <summary>The fragmented MP4 written while recording (crash-safe); converted to <see cref="VideoFile"/> on completion.</summary>
    public const string RecordingFile = "recording.mp4";
    public const string InProgressMarker = "recording.inprogress";

    public string VideosDirectory { get; }

    /// <summary>The MP4 file of a video in the library.</summary>
    public string VideoPath(Guid id) => Path.Combine(VideoFolder(id), VideoFile);

    /// <summary>
    /// Starts a recording: creates its folder with the in-progress marker and returns the path the MP4 is written to.
    /// The video is not listed until <see cref="CompleteVideo"/>.
    /// </summary>
    public (Guid Id, string VideoPath) BeginVideo(DateTime startedUtc)
    {
        Guid id = Guid.NewGuid();
        string dir = VideoFolder(id);
        Directory.CreateDirectory(dir);
        AtomicFile.WriteAllBytes(Path.Combine(dir, InProgressMarker), JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["format"] = VideoFormatId + ".inprogress",
            ["startedUtc"] = CaptureFormat.Iso(startedUtc),
        }));
        return (id, Path.Combine(dir, RecordingFile));
    }

    /// <summary>
    /// Completes a recording (normally, or after recovery): writes its thumbnail, metadata and finally the manifest,
    /// then removes the in-progress marker. <paramref name="thumbnailSource"/> is a frame of the video.
    /// </summary>
    public CaptureRecord CompleteVideo(Guid id, VideoInfo info, PixelBuffer? thumbnailSource, DateTime createdUtc, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        string dir = VideoFolder(id);
        string video = Path.Combine(dir, VideoFile);
        string layout = FinishVideoFile(dir);
        if (!File.Exists(video))
        {
            throw new FileNotFoundException("The recording has no video file.", video);
        }

        if (thumbnailSource is not null)
        {
            AtomicFile.WriteAllBytes(Path.Combine(dir, CaptureFormat.ThumbFile), PixelCodec.Encode(Thumbnailer.Create(thumbnailSource, ThumbnailSize)));
        }

        if (!File.Exists(Path.Combine(dir, CaptureFormat.MetaFile)))
        {
            WriteMeta(dir, new CaptureMetadata { Title = title?.Trim() is { Length: > 0 } t ? t : DefaultVideoTitle(createdUtc) });
        }

        // FA-008: a streamed SHA-256 of the final MP4 (never loaded whole) lets backup, restore, relocation and export
        // detect any modification, including same-length ones. Startup indexing still checks only the length.
        (string videoSha256, long videoBytes) = AtomicFile.HashFile(video);

        var manifest = new JsonObject
        {
            ["format"] = VideoFormatId,
            ["version"] = VideoFormatVersion,
            ["id"] = id.ToString("D"),
            ["createdUtc"] = CaptureFormat.Iso(createdUtc),
            ["modifiedUtc"] = CaptureFormat.Iso(DateTime.UtcNow),
            ["width"] = info.Width,
            ["height"] = info.Height,
            ["framesPerSecond"] = info.FramesPerSecond,
            ["durationMs"] = info.DurationMs,
            ["hasAudio"] = info.HasAudio,
            ["recovered"] = info.Recovered,
            ["container"] = "mp4",
            ["videoCodec"] = "h264",
            ["audioCodec"] = info.HasAudio ? "aac" : null,
            ["videoBytes"] = videoBytes,
            ["videoSha256"] = videoSha256,
            ["layout"] = layout,
            ["generator"] = $"{ProductInfo.Name} {typeof(CaptureLibrary).Assembly.GetName().Version}",
        };
        AtomicFile.WriteAllBytes(Path.Combine(dir, VideoManifestFile), JsonSerializer.SerializeToUtf8Bytes(manifest, AnnotationSerializer.Indented));
        File.Delete(Path.Combine(dir, InProgressMarker));
        return Refresh(id);
    }

    /// <summary>
    /// Turns the recording's fragmented MP4 into the final progressive <c>video.mp4</c> (index at the front, read the
    /// same way by every player). Falls back to keeping the fragmented file when it cannot be converted or the disk
    /// lacks room for the copy. Returns the layout of the final file ("progressive" or "fragmented").
    /// </summary>
    private static string FinishVideoFile(string dir)
    {
        string recording = Path.Combine(dir, RecordingFile);
        string video = Path.Combine(dir, VideoFile);
        if (!File.Exists(recording))
        {
            return File.Exists(video) ? "progressive" : "missing";
        }

        string temp = video + ".converting";
        try
        {
            long size = new FileInfo(recording).Length;
            string? root = Path.GetPathRoot(Path.GetFullPath(dir));
            if (root is not null && new DriveInfo(root).AvailableFreeSpace < size + (64L * 1024 * 1024))
            {
                throw new IOException("Not enough free disk space to optimise the video.");
            }

            File.Delete(temp);
            Video.Mp4Defragmenter.Convert(recording, temp);
            File.Move(temp, video, overwrite: true);
            File.Delete(recording);
            return "progressive";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            File.Delete(temp);
            File.Move(recording, video, overwrite: true);
            return "fragmented";
        }
    }

    /// <summary>Discards a recording that was cancelled (or could not be recovered): its folder is removed.</summary>
    public void AbandonVideo(Guid id)
    {
        string dir = VideoFolder(id);
        if (Directory.Exists(dir) && !File.Exists(Path.Combine(dir, VideoManifestFile)))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Recordings that were started but never completed (the app or PC stopped while recording).</summary>
    public IReadOnlyList<InterruptedRecording> InterruptedRecordings()
    {
        var list = new List<InterruptedRecording>();
        if (!Directory.Exists(VideosDirectory))
        {
            return list;
        }

        foreach (string dir in Directory.EnumerateDirectories(VideosDirectory))
        {
            string marker = Path.Combine(dir, InProgressMarker);
            if (!Guid.TryParse(Path.GetFileName(dir), out Guid id) || !File.Exists(marker) || File.Exists(Path.Combine(dir, VideoManifestFile)))
            {
                continue;
            }

            DateTime started = Directory.GetCreationTimeUtc(dir);
            try
            {
                if (JsonNode.Parse(File.ReadAllBytes(marker)) is JsonObject o && o["startedUtc"]?.GetValue<string>() is { } s)
                {
                    started = CaptureFormat.ParseIso(s);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or FormatException or InvalidOperationException)
            {
            }

            // Normally the fragmented recording; if the app stopped after converting it but before writing the
            // manifest, only the finished video.mp4 is left — report that one so it is not discarded.
            string video = Path.Combine(dir, RecordingFile);
            if (!File.Exists(video) && File.Exists(Path.Combine(dir, VideoFile)))
            {
                video = Path.Combine(dir, VideoFile);
            }

            list.Add(new InterruptedRecording(id, video, started, File.Exists(video) ? new FileInfo(video).Length : 0));
        }

        return list;
    }

    public static string DefaultVideoTitle(DateTime createdUtc) =>
        "Recording " + createdUtc.ToLocalTime().ToString("yyyy-MM-dd HH.mm.ss", CultureInfo.InvariantCulture);

    private string VideoFolder(Guid id) => Path.Combine(VideosDirectory, id.ToString("D"));

    private bool IsVideoFolder(string folder) =>
        string.Equals(Path.GetDirectoryName(folder), VideosDirectory, StringComparison.OrdinalIgnoreCase);

    private void IndexVideos(Dictionary<Guid, CaptureRecord> found)
    {
        if (!Directory.Exists(VideosDirectory))
        {
            return;
        }

        foreach (string dir in Directory.EnumerateDirectories(VideosDirectory))
        {
            if (Guid.TryParse(Path.GetFileName(dir), out Guid id) && File.Exists(Path.Combine(dir, VideoManifestFile)))
            {
                // A manifest means the recording was completed; a marker left beside it (stopped between the two
                // writes) is obsolete.
                string marker = Path.Combine(dir, InProgressMarker);
                if (File.Exists(marker))
                {
                    File.Delete(marker);
                }

                found[id] = ReadVideoRecord(id, dir);
            }
        }
    }

    private static CaptureRecord ReadVideoRecord(Guid id, string dir)
    {
        CaptureMetadata meta = ReadMeta(dir);
        long size = Directory.EnumerateFiles(dir).Sum(f => new FileInfo(f).Length);
        try
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(dir, VideoManifestFile));
            JsonObject m = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("Manifest is not an object.");
            CheckVideoVersion(m);
            string video = Path.Combine(dir, VideoFile);
            if (!File.Exists(video))
            {
                throw new InvalidDataException("The video file is missing.");
            }

            long expected = m["videoBytes"]?.GetValue<long>() ?? -1;
            if (expected != new FileInfo(video).Length)
            {
                throw new InvalidDataException("The video file is damaged or was modified (its size changed).");
            }

            return new CaptureRecord
            {
                Id = id,
                Meta = meta,
                Kind = CaptureKind.Video,
                CreatedUtc = CaptureFormat.ParseIso(m["createdUtc"]?.GetValue<string>()),
                ModifiedUtc = CaptureFormat.ParseIso(m["modifiedUtc"]?.GetValue<string>()),
                Width = m["width"]?.GetValue<int>() ?? 0,
                Height = m["height"]?.GetValue<int>() ?? 0,
                FramesPerSecond = m["framesPerSecond"]?.GetValue<int>() ?? 0,
                DurationMs = m["durationMs"]?.GetValue<long>() ?? 0,
                HasAudio = m["hasAudio"]?.GetValue<bool>() ?? false,
                Recovered = m["recovered"]?.GetValue<bool>() ?? false,
                VideoFragmented = m["layout"]?.GetValue<string>() == "fragmented",
                SizeBytes = size,
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or FormatException)
        {
            return new CaptureRecord { Id = id, Meta = meta, Kind = CaptureKind.Video, SizeBytes = size, Damage = ex.Message, CreatedUtc = Directory.GetCreationTimeUtc(dir), ModifiedUtc = Directory.GetLastWriteTimeUtc(dir) };
        }
    }

    private static void CheckVideoVersion(JsonObject m)
    {
        if (m["format"]?.GetValue<string>() != VideoFormatId)
        {
            throw new InvalidDataException("Not a SnapForge video manifest.");
        }

        int version = m["version"]?.GetValue<int>() ?? 0;
        if (version is < 1 or > VideoFormatVersion)
        {
            throw new InvalidDataException($"This video was saved by a newer version of SnapForge (format version {version}). Update SnapForge to open it.");
        }
    }

    /// <summary>Images-only operations (editing, sanitizing, packages, duplicates) refuse videos.</summary>
    private void RequireImage(Guid id)
    {
        if (Find(id)?.IsVideo == true || File.Exists(Path.Combine(VideoFolder(id), VideoManifestFile)))
        {
            throw new InvalidOperationException("This is a video. Videos cannot be opened in the image editor, sanitized or packaged.");
        }
    }

    /// <summary>
    /// Deletes a library file. Image files are overwritten with zeros first (best effort); large video files are
    /// deleted directly, because overwriting gigabytes would take minutes — documented in SECURITY.md.
    /// </summary>
    private static void DeleteLibraryFile(string file)
    {
        if (string.Equals(Path.GetFileName(file), VideoFile, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
        }
        else
        {
            AtomicFile.OverwriteAndDelete(file);
        }
    }

    /// <summary>Copies completed videos into another library root (used by <see cref="MoveTo"/>), streaming each file.</summary>
    private void CopyVideosTo(string targetRoot)
    {
        string targetVideos = Path.Combine(targetRoot, VideosFolderName);
        Directory.CreateDirectory(targetVideos);
        foreach (string dir in Directory.EnumerateDirectories(VideosDirectory))
        {
            if (!File.Exists(Path.Combine(dir, VideoManifestFile)))
            {
                continue; // in-progress or abandoned recordings stay where they are
            }

            string dest = Path.Combine(targetVideos, Path.GetFileName(dir));
            Directory.CreateDirectory(dest);

            // Manifest last, as when recording: a copy interrupted midway is not listed as a complete video.
            foreach (string file in Directory.EnumerateFiles(dir).OrderBy(f => Path.GetFileName(f) == VideoManifestFile ? 1 : 0))
            {
                AtomicFile.Write(Path.Combine(dest, Path.GetFileName(file)), s =>
                {
                    using FileStream src = File.OpenRead(file);
                    src.CopyTo(s);
                });
            }
        }
    }
}
